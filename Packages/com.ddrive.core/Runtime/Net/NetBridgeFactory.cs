using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — NGO(com.unity.netcode.gameobjects)は
    // versionDefines(DDRIVE_NGO)で任意依存に切り離した。`DDrive.Runtime` 自身は `Unity.Netcode.Runtime` を
    // 直接参照しない(参照すると NGO 未導入の持ち込み先で DDrive.Runtime ごとコンパイル対象外になり、
    // D-Drive 全体が動かなくなるため)。
    //
    // 実際の NGO ブリッジ生成(`NgoNetBridge`/`NgoTransportConfigurator`/`NetDebugOverlay` の組み立て)は
    // `DDrive.Runtime.Ngo` アセンブリ(`defineConstraints: ["DDRIVE_NGO"]`。NGO 未導入時はアセンブリごと
    // コンパイル対象外になる)側が担当し、そちらが `[RuntimeInitializeOnLoadMethod]` でこのレジストリへ
    // 自分自身を登録する(`DDriveNgoBootstrapHook`/`NgoBridgeFactoryInstaller` 参照)。
    // `DDriveRuntimeBootstrap.ResolveNetBridge()` は `NetBridgeFactoryRegistry.Current` が登録されていれば
    // それを使い、無ければ(NGO 未導入、または NGO 導入済みだがシーンに NetworkManager が無い等)警告のうえ
    // `LocalLoopbackBridge` にフォールバックする(CLAUDE.md §0-4「例外で止めない」)。
    public interface INgoBridgeFactory
    {
        // 生成に失敗したとき(シーンに NetworkManager/NgoNetBridge が見つからない等)は null を返す。
        // 呼び出し側(DDriveRuntimeBootstrap)が警告のうえ LocalLoopbackBridge にフォールバックする。
        NgoBridgeCreateResult Create(in NgoBridgeCreateArgs args);
    }

    // NGO ブリッジ生成に要る入力値。Unity 型(NetworkManager 等)は含まない
    // (DDrive.Runtime アセンブリからも安全に扱えるようにするため)。
    public readonly struct NgoBridgeCreateArgs
    {
        public readonly NetLaunchRole Role;
        public readonly string Host;
        public readonly ushort Port;
        public readonly NetLaunchOptions LaunchOptions;
        public readonly bool ShowDebugOverlay;

        // NetDebugOverlay を Bootstrap と同じ寿命(DontDestroyOnLoad/破棄)にするための親 Transform。
        // 元 DDriveRuntimeBootstrap.ResolveNetBridge が `overlayGo.transform.SetParent(transform, false)`
        // としていたのと同じ挙動を維持するために渡す(Transform 自体は NGO 非依存の UnityEngine 型)。
        public readonly Transform ParentTransform;

        // [M-1d、2026-09-25] ShowDebugOverlay=true でも、リリースビルド(Debug.isDebugBuild/Application.isEditor
        // のどちらも false)では既定でオーバーレイを生成しない(NgoBridgeFactory.Create 参照)。この値が true の
        // ときだけリリースビルドでも生成する(DDriveRuntimeBootstrap.ShowNetDebugOverlayInRelease の値をそのまま運ぶ、
        // 追加のみのフィールドなので既存の 6 引数コンストラクタ経由で作られた既存呼び出し元は false のまま)。
        public readonly bool ShowDebugOverlayInRelease;

        public NgoBridgeCreateArgs(NetLaunchRole role, string host, ushort port, NetLaunchOptions launchOptions, bool showDebugOverlay, Transform parentTransform)
        {
            Role = role;
            Host = host;
            Port = port;
            LaunchOptions = launchOptions;
            ShowDebugOverlay = showDebugOverlay;
            ParentTransform = parentTransform;
            ShowDebugOverlayInRelease = false;
        }

        // [M-1d、2026-09-25] ShowDebugOverlayInRelease を指定できる追加コンストラクタ(既存の 6 引数版は
        // 互換性のため残したまま、オーバーロード追加として提供する。[42_distribution.md] §5.4 MINOR)。
        public NgoBridgeCreateArgs(NetLaunchRole role, string host, ushort port, NetLaunchOptions launchOptions, bool showDebugOverlay, Transform parentTransform, bool showDebugOverlayInRelease)
        {
            Role = role;
            Host = host;
            Port = port;
            LaunchOptions = launchOptions;
            ShowDebugOverlay = showDebugOverlay;
            ParentTransform = parentTransform;
            ShowDebugOverlayInRelease = showDebugOverlayInRelease;
        }
    }

    public sealed class NgoBridgeCreateResult
    {
        public INetBridge Bridge;

        // Start() のタイミングで呼ぶ(NetworkManager.Awake/OnEnable が済んでからでないと
        // StartHost/StartClient が NullReferenceException になるため、実際の呼び出しを遅延する。
        // 元 DDriveRuntimeBootstrap.StartNetworkingIfPending と同じ理由)。role が Manual のときは
        // null(自動開始しない。[14_networking.md] N-1、2026-09-22)。
        public Action PendingStart;

        // NetHashGate は ResolveNetBridge() の戻り値を受け取った"後"に Bootstrap 側で生成されるため、
        // NetDebugOverlay へは生成後にコールバック経由で渡す(CatalogContentHashGate は DDrive.Runtime 側の
        // 型なので Ngo アセンブリからも安全に参照できる)。
        public Action<CatalogContentHashGate> AssignHashGate;

        // [14_networking.md] N-1(2026-09-22) — 開発用の手動接続 API(DDriveRuntimeBootstrap.StartHost/
        // StartClient/StopNetworking/IsNetworkStarted)が使う。`NetworkManager`/`NgoNetBridge` 型を
        // DDrive.Runtime アセンブリへ露出させずに済むよう、実処理は全て Ngo アセンブリ側の delegate に
        // 閉じ込める(既存の PendingStart/AssignHashGate と同じパターン)。役割(Host/Client/Manual)に
        // 関わらず常に渡す(Auto で起動済みのセッションを後から StopNetworking() で止める用途にも使えるため)。
        public Func<bool> IsListening;
        public Func<ushort, bool> ManualStartHost;
        public Func<string, ushort, bool> ManualStartClient;
        public Action ManualStop;
    }

    public static class NetBridgeFactoryRegistry
    {
        public static INgoBridgeFactory Current { get; set; }
    }
}
