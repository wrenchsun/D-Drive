# 14. ネットワーク（通信）設計

関連: [01_architecture.md](01_architecture.md) / [02_core_framework.md](02_core_framework.md) / [08_presentation.md](08_presentation.md)

---

## 1. 基本方針

**アセットシステム本体はトランスポート非依存**（Netcode for GameObjects / Photon / Mirror のどれでも動く）。ただし以下の 3 点を最初から基盤に織り込む。後付けが最も困難な部分だからである。

1. **ID がそのままネットワークメッセージになる** — 「何を再生するか」を Prefab 参照でなく ulong ID で送れる。本システムの ID 駆動設計はネットワークと本質的に相性が良い
2. **全アセットに複製区分（NetMode）を持たせる** — 「クライアント演出」か「サーバー権威」かをデータで宣言する
3. **再生の再現は Instance 同期ではなくイベント + 経過時間で行う** — 遅参加者(Late Join)・観戦に必要

```
Server                                 Client A / B / C
Presentation.Play(SkillSlash, ctx)
  │  権威判定・ゲームロジック
  └─▶ NetBridge.Broadcast(
        PlayMsg{ presId, selfNetId,     ──▶ 受信 → Presentation.PlayRemote(msg)
                 targetNetId, pos,           → ローカルの各 Manager で再生
                 serverTime })                 (SE/VFX はクライアント側で自律実行)
```

## 2. INetBridge（トランスポート抽象）

```csharp
public interface INetBridge          // Foundation 層。実装はアダプタとして差し替え
{
    bool IsServer { get; }
    bool IsClient { get; }
    double NetworkTime { get; }                       // 全端末で単調同期した時刻
    void Broadcast<T>(in T msg, NetChannel ch) where T : INetMessage;
    void SendTo<T>(ulong clientId, in T msg, NetChannel ch);
    IDisposable Subscribe<T>(Action<ulong, T> handler);
    Transform ResolveNetObject(ulong netId);          // NetId → Transform
}
// NetChannel: ReliableOrdered / Unreliable(演出用)
```

- シングルプレイでは `LocalLoopbackBridge`（自分に即時配送）を挿す → **ゲームコード・データは通信の有無で一切変わらない**
- アダプタは v1 で NGO（Netcode for GameObjects **2.13.2**）実装 `NgoNetBridge` を 1 つ用意。Photon 等は同インタフェースで追加

### NgoNetBridge の配送規則（2026-09-08 改定・MS2026 統一）

| 呼び出し | 実装 | 備考 |
|---|---|---|
| Host から `Broadcast` | `[Rpc(SendTo.ClientsAndHost)]` | **ホスト自身にも届く**。旧 `[ClientRpc]` は NGO 2.x では `SendTo.NotServer` 扱いでホストに届かず、ホストの Manager が Cosmetic を再生できなかった |
| Client から `Broadcast` | `[Rpc(SendTo.Server)]` で Host に「依頼」→ Host がレート制限（60/秒/クライアント）と種別登録を検証して全員へ配る | §9 の「無条件中継しない」の実装。Simulated はこの経路を通さない |
| Host から `SendTo(clientId)` | `[Rpc(SendTo.SpecifiedInParams)]` + `RpcTarget.Single` | Client からの呼び出しは警告して破棄 |
| `NetChannel.Unreliable` | `RpcDelivery.Unreliable`。ペイロードが 1000 bytes を超える場合は Reliable にフォールバック | NGO の Unreliable は 1 パケット(MTU)制限があるため |
| ログ | `[Net/Host]` / `[Net/Client]` プレフィックス | MS2026 Networking.md §5 と同じ |

## 3. NetMode（AssetFlags 拡張）

```csharp
public enum NetMode
{
    Local,        // 完全ローカル (UI, メニューSE, 自分専用演出)
    Cosmetic,     // 全クライアントで再生するが結果に影響しない (SE/VFX/CameraShake)
                  //   → Unreliable 配送可・欠落しても再送しない・クライアント自律実行OK
    Simulated,    // ゲーム結果に影響する (PrefabのSpawn, 当たり判定持ちProjectile)
                  //   → サーバー権威。クライアントは要求のみ、生成はサーバー
}
```

- `AssetFlags.Net : NetMode` を全種別共通で追加（[02] §2）
- 既定値: Audio/VFX/Canvas/Material = Cosmetic または Local、Prefab(Projectile 等) = Simulated
- Manager は Play/Spawn 時に NetMode を見て自動で配送経路を選ぶ。**プログラマーは通常 API を呼ぶだけ**

## 4. 種別ごとの通信規則

| 種別 | 規則 |
|---|---|
| SE / BGM | 常にクライアントローカル実行。Cosmetic はイベントとして受信して各自再生（音量・距離減衰は各クライアントのリスナー基準） |
| VFX | Cosmetic。`VfxNetMsg{ vfxId, anchorNetId or pos, paramOverrides }`。欠落許容（Unreliable） |

> **実装メモ(2026-07-27, Phase 2 の 2-8 時点)**: SE/VFX とも `SeNetMsg`/`VfxNetMsg{ SeId/VfxId, AnchorNetId(予約, 常に0), Position }` を実装済み。**`Position` のみが実際に使われる**(送信時点でのワールド座標を1回送るだけで、受信側でのアンカーへのライブ追従は行わない)。`AnchorNetId` は将来の NGO アダプタ向けの予約フィールド — `INetBridge` に Transform→NetId の逆引きが無く、Foundation 層だけでは安全に解決できないため。`paramOverrides` の同期も未実装（VFX の Params は各クライアントのローカルデフォルト値で再生される）。Cosmetic 指定時、Play/Spawn 呼び出しは即座に再生せず内部バッチへ積まれ、同一 Tick 内の呼び出しは1つの `SeNetBatchMsg`/`VfxNetBatchMsg` にまとめて Broadcast される（§8）。送信元自身も Broadcast を自分で受信して初めて再生する（直接再生しない。二重再生防止）ため、`PlaySe`/`Vfx.Spawn` は Cosmetic データに対して常に Invalid ハンドルを返す。
| Animation | 原則 NetworkAnimator 等の既存同期に任せ、本システムの Frame イベント（SE/VFX）は**各クライアントがローカルの Animator から発火**（イベントを送らない = 帯域ゼロ・ズレなし） |
| Prefab | Simulated はサーバーが `Prefabs.Spawn` → NetBridge の NetworkObject 複製。PrefabData に `NetworkPrefab` 検証（§8） |
| Canvas / UI | 常に Local。ネット対象外 |
| Material | Cosmetic（スキン替え等は見た目のみ）。装備など結果に影響する場合はゲームロジック側の同期変数から駆動 |
| Presentation | §5 参照。ネット対応の主役 |

> **実装メモ（2026-09-11、4-13）**: `PrefabsManager` に `INetBridge netBridge = null` を追加（既定 null = シングルプレイ相当で今までどおり常にローカル Spawn）。`Assets/DDrive/Runtime/Net/PrefabMessages.cs` に3種のメッセージを追加。
> - `PrefabSpawnRequestMsg{ PrefabId, Position, Rotation, RequestKey }`: クライアント→サーバーの Spawn 要求。`netBridge.IsServer == false` のとき `SpawnData` はローカル Instantiate せず、`SendTo(serverClientId=0, ...)` でこのメッセージを送って `Handle<PrefabMarker>.Invalid` を返す（クライアントは Simulated Prefab のローカル Handle を一切持たない。観測は将来 NGO の NetworkObject 経由に委ねる）。
> - `PrefabSpawnedMsg{ PrefabId, NetObjectId, Position, Rotation, RequestKey }`: サーバーが権威生成した直後に `Broadcast` する通知のみのメッセージ。`NetObjectId` は現状常に 0（`LocalLoopbackBridge` 経路。NGO の `NetworkObjectId` 配線は Phase 6）。
> - `PrefabDespawnedMsg{ NetObjectId }`: サーバーが Simulated インスタンスを Despawn した際に `Broadcast`。
> - サーバー（`netBridge.IsServer == true`）は `PrefabSpawnRequestMsg` を Subscribe し、クライアントごとに 60 件/秒のレート制限（`NgoNetBridge.ConsumeRelayBudget` と同じ考え方の簡易ウィンドウ）を掛けたうえで、`PrefabId` がレジストリ上 `NetMode.Simulated` の `PrefabData` に解決できる場合のみ権威 Spawn する。非 Simulated ID への要求は ID ごとに 1 回だけ警告して無視する。
> - Cosmetic/Local な Prefab は今までどおり無条件でローカル Spawn（挙動変更なし）。
> - Validator（`PrefabDataValidator`）: `NetMode.Simulated` かつ Prefab に `Unity.Netcode.NetworkObject` が無い → Error。`Kind` が Projectile/Gimmick/Character 以外 → Info。`Flags.Pool.Kind == Pooled` との併用 → Warning。
> - 見送り: 実際の NGO `NetworkObject` 複製（`NetworkManager.SpawnManager.InstantiateAndSpawn` 等）と `NetObjectId` の実配線、Late Join 時の Simulated インスタンス一覧のスナップショット同期は Phase 6（NGO 統合）で行う。テストは `PrefabSimulatedSpawnTests`（`FakeNetBridge` で IsServer/IsClient を切替、ループバック配送は `SendTo`/`Broadcast` が同一インスタンス内の Subscribe ハンドラへ即時配送する簡易実装）。

## 5. Presentation のネットワーク再生

```csharp
// サーバー(または行為者)側 — 通常APIのまま。NetMode=Cosmetic なら自動 Broadcast
Presentation.Play(PRESENTID.SkillSlash, ctx);

[NetMessage] struct PresentationPlayMsg
{
    ulong PresId;
    ulong SelfNetId; ulong TargetNetId;   // 0 = なし → Position 使用
    Vector3 Position;
    double StartNetTime;                  // ★NetworkTime 基準の開始時刻
    ushort Seed;                          // ランダム要素の同期(SE選択等)
}
```

- 受信側は `PlayRemote(msg)`: `NetworkTime - StartNetTime` 分だけ**シークして再生開始**（遅延分を吸収し全端末で位相が揃う）
- `OnSignal("hit")` トラック: ヒット判定はサーバー権威 → サーバーが `SignalMsg{ handleNetKey, "hit" }` を Broadcast。クライアントの HitStop/Shake はそれを受けて発火
- 行為者クライアントは**予測再生**（自分の入力に対し即時ローカル再生）可。フラグ `PredictLocal` を PresentationData に持ち、サーバー確定と重複しないよう handle キーで抑制
- Late Join / 観戦: ループ中の Presentation・常駐 VFX は「アクティブ演出リスト（presId + StartNetTime + ctx）」をサーバーが保持し、接続時スナップショットとして送る → 受信側はシーク再生で復元。ワンショット演出は復元しない（設計として割り切る）

## 実装メモ（2026-09-14、5-8）

実装: `Runtime/Net/PresentationMessages.cs`（新規: `PresentationPlayMsg`/`PresentationSignalMsg`/`PresentationCancelMsg`）、`Runtime/Presentation/PresentationManager.cs`（ネット再生本体）、`Runtime/Presentation/PresentationData.cs`（`PredictLocal` フィールド追加）、`Runtime/Presentation/PresentationDataValidator.cs`（Info 検査追加）、`Runtime/Loop/DDriveRuntimeBootstrap.cs`（`netBridge: NetBridge` を渡すよう変更）。テストは `Tests/Runtime/PresentationNetTests.cs`（新規）。Late Join（5-9）は依存チケットのため別コミットで追記する（下記§5実装メモ「5-9」参照）。

- **メッセージ設計**: `PresentationPlayMsg{ PresId, SelfNetId, TargetNetId, Position, StartNetTime, Seed, HandleNetKey }` / `PresentationSignalMsg{ HandleNetKey, SignalKeyHash }` / `PresentationCancelMsg{ HandleNetKey }`。SelfNetId/TargetNetId は SE/VFX の Cosmetic メッセージ（`CosmeticMessages.cs`）と同じ既知の制約により常に 0 で送る（§4 実装メモ参照）。`HandleNetKey` は送信者(行為者)が `PresentationManager.NextHandleNetKey()`（インスタンスごとの乱数 salt ⊕ NetworkTime のビット ⊕ ローカル連番、0 は予約して返さない）で 1 回だけ生成し、以後の Signal/Cancel/Late-Join スナップショットの突き合わせキーとして使う（`PrefabSpawnRequestMsg.RequestKey` と同じ考え方）。**要判断**: 32bit の salt/連番方式は衝突確率が十分低いという判断だが、`INetBridge` にまだ `LocalClientId` が無いため厳密な一意性(clientId を上位ビットに埋める等)は保証していない。NGO 統合(Phase 6)で `LocalClientId` 相当が手に入ったら見直すこと。
- **SignalKey の帯域節約**: 事前登録テーブル方式ではなく、16bit FNV-1a ハッシュ（`PresentationManager.HashSignalKey`、0 alloc・純関数）を採用した。同一 Presentation 内で衝突する可能性はゼロではないが、実運用の SignalKey 種類数は少数（"hit" 等）のため許容した。
- **NetChannel**: Play/Signal/Cancel はいずれも `NetChannel.ReliableOrdered` で送る（§8 のバッチ化・Unreliable は VFX/SE 単位の高頻度イベント向けであり、Presentation の Play/Signal/Cancel は頻度が低く、欠落してよい類のイベントでもないため）。SE/VFX のような同一 Tick 内バッチ化も行わない(Presentation の呼び出し頻度は VFX/SE ほど高くないと想定した設計判断)。
- **配送経路**: `PresentationManager` は `AudioManager`/`VfxManager`/`PrefabsManager` と同じ形で `INetBridge netBridge = null` を受け取る（null=シングルプレイ相当で常にローカル、既存の原則どおり）。`Flags.Net == NetMode.Cosmetic` かつ `netBridge != null` のときだけネット経路に乗る。`NetMode.Simulated` は Presentation には意味を持たせず(Info 検査で警告)、通常のローカル再生にフォールバックする。
- **予測再生と重複抑制**: `PresentationData.PredictLocal=true` のとき、`Play()` 呼び出し元(行為者)は即座にローカル Instance を生成して `Handle` を返す(`FireDueTracks` 相当が同期的に走る)と同時に Broadcast する。Broadcast は Host/Client 問わず(NGO では Client→Host→全員の中継を `NgoNetBridge` が既存のとおり行う。Loopback/テストでは直接)、送信元自身にも `ClientsAndHost` 経由で返ってくる。受信側は `HandleNetKey` を `_networkedHandles` で引き、既に自分の Instance(予測済みまたは受信生成済み)があれば**新規生成せず**、Host なら「アクティブ演出リスト」への登録だけ行う(二重発火・二重生成を防ぐ)。`PredictLocal=false` の Presentation は SE/VFX の既存 Cosmetic と同じく、自分の Broadcast を受信して初めて再生する。
- **開始時刻シーク（`PlayRemote` = `OnReceivePlayMsg`）**: `elapsed = max(0, NetworkTime - StartNetTime)`。`duration > 0 && elapsed >= duration` なら「到着時点で既に終わっている演出」として復元しない（Late Join のワンショット非復元と同じロジックを再利用）。それ以外は `Elapsed = elapsed` で Instance を作り、`Time <= elapsed` の AtTime トラックのうち **one-shot は(2026-09-14 修正、下記「6-0 修正6」参照)猶予以内なら遅れて発火・猶予より古ければスキップ**（`Fired` だけ立てる）、**continuous(ループ)系だけ今から再生開始**する。判定は `PresentationManager.IsContinuousAtSeek`: Anim/Anim2D/Bgm は常に continuous、**Vfx/Se はデータ側のループ設定（`VfxData.LifeMode==Loop` / `SeData.Loop`）を見て判定**する（一撃 VFX・単発 SE はワンショットのままスキップし、常駐 VFX・ループ SE だけ復元対象にする。5-9 の「途中参加でループ VFX/BGM が復元」AC に必要な判定で、当初は Kind 単位の固定分類だけだったが Vfx を一律ワンショット扱いにしていたため late-join の VFX 復元テストが失敗し、この形に修正した)。Anim/Anim2D はさらに `AnimManager.Seek(handle, normalizedTime)` で位相を合わせる（`AnimData.LengthSec` から算出。**要判断**: Bgm は `BgmManager` に Seek API が無いため頭から再生するだけで位相は合わせていない。厳密な同期が必要になったら `BgmManager` 側に再生位置指定 API を追加すること）。
- **Signal 中継**: `handle.Signal(key)` は Instance が `IsNetworked` なら直接発火せず `PresentationSignalMsg` を Broadcast する（Host 権威。Client 発は `NgoNetBridge` が既存の「Client→Host 依頼→レート制限検証→全員へ配る」経路を通るため、Presentation 側で追加の検証コードは書いていない。受信側で `HandleNetKey` が未知なら何もしない、というのが ID 未検証時の安全側フォールバックになっている）。受信側 `OnReceiveSignalMsg` は `SignalKeyHash` が一致する未発火の OnSignal トラックを発火する。
- **Cancel 中継**: 同様に `PresentationCancelMsg` を Broadcast してから、自分を含む全員が受信して初めて `CancelInternal` する（直接 Cancel すると Broadcast 前に自分だけ止まってしまうため）。
- **HitStop は全員が実行する（観戦者を区別しない、既定）**: §6 の「HitStop はローカル演出として各自実行」を、MS2026 の 1v1 前提（当事者は必ず 2 人だけ）ではそのまま「Signal を受け取った全ピアが同じように HitStop する」でよいと判断した。**要判断**: 将来 3 人以上の観戦者が入る構成になった場合、観戦者は HitStop すべきでない可能性があるため、その時点で PlayContext 側に「当事者かどうか」を判定する仕組みを追加すること。**→ 2026-09-22 N-4 で解消**（PresentationTrack.Scope=ParticipantsOnly。既定は Everyone のままで挙動は変えない。§17 参照）。
- **Haptic の LocalPlayerOnly 誤爆防止（オーケストレーターの追加指示、2026-09-14）**: `SelfNetId`/`TargetNetId` を常に 0 で送る既知の制約により、受信側は「この事象が自分に起きたことか」を判定できない。そのため `PresentationInstance` に `PlayedViaNetworkReceive`(= `PresentationPlayMsg` を受信して生成した Instance かどうか。予測再生した行為者自身の Instance は false)を持たせ、`FireHaptic` で `PlayedViaNetworkReceive && HapticsData.LocalPlayerOnly` のときは再生をスキップする（誤爆防止の安全側デフォルト）。**要判断**: この結果、`PredictLocal=false` の Presentation では行為者自身も(自分の Broadcast を受信して初めて再生する経路を通るため) `PlayedViaNetworkReceive=true` になり、LocalPlayerOnly な Haptic が鳴らない。真に「自分の事象か」を判定するには `INetBridge` に `LocalClientId` 相当を追加し `SelfNetId` と比較する必要がある(Phase 6、NGO 統合時に見直す)。当面、行為者自身にも確実に Haptic を鳴らしたい演出は `PredictLocal=true` にする運用で回避できる。
- **DDriveRuntimeBootstrap**: `Presentation = new PresentationManager(..., NetBridge)` に変更。現状 `NetBridge` は常に `LocalLoopbackBridge`(NGO 統合は Phase 6)のため、この変更自体はランタイムの挙動を変えない(Audio/Vfx/Prefabs と同じ配線パターンに揃えただけ)。
- **見送り(Phase 6 へ)**: `NgoNetBridge` の実配線(Bootstrap への差し込み)、`SelfNetId`/`TargetNetId`/`LocalClientId` の実解決、SE のランダム選択(`Seed` の実消費)、`PresentationSignalMsg`/`PresentationCancelMsg` へのクライアント別レート制限の追加検証(現状は `NgoNetBridge` の汎用レート制限のみに依存)。

## 実装メモ（2026-09-14、5-9）

実装: `Foundation/Net/INetBridge.cs`（`event Action<ulong> ClientConnected` 追加）、`Foundation/Net/LocalLoopbackBridge.cs` / `Runtime/Net/NgoNetBridge.cs` / `Tests/Runtime/{FakeNetBridge,CountingNetBridge}.cs`（同イベントの実装を追加）、`Runtime/Presentation/PresentationManager.cs`（アクティブ演出台帳 + Late Join 送信、5-8 で追加した基盤の上に積む）。テストは `Tests/Runtime/PresentationLateJoinTests.cs`（新規）。

- **Late Join**: `INetBridge` に `event Action<ulong> ClientConnected` を追加した(最小限の新規接続通知)。`LocalLoopbackBridge`/`Tests/Runtime/{FakeNetBridge,CountingNetBridge}` は手動発火用の `RaiseClientConnected(clientId)` を持つ(シングルプレイでは通常発火しない)。`NgoNetBridge` は `OnNetworkSpawn`/`OnNetworkDespawn` で `NetworkManager.OnClientConnectedCallback` を中継する。`PresentationManager` は Host(`_netBridge.IsServer`)のときだけ「アクティブ演出台帳」(`_activeNetworked: Dictionary<HandleNetKey, {Data, Ctx, StartNetTime, Seed}>`)を保持し、`OnClientConnected` で台帳の全エントリを新規クライアントへ `SendTo`(専用の Late Join メッセージは用意せず、既存の `PresentationPlayMsg` をそのまま送ることで `OnReceivePlayMsg` の開始時刻シーク/ワンショットスキップ・LocalPlayerOnly 判定をそのまま再利用する)。台帳への登録・削除は Instance の生成(`OnReceivePlayMsg`/予測確定)・消滅(`Cleanup`、Complete/Cancel 双方)に同期させているため、**ワンショットは尺が短いため自然に台帳から外れて復元されない**(専用の「これは復元しない」フラグを増やさない設計判断)。

## 実装メモ（2026-09-14、6-0 NGO 統合 + 実機 2 台確認環境）

実装: `Foundation/Net/INetBridge.cs`（`LocalClientId`/`ResolveNetId`/`IsLocalPlayerObject`/`SpawnNetworked`/`DespawnNetworked` を追加）、`Foundation/Net/LocalLoopbackBridge.cs` / `Runtime/Net/NgoNetBridge.cs` / `Tests/Runtime/{FakeNetBridge,CountingNetBridge,PresentationNetTests.DelayedNetBridge}`（同インタフェースの実装）、`Runtime/Net/NetLaunchArgs.cs`（新規）、`Runtime/Net/NgoTransportConfigurator.cs`（新規）、`Runtime/Net/NetDebugOverlay.cs`（新規）、`Runtime/Loop/DDriveRuntimeBootstrap.cs`（ブリッジ選択）、`Runtime/Presentation/PresentationManager.cs`（発行者検証・SelfNetId/TargetNetId 実解決・Seed 実消費）、`Runtime/Vfx/VfxManager.cs`/`Runtime/Audio/AudioManager.cs`（AnchorNetId 実解決）、`Runtime/Prefab/PrefabsManager.cs`（Simulated Prefab の Host 生成経路）、`Samples/NetCheckRunner.cs`（新規）、`Editor/Build/NetCheckBuilder.cs`（新規）。テストは `Tests/Editor/NetLaunchArgsTests.cs`（新規）、`Tests/Runtime/PresentationNetSecurityTests.cs`（新規）+ 既存テストの拡張。

- **A（ブリッジ選択）**: `DDriveRuntimeBootstrap.NetBridgeMode`(`Loopback`/`Ngo`。既定 `Loopback`)を Inspector に追加。`ResolveNetBridge()` が `NetLaunchArgs.Parse(Environment.GetCommandLineArgs())` の結果(未指定なら Inspector 既定値)で最終ロールを決め、`Loopback` なら `LocalLoopbackBridge` を new する(既存どおり)。`Ngo` のときは **Bootstrap 自身は `NetworkManager` を生成しない**(シーンに `NetworkManager`+`UnityTransport`+`NetworkObject`+`NgoNetBridge` を置く設計を選んだ。`NetworkManagerRef`/`NgoBridgeRef` の Inspector 直参照、未設定なら `FindAnyObjectByType` で自動検索)。見つからなければ警告して `LocalLoopbackBridge` にフォールバックする(例外で止めない)。見つかった場合は `NgoTransportConfigurator.TryConfigure` で IP/Port/シミュレータを設定してから `NetworkManager.StartHost()`/`StartClient()` を呼ぶ。**要判断**: 「NetworkManager をシーンに置く」と「Bootstrap が実行時に動的生成する」のどちらにするかは要判断だったが、UnityTransport の設定(ConnectionData 等)をあらかじめシーン上で調整できる・NGO の一般的な使い方に近い、という理由でシーン配置を選んだ。
- **B（起動引数・オーバーレイ）**: `NetLaunchArgs`(Unity API 非依存の純関数パーサ、EditMode テストで検証)が `-ddrive-net host|client|off`/`-ddrive-host`/`-ddrive-port`/`-ddrive-sim-latency`/`-ddrive-sim-loss`/`-ddrive-autotest` を解釈する。`NgoTransportConfigurator` が IP/Port とシミュレータ(`UnityTransport.SetDebugSimulatorParameters(packetDelay, packetJitter, dropRate)`。`dropRate` は 0-100 の%そのもの、実行時リフレクションで確認済み)を設定する。**要判断(asmdef)**: `UnityTransport` クラス自体は `Unity.Netcode.Runtime`(既参照)に同梱されているが、そのメソッドの一部オーバーロードが `Unity.Networking.Transport.NetworkEndpoint`(未参照アセンブリ)を引数に取るため、素直に `using Unity.Netcode.Transports.UTP;` して直接呼ぶとコンパイラがオーバーロード解決のために未参照アセンブリの読み込みを要求し `CS0012` でコンパイルエラーになる(実際に確認した)。asmdef に `Unity.Networking.Transport` を追加すれば解決するが、6-0 では asmdef 変更を避け、リフレクションで名前解決して呼ぶことで回避した(型/メソッドが見つからない場合は警告 1 回のみで no-op)。`NetDebugOverlay`(`OnGUI`。画面左上に役割/ClientId/NetworkTime/RTT/受信数)は `ShowNetDebugOverlay`(既定 ON)で Ngo モードのときだけ追加される。RTT は `NetworkTransport.GetCurrentRtt(clientId)`(基底クラス API、`ulong`/`NetworkEndpoint` を経由しないため未参照アセンブリの問題が起きない)。
- **C（NetId 実解決）**: `INetBridge.ResolveNetId(Transform)`(`ResolveNetObject` の逆方向)を追加。`LocalLoopbackBridge` は `RegisterNetObject` 時に逆引き辞書も同時に埋める。`NgoNetBridge` は `transform.GetComponentInParent<NetworkObject>()` が `IsSpawned` なら `NetworkObjectId` を返す(それ以外は 0、既存の Position フォールバックへ)。`VfxNetMsg`/`SeNetMsg` の `AnchorNetId` は `contextRoot` から解決した実値を送るようになり(§4 の 2026-07-27 実装メモの制約を解消)、受信側も `AnchorNetId!=0` のときはそこへ**追従再生**する(解決できないときは既存どおり送信時点の Position 固定)。`PresentationPlayMsg.SelfNetId`/`TargetNetId` も同様に `ctx.Self`/`ctx.Target` から解決する。**Haptics の LocalPlayerOnly**: `INetBridge.IsLocalPlayerObject(Transform)`(NGO は `NetworkObject.OwnerClientId == LocalClientId`、Loopback は常に true)を追加し、`FireHaptic` は `PlayedViaNetworkReceive && LocalPlayerOnly` のとき「`ctx.Self`/`ctx.Target` が解決できて、かつ自分の所有物」であれば再生し、それ以外(未解決 or 自分ではない)は従来どおり安全側で再生しない(2026-09-14 の要判断を解消)。**Simulated Prefab の Host 生成**: `INetBridge.SpawnNetworked(GameObject root)`/`DespawnNetworked(ulong,bool)` を追加。`PrefabsManager.SpawnData` は Host かつ `NetMode.Simulated` のとき、Pool から借りた `root`(既存の Instantiate 経路のまま)に対して `SpawnNetworked` を呼び、返ってきた `NetworkObjectId` を `PrefabSpawnedMsg.NetObjectId` に載せる(`LocalLoopbackBridge` は常に 0 = 挙動不変)。`Despawn` は `IsPooled` かどうかで `destroy` フラグを分ける(Pool へ戻す場合は `destroy:false`)。**要判断**: Pool から再度 Rent されたときに NetworkObject を再 Spawn する経路はまだ無い(Pooled な Simulated Prefab を Despawn→再 Rent する運用がある場合は追加実装が必要)。
- **D（実機確認シーン + 自動確認）**: `Assets/GameData/PreviewScenes/NetCheckScene.unity`(Editor API で作成)。`[D-Drive] Runtime`(`GameLoopDriver`+`DDriveRuntimeBootstrap`、`DefaultNetBridge=Ngo`)、`NetworkManager`(`NetworkManager`+`UnityTransport`+`NetworkObject`+`NgoNetBridge`+`NetBridgeSmokeTest`。`NetworkConfig.Prefabs` に既存の `Assets/DefaultNetworkPrefabs.asset` を割り当て)、`Actor`(`NetCheckRunner`)。`NetCheckRunner` は Host なら `PRES_Demo_SkillSlash` を周期再生 → `Signal("hit")`、全ピアで `[DDriveNetCheck] key=value` 形式のハートビート(`activeCount`/`NetworkTime` 等)を出す。Client 役は定期的に偽造 `PresentationCancelMsg`(存在しない `HandleNetKey`)を `Broadcast` し、F の発行者検証で破棄されることをログ(`[Net/Host]`/`[Net/Client]` の警告)で確認できるようにした。`-ddrive-autotest <name>` 指定時は一定時間後に自動終了する。判定基準・起動コマンドは [docs/29](29_network_device_test.md) §3/§4。
- **E（ビルド）**: `DDrive.Editor.Build.NetCheckBuilder`(`Tools > D-Drive > Build > 実機確認用 Windows 開発ビルド` + `Build()` static メソッド)。`BuildPlayerOptions.scenes = [NetCheckScene のパス]` を明示指定するため `EditorBuildSettings.scenes` は変更しない。出力 `Builds/DDriveNetCheck/DDriveNetCheck.exe`(development build)→ `Builds/DDriveNetCheck.zip`(`System.IO.Compression.ZipArchive` で手動圧縮。`ZipFile.CreateFromDirectory`/`CreateEntryFromFile` は `System.IO.Compression.FileSystem` アセンブリの拡張メソッドで asmdef 変更が必要になるため避けた)。`Builds/` は `.gitignore` に追加した。
- **F（P5 レビュー第 2 弾対応）**: 詳細は `docs/30_phase5_review_2026-09-14.md` の「第 2 弾（ネット）」節。要旨: `HandleNetKey` の上位 8bit に発行者(`LocalClientId` の下位 8bit)を埋め込み、`PresentationManager.IsAuthorizedSender(senderId, handleNetKey)`(発行者一致 or Host=0 からの信頼された中継/再送)で Signal/Cancel/Play(既知キー分岐)を検証してから処理する(不一致は警告して破棄)。受信 Cancel は `Interruptible` を再チェックする。`PredictLocal && IsServer` は確定エコーを待たず即時台帳登録。`OnClientConnected` は自己接続を early-return し、台帳をスナップショット配列にしてから送る。テスト用 `DelayedNetBridge`/`FakeNetBridge` に `LocalClientId` と(`FakeNetBridge`)Client→Host 依頼経路相当の検証(型登録+レート制限)を追加し、Client 行為者の Play/Signal/Cancel と偽造メッセージ破棄のテストを追加した。SE の `Seed` は `AudioManager.PlaySeData(..., seed:)` → `SelectClip`/`ConfigureSource` が `System.Random(seed)` から Clip/Pitch を決定的に選ぶよう接続した(ローカル再生は未指定のまま `UnityEngine.Random` を使い続けるため挙動不変)。
- **NgoNetBridge の発行者伝達バグ修正(P1-2 の前提)**: 6-0 着手前の `NgoNetBridge.Dispatch` は受信メッセージの `senderId` を常に `NetworkManager.ServerClientId` としていたため、Client 発のメッセージが Host 経由で中継された後は全ピアで「Host から来た」ものとして見えてしまい、発行者検証が原理的に機能しなかった。`ReceiveRpc`/`ReceiveUnreliableRpc`/`ReceiveToRpc`/`ReceiveUnreliableToRpc` に `originClientId` パラメータを追加し、`RequestBroadcastRpc` が `rpcParams.Receive.SenderClientId`(トランスポートが付与する真の値。クライアントは偽装できない)をそのまま中継するよう変更した。
- **見送り(6-0 のスコープ外)**: カタログ ContentHash 照合(Phase 6-5)、Simulated Prefab のプール再利用時の再 Spawn、`NetworkPrefabsList` への D-Drive 側 Prefab の実登録(NetCheckScene では既存の空リストを割り当てただけ)、`-ddrive-autotest` の詳細な成否判定(現状はログ出力のみで pass/fail の自動判定はしない)。
- **ローカル結合確認で見つかった実バグ 2 件(重要)**: (1) `NetworkManager.StartHost()`/`StartClient()` を `DDriveRuntimeBootstrap.Awake()`(`DefaultExecutionOrder(-1000)`)から直接呼ぶと、`NetworkManager` 自身の `Awake()`/`OnEnable()` が済む前に呼ばれてしまい `NullReferenceException` になる。→ `ResolveNetBridge()`(Awake 内)は役割決定・Transport 設定のみを行い、実際の `StartHost()`/`StartClient()` 呼び出しは `Start()`(全オブジェクトの Awake 完了が保証される)まで遅延させる(`StartNetworkingIfPending()`)。(2) `NetworkManager` と `NetworkObject` を同じ GameObject に置くと NGO が `[OnValidate] NetworkManager cannot be a NetworkObject` を警告し機能しない → `NgoNetBridge`(`NetworkObject` が必要)は別 GameObject(`NgoBridge`)に置く。いずれもユニットテスト(Fake/Delayed ブリッジ)では検出できず、実プレイヤー2プロセスでのローカル結合確認で初めて見つかった。詳細・ログ抜粋は [docs/29](29_network_device_test.md) §7。

## 実装メモ（2026-09-14、6-0 実機確認で見つかった課題の修正）

実機 2 台（PC-A Host + PC-B Client、[docs/29](29_network_device_test.md) §8）で見つかった課題 5 件を修正した。
実装: `Runtime/Net/NgoNetBridge.cs`（アプリ層送受信キュー遅延・Ping/Pong による App RTT 計測・切断通知）、
`Runtime/Net/NetPingMessages.cs`（新規、`NetPingMsg`/`NetPongMsg`）、`Runtime/Net/NgoTransportConfigurator.cs`
（コメント更新のみ）、`Runtime/Net/NetDebugOverlay.cs`（App RTT 併記）、`Runtime/Loop/DDriveRuntimeBootstrap.cs`
（`ConfigureAppLayerSimLatency` 配線 / `Presentation.SetRegistryReady`）、`Runtime/Presentation/PresentationManager.cs`
（Registry ready キュー・`OnNetworkReceivedPlay` イベント・未知キー破棄ログ）、`Samples/NetCheckRunner.cs`
（signal_fire/signal_recv・rtt_app_ms・connected/disconnected ログ、偽造 Cancel の狙い撃ち改善）。テストは
`Tests/Runtime/PresentationNetDeviceFixTests.cs`（新規）。

- **課題1(遅延シミュレーターが効かない)の原因**: `NgoTransportConfigurator` の呼び出し順序(`StartHost`/
  `StartClient` より前)は正しかった。真因は導入済みバージョンの UnityTransport(`Library/PackageCache/
  com.unity.netcode.gameobjects@.../Runtime/Transports/UTP/UnityTransport.cs`)側で
  `SetDebugSimulatorParameters`/`DebugSimulator` が `[Obsolete("... is no longer supported and has no
  effect. Use Network Simulator from the Multiplayer Tools package.")]` になっており、`DebugSimulator`
  フィールドはドライバ生成(`WithNetworkSimulatorParameters()` を引数無しで呼ぶ実装)時に一切参照されない
  ため、呼び出し自体が完全な no-op だったこと。**代替実装**: `NgoNetBridge` に送信キュー(`SendToAll`/
  `SendTo<T>`)と受信キュー(`Dispatch`)それぞれに `UniTask.Delay(ms)` を挟むアプリ層の遅延機構
  (`ConfigureAppLayerSimLatency(int)`)を追加した。`Debug.isDebugBuild`(Editor またはビルド設定で
  Development Build を付けた場合のみ true)かつ `-ddrive-sim-latency` 指定時のみ有効(既定は無効で挙動
  不変)。`DDriveRuntimeBootstrap.ResolveNetBridge()` が `NgoTransportConfigurator.TryConfigure` と並行して
  `bridge.ConfigureAppLayerSimLatency(...)` を呼ぶ(UnityTransport 側の呼び出しは将来 API が復活する場合に
  備えて残した。現状は無害な no-op)。**送信・受信の両方に遅延ロジックを持たせているが、実際に加算される
  箇所は経路によって変わる**: Ping/Pong の実測(§7 のローカル結合確認)では、Client の Ping 送信(`Broadcast`)
  は `RequestBroadcastRpc` を直接呼ぶだけで Client 側の送信キュー遅延を経由せず(`SendToAll`/`SendTo` は
  Host 側専用のため)、Host も遅延未設定だったので Host→Client の Pong 送信も遅延なし。**Client の受信
  (`Dispatch`)だけが 200ms 遅延した**ため、計測された `rtt_app_ms` は設定値とほぼ 1:1(実測 202〜211ms、
  `-ddrive-sim-latency 200` に対して)になった。Host 側にも `-ddrive-sim-latency` を指定した場合や、Client が
  Cosmetic を中継依頼する経路(`RequestBroadcastRpc` → Host の `SendToAll`)を使う場合は、送信・受信それぞれの
  遅延が積み重なるため設定値の倍数になり得る。「設定した ms 分だけ確実に増える方向に効く」ことの確認が目的
  であり、正確に 1:1 になることを保証する仕組みではない。
- **App RTT の計測**: `NgoNetBridge` が(Host ではない)Client のときだけ 1 秒おきに `NetPingMsg{
  SentAtNetworkTime }` を Broadcast し、Host が受信したら `SendTo` で `NetPongMsg{
  OriginalSentAtNetworkTime }` を送り返す。Client が Pong を受け取った時点で
  `(NetworkTime - OriginalSentAtNetworkTime) * 1000` を `AppRoundTripMs`(`double?`)として保持する。
  Ping/Pong 自体は既存の `Subscribe`/`Broadcast`/`SendTo` 経路(型登録・中継・レート制限)に相乗りするだけの
  最小実装で、専用の RPC は増やしていない。`NetDebugOverlay` は `Bridge is NgoNetBridge` パターン
  (`ReceivedMessageCount` と同じ既存の書き方)で `AppRoundTripMs` を読み、「RTT: … / App RTT: …」と並記する。
  `NetCheckRunner` の heartbeat にも `rtt_app_ms` を追加した。
- **課題2(Client 側で Signal 中継を観測できない)の対応**: `PresentationManager` に開発/確認ツール専用の
  `event Action<Handle<PresentationMarker>, uint> OnNetworkReceivedPlay`(ネット受信で新規生成された
  Instance の通知。既存の「確定エコー」分岐(二重生成防止)では発火しない)を追加した。`NetCheckRunner` は
  Host 自身の予測 Instance(`PlayAndSignal` 直後)とこのイベントの両方から `PresentationManager.OnTrackFired
  (handle)`(既存の R3 Observable)を購読し、実際に `TrackTrigger.OnSignal` が発火した瞬間に
  `signal_recv=hit key=<HandleNetKey> networkTime=...` を出す。Host が `handle.Signal("hit")` を呼んだ
  意図表明の瞬間は `signal_fire`(旧 `signal`)に改名し、`key`/`networkTime` を追加した。`HandleNetKey` は
  新設の `PresentationManager.DebugHandleNetKeyOf(handle)`(テスト/デバッグ専用、`DebugActiveHandles` と同じ
  位置付け)で取得する。
- **課題3(Late Join 直後の Placeholder)の原因**: 推測どおり、接続直後に届く Late-Join スナップショット
  (`PresentationPlayMsg`)が、Client 自身のカタログ登録(`DDriveRuntimeBootstrap.RegisterCatalogsAsync()`、
  `Start()` 内で `await` される非同期処理)完了より先に処理される順序の問題だった。**対応**:
  `PresentationManager.SetRegistryReady(bool)` を追加し、受信した `PresentationPlayMsg`/
  `PresentationSignalMsg`/`PresentationCancelMsg` は `_registryReady==false` の間、到着順序を保つ
  `Queue<PendingNetMessage>`(構造体、判別用の `Kind` + 3 種のメッセージを直接持つ。クロージャ/boxing を
  避けるため`Action` のキューにはしていない)へ保留し、`SetRegistryReady(true)` が呼ばれた時点でまとめて
  受信順に処理する。`DDriveRuntimeBootstrap.Build()` が `PresentationManager` 構築直後に `SetRegistryReady
  (false)` を呼び、`RegisterCatalogsAsync()` が `IsReady=true` にした直後に `SetRegistryReady(true)` を呼ぶ。
  ローカルの `Play()`/`PlayData()` API(ゲームコードが直接呼ぶ経路)はこのフラグの影響を受けない(ネット
  受信の 3 ハンドラだけがゲートされる)。既定値は `true`(Bootstrap を経由しない既存の全テスト/シングル
  プレイは今までどおり即時処理される)。回帰テストは
  `PresentationNetDeviceFixTests.OnReceivePlayMsg_BeforeRegistryReady_IsQueued_AndFlushedWithoutPlaceholder_AfterReady`。
  **要判断**: 接続直後の `activeCount` 5→0 がこの修正で直るかどうかは、ローカル(ループバック)結合確認では
  再現条件が異なる(カタログ登録がほぼ即時に終わるため実機ほどの遅延窓が無い)ため未確認。PC-B での再確認
  待ち。
- **課題4(未知キー Cancel の破棄ログ欠落)の対応**: `OnReceiveSignalMsg`/`OnReceiveCancelMsg` が発行者検証
  を通過した後、`_networkedHandles` に見つからない(未知のキー、または対象の演出が既に完了して
  `Cleanup()` で台帳から外れた)場合に、開発ビルドでは(`#if DEVELOPMENT_BUILD || UNITY_EDITOR`)
  `_unknownKeyDiscardWarned`(`HashSet<uint>`)でキーごとに 1 回だけ「未知のキー、または対象の演出が既に
  完了しているため破棄しました」と警告する。`NetCheckRunner.SendForgedCancel` は、Host が今アクティブな
  Presentation(`DebugActiveHandles()`)を持っていれば、その `HandleNetKey`(自分が発行していないキー)を
  最初に狙い、無ければ従来どおり完全ランダムな(未知の)キーにフォールバックする(発行者不一致・未知キーの
  両方の破棄経路を安定して踏ませる狙い)。
- **課題5(オーケストレーター追加指示。Host 停止時に Client が切断を検知しない)の対応**: `NgoNetBridge` が
  `NetworkManager.OnClientDisconnectCallback`/`OnTransportFailure` を購読し、`[Net/Host] Client <id> が
  切断しました(reason=...)`/`[Net/Client] Host から切断されました(reason=...)`(`NetworkManager.
  DisconnectReason` を含む)をログに出す。新設の `event Action<ulong, string> ClientDisconnected` を
  `NetCheckRunner` が購読し、heartbeat に `connected=0|1`(Client が Host との接続を保っているか。Host は
  常に 1)を追加、切断時に一度だけ `disconnected=1 role=... reason=...` を出す。自動再接続は MS2026 の
  規約に存在しないため実装しない(要判断: 将来必要になれば別チケットで追加)。切断後の Presentation 側台帳
  (`_networkedHandles`/`_activeNetworked`)は、進行中の Cosmetic 演出が通常の Elapsed/Duration 経由で
  Complete/Cancel されるのに任せる設計のままにした(相手の接続状態に関わらず一定時間で自然に台帳から
  外れるため、切断によって新たに残留エントリが生じるわけではないと判断した。専用のクリーンアップは
  追加していない)。**→ 2026-09-14 追記(6-0 修正7)**: この判断は誤りだった。実機確認 v3(docs/29 §8)で
  「切断後も VFX が消えずに描画し続ける」実バグが見つかり、Elapsed/Duration に任せるだけでは不十分
  (下記「実装メモ(6-0 修正7)」参照。専用のクリーンアップ `PresentationManager.CancelAllNetworked()` を
  追加した)。
- **見送り**: `-ddrive-sim-loss`(パケットロス)の代替実装は今回のスコープ外(課題として報告されていない
  ため。UnityTransport の `SetDebugSimulatorParameters` が no-op である以上、同じ問題を抱えているはずだが、
  実機確認で明示的に指摘されなかったため見送った。要判断: 必要になれば `NgoNetBridge` の送信キューで
  `UnityEngine.Random` によるドロップ判定を追加する形で同じ枠組みに乗せられる)。

## 実装メモ（2026-09-14、6-0 修正6: 実機確認 v2 で発見した「遅延時にワンショットが一切発火しない」実バグの修正）

[docs/29](29_network_device_test.md) §8「修正版 v2 での再確認」で見つかった、遅延 200ms 環境で剣攻撃デモの
VFX が Client に一切描画されない実バグの修正。加えて、その後の切断確認で見つかった 4 件の小さな課題も
同じ PR で対応した。

- **原因**: `PresentationManager.OnReceivePlayMsgInternal` が `elapsed = Max(0, NetworkTime - StartNetTime)`
  でシーク開始し、`SeekInitialTracks`(旧称。実装は変わらず本節でリネームはしていない)が `elapsed > 0` かつ
  `IsContinuousAtSeek` でないワンショット(Time=0 の VFX/SE 等)を無条件にスキップしていた。遅延 0ms では
  Client の ServerTime 推定が Host より約 80ms 遅れて見える([docs/29](29_network_device_test.md) §8 の
  気になる点①)ため差が負になり `Max(0, ...)` で 0 にクランプされて偶然発火していたが、実際に遅延がある
  と差が正の値になり、開始直後のワンショット演出がリモートでは常にスキップされていた。
- **修正**: `SeekInitialTracks` のワンショットスキップ判定に猶予(`_remoteOneShotGraceSec`)を追加した。
  `lateBySec = elapsed - track.Time` を計算し、`lateBySec <= 猶予` なら「単に遅れて届いただけ」として
  そのまま `FireTrack` を呼んで遅れて発火させる(VFX はシーク相当ではなく頭から再生になるが、要判断: 秒数が
  短いワンショットでは実用上問題ないと判断した。厳密なシーク再生が必要になったら見直すこと)。`lateBySec` が
  猶予を超えるものだけ従来どおり `Fired[t]=true` にしてスキップする(Late Join で大幅に古い演出を復元する
  ケースなど)。**猶予の既定値は 0.5 秒**とした(要判断の詳細は [docs/31](31_phase5_decisions.md) 参照)。
  シリアライズフィールドは増やさず、`PresentationManager` のコンストラクタの任意引数
  `remoteOneShotGraceSec = 0.5f` として渡す(`DDriveRuntimeBootstrap` は明示せず既定値のまま使う)。
  Late Join のスナップショット再送(5-9)は `OnReceivePlayMsg` と全く同じコード経路(`SeekInitialTracks`)を
  通るため、この猶予ロジックは追加の分岐なしで両方に一律適用される(回帰テスト
  `PresentationLateJoinTests.LateJoin_LoopingVfx_IsRestored_ButOldOneShotMarker_IsNotFired_...` で
  「ループ系は復元されるが、同じ Presentation 内の遠い過去のワンショットはスキップされる」の両立を確認)。
- **判定用ログ**: 確認ツール専用に `PresentationManager` へ Manager 全体で 1 つの event を 2 つ追加した:
  `OnAtTimeTrackFired(PresentationTrack, uint handleNetKey, float elapsed)`(`TrackTrigger.AtTime` のトラックが
  `FireTrack` へ実際に委譲された=発火した瞬間に発火。`elapsed` はその時点の `Instance.Elapsed`)と
  `OnRemoteOneShotSkipped(PresentationTrack, uint handleNetKey, float lateSec)`(猶予を超えてスキップした
  ときに発火)。**要判断ではなく実装上の必然**: 当初は Handle 単位の既存 `OnTrackFired(handle)`
  (R3 Observable)を `Play()` の戻り値を受け取った後に購読する設計にしていたが、`Play()` 自身が
  `Time<=elapsed` のトラックを同期的に発火させてしまうため、Host の予測再生や Client の受信生成
  (`OnNetworkReceivedPlay` 経由)いずれも「購読する前に最初の発火が終わっている」タイミング問題があり、
  実機確認前のローカル結合確認で `track_fired` が一件も出ないことに気付いて発見した。Manager 全体の
  event(`OnNetworkReceivedPlay`/`OnRemoteOneShotSkipped` と同じ設計)を `NetCheckRunner.Start()` で
  一度だけ購読する形に直し、Play() 内の同期発火にも間に合うようにした。`NetCheckRunner` は
  `OnAtTimeTrackFired` から `track_fired kind=<Kind> time=<Time> key=<HandleNetKey>
  late_ms=<(elapsed-Time)*1000> networkTime=...` を、`OnRemoteOneShotSkipped` から(開発ビルドのみ)
  `track_skipped kind=<Kind> time=<Time> key=<HandleNetKey> late_ms=...` を出す
  ([docs/29](29_network_device_test.md) §4 参照)。OnSignal トラック(`signal_recv`)は元々 `Play()` の
  戻り後にしか発火しないため、この問題の影響を受けず既存の Handle 単位購読のままでよい。
- **切断確認(オーケストレーター追加指示)で見つかった追加課題 4 件**:
  1. 切断後も `rtt_app_ms` が最後の値を表示し続ける → `NgoNetBridge.HandleClientDisconnected` が
     (自分=Client が切断された場合だけ)`AppRoundTripMs = null` にリセットする。`NetCheckRunner`/
     `NetDebugOverlay` は元から `HasValue` を見て `n/a` 表示するため、この一箇所のリセットだけで済んだ。
  2. 切断後も `NetCheckRunner` が 5 秒おきに偽造 Cancel を送ろうとして `NgoNetBridge.Broadcast` の
     「未接続のため送信できません」警告が出続ける → `NetCheckRunner.Update()` の偽造 Cancel 送信を、
     新設した `Connected(bootstrap)` ヘルパー(Host/Loopback は常に true、Client は
     `NgoNetBridge.IsConnected` を見る)でガードした。
  3. **切断直後に Client の画面へ VFX が薄く出る**: アプリ層遅延キュー(`NgoNetBridge` の
     `DelayedDispatch`/`DelayedSendTo`/`DelayedSendToAll`、`ConfigureAppLayerSimLatency` 参照)に積まれた
     `UniTask.Delay` が切断後も生き続け、`NetworkTime` が 0 に巻き戻った状態(`NetworkManager.ServerTime`
     が未接続時に返す値)で `Dispatch`/`SendTo` を実行してしまい、`elapsed = Max(0, 0 - StartNetTime) = 0`
     と再計算されて「今始まった正常な Play」と誤認されていた。→ `NgoNetBridge` に
     `CancellationTokenSource _appLayerQueueCts` を追加し、切断時(自分=Client が切断された場合。
     `OnNetworkDespawn` でも Cancel する)に `Cancel()` する。`Delayed*` の各メソッドはスケジュール時点で
     トークンを捕まえておき、`UniTask.Delay(..., cancellationToken: ct).SuppressCancellationThrow()` の後に
     `ct.IsCancellationRequested` を確認してから実行する(`PingLoopAsync` と同じ既存パターン)。**実際の
     NGO 接続が必要なため単体テストの対象外**(既存の慣習どおり)で、`docs/29` §7/§9 のローカル結合確認で
     検証する。PresentationManager 側から見た契約(「配送前に破棄されたメッセージは NetworkTime が
     巻き戻っても処理されない」)は回帰テスト
     `PresentationNetDeviceFixTests.RemotePlay_DiscardedBeforeDelivery_IsNeverProcessed_EvenIfNetworkTimeRewinds`
     で(`DelayedNetworkRelay.DiscardAllPending`/`RewindNetworkTimeToZero` を使い)検証する。
  4. デバッグ表示に接続状態が無い → `NgoNetBridge.IsConnected`(公開プロパティ、既定 true、Client が
     切断されたときだけ false)を追加し、`NetDebugOverlay` に `State: 接続中/切断` の行を追加した。
- **軽微(元の実機確認 v2 の気になる点②)**: 起動直後(`role=off`)の heartbeat が `connected=1` と紛らわしく
  出る問題も同じ修正で解消した。`NetCheckRunner.Connected(bootstrap)` は
  `IsServer || (IsClient && (Ngo なら IsConnected))` を返すため、`role=off`(`IsServer`/`IsClient` 双方
  false)では `connected=0` になる。
- **軽微(気になる点③)**: `signal_recv` に `kind=<Kind>` を追加した(同じ `key` で OnSignal トラック数ぶん
  行が出ることが分かるようにする)。

## 実装メモ（2026-09-14、6-0 修正7: 実機確認 v3 で発見した「切断後も VFX が消えずに描画し続ける」実バグの修正）

[docs/29](29_network_device_test.md) §8「修正版 v3 での再確認」の「切断」で見つかった実バグの修正。

- **原因(2 つが重なって発生)**:
  1. **切断時に何もクリーンアップしていなかった**: `ClientDisconnected` イベント自体は 6-0 修正5 で
     追加済みだったが、購読して実際にネット経由の Presentation を止めるコードが無かった(上の課題5の節に
     あった「Elapsed/Duration に任せる」という判断がそもそも誤りだった)。切断した時点で `TotalDuration`
     の途中(=まだ `_active` に残っている)ネット経由 Presentation は、切断後もタイマーが進み続けて
     普通に `Complete()` するだけで、`FiredVfx`/`FiredSe` 等は一切止められない
     (`Complete()` は `Cancel()` 経由の `StopFiredForCancel` を通らない。この「通常完了時に Fired 済みの
     ものを止めない」設計自体は本チケットのスコープ外の広い論点として [docs/31](31_phase5_decisions.md) に
     残した)。
  2. **デモアセット側の見落とし**: `PRES_Demo_SkillSlash.asset` の Vfx トラック(`VFX_Player_Slash` を
     再生する Time=0 のトラック)が `StopOnCancel=false` のままだった。`StopOnCancel=true` の他トラック
     (CameraShake/Haptic)と非対称で、そもそも `Cancel()` 経路が通っても止まらない状態だった。加えて
     `VFX_Player_Slash` が参照する Prefab(`Assets/SourceAssets/Data/vfx_sample.prefab`)の
     `ParticleSystem` は `looping=true`(`VfxData.LifeMode=OneShot` の意図=「自然終了で自動返却」と矛盾する
     設定)で、`VfxManager.IsLifetimeExpired` の OneShot 判定(`ps.IsAlive(true)` が false になるまで待つ)が
     エミッションが続く限り真になり続けるため自然終了しない。**この 2 点目は接続の有無に関係なく発生する
     (切断しなくても、Cancel されない限り永遠に再生され続ける)**。実機確認 v3 で「接続中の 5 枚は白画素
     76〜82 で安定」していたのは、Host が 3 秒おきに Play する `PRES_Demo_SkillSlash` それぞれの VFX が
     ループしたまま溜まり続けている状態(単純に短時間の観測窓では大きな変化として見えなかった)と考えられる
     (要判断として残した点は [docs/31](31_phase5_decisions.md) 参照)。
- **修正**:
  1. `PresentationManager.CancelAllNetworked()` を追加。`_active` のうち `IsNetworked=true` かつ未完了の
     Instance だけを `CancelInternal()` で強制終了する(既存の `StopAll()` と同じく `Interruptible` を見ない。
     `StopFiredForCancel` が既存の規則どおり `StopOnCancel=true` の Fired Vfx/Se/Anim 等を止める)。
     ネット非経由(`IsNetworked=false`)の Instance には触れない。
  2. `DDriveRuntimeBootstrap` が `NgoBridgeRef.ClientDisconnected` を購読し(`Ngo` モードのときだけ。
     `Loopback` は既存どおり無関係)、**Client 視点で自分が切断されたとき**(`!NgoBridgeRef.IsServer`)だけ
     `Presentation.CancelAllNetworked()` を呼ぶ。Host 視点(相手が抜けた)は自分の接続は継続しているため
     対象外(`NgoNetBridge.IsConnected` が Client 側でだけ false になる既存の仕様と対になる判定)。
  3. `PRES_Demo_SkillSlash.asset` の Vfx トラックを `StopOnCancel=true` に変更(Unity Editor 経由、
     `Undo.RecordObject`+`EditorUtility.SetDirty`+`AssetDatabase.SaveAssets`)。これが無いと上の 1./2. の
     コード修正だけでは(このデモに限っては)`StopFiredForCancel` が対象を見つけられず無意味になるため、
     コード修正と対にして直した(`vfx_sample.prefab` の `looping=true` 自体は直していない。要判断は
     [docs/31](31_phase5_decisions.md) 参照)。
  4. **判定用ログ**: `NetCheckRunner` の heartbeat に `vfx_active=<VfxManager.ActiveCount>`(生存中の VFX
     インスタンス数。既存の公開プロパティで、新規 API 追加は不要だった)を追加した。`activeCount`
     (Presentation)だけが変化したときだけでなく `vfx_active` だけが変化したときも heartbeat を出すよう
     `_lastVfxActive` の比較を足した。
- **スコープの限界(要判断)**: `CancelAllNetworked()` は「切断した時点でまだ `_active` に残っている
  (=`TotalDuration` 未満の)ネット経由 Presentation」だけを対象にする。すでに `Complete()` して台帳から
  外れた Presentation から Spawn した Vfx/Se(`StopOnCancel` の有無に関係なく、そもそも `Complete()` が
  `StopFiredForCancel` を呼ばないため)は対象外で、切断以前から残っていた分は切断後も残り続ける
  (このケース自体は「通常完了時の設計」の問題であり切断固有ではないため、本チケットでは直していない)。
  ローカル結合確認(docs/29 §11)は、この限界を踏まえて「切断直前に再生を始めた分がまだ `_active` に
  残っている短い接続窓」で確認した。回帰テストは
  `PresentationNetDeviceFixTests.CancelAllNetworked_StopsLoopingFiredVfx_EvenWhenNotInterruptible`/
  `CancelAllNetworked_DoesNotAffect_NonNetworkedPresentation`。

## 6. 時刻・乱数・決定性

- 演出のスケジュール（AtTime トラック、Frame イベント）は `NetworkTime` 基準に統一。`Time.time` を Foundation で直接使わない（`ITimeSource` 注入。ローカル時は Time.time 実装）
- SeData の Random 選択・PitchRange は `Seed` から決定的に引く → 全クライアントで同じ音が鳴る（こだわらない場合は Local 乱数でも可、データフラグで選択）
- HitStop / TimeScale はローカル演出として各自実行（サーバーのシミュレーション時間には影響させない）

## 7. カタログ整合性（コンテンツバージョン照合）

クライアントとサーバーでアセット定義がズレていると「相手には見えない VFX」等の不具合になる。

- ビルド時にカタログごとの **ContentHash**（全 Entry の ID + Data ハッシュ）を生成
- 接続ハンドシェイクで照合: 不一致 → 切断 or 互換モード（ID 存在チェックのみ）をプロジェクト方針で選択
- Addressables Remote 更新時はカタログバージョンを合わせて配信。`MinCompatibleVersion` で下位互換範囲を宣言

### 実装メモ（2026-09-15、6-5: カタログ ContentHash 生成 + 接続時照合）

実装: `Foundation/Registry/CatalogContentHasher.cs`（新規、64bit FNV-1a 風の決定的ハッシュ）、
`Runtime/Net/CatalogContentHashMessages.cs`（新規、`CatalogContentHashMsg`/`CatalogContentHashResultMsg`）、
`Runtime/Net/CatalogContentHashPolicy.cs`（新規、不一致/タイムアウト時の方針決定 + 差分説明の純関数）、
`Runtime/Net/CatalogContentHashGate.cs`（新規、接続時照合のオーケストレーション）、
`Foundation/Net/INetBridge.cs`（`DisconnectClient(ulong,string)` 追加。`LocalLoopbackBridge`/`NgoNetBridge`/
テスト用 `FakeNetBridge`/`CountingNetBridge`/`DelayedNetBridge`(Tests/Runtime/PresentationNetTests.cs) に実装追加）、
`Runtime/Loop/DDriveRuntimeBootstrap.cs`（`NetHashGate` 生成・`RegisterCatalogsAsync` 完了時の
`SetLocalSummary` 呼び出し・`Update()` での `Tick`）、`Runtime/Net/NetDebugOverlay.cs`（ContentHash 状態の表示）、
`Editor/Validation/ContentHashCatalogCoverageValidator.cs`（新規、CI Error）。テストは
`Tests/Runtime/{CatalogContentHasherTests,CatalogContentHashPolicyTests,CatalogContentHashGateTests}.cs`
（新規）+ `Tests/Editor/ContentHashCatalogCoverageValidatorTests.cs`（新規）。

- **何をハッシュに含めるか**: 本節冒頭は「全 Entry の ID + Data ハッシュ」とだけ定めており、Data 本体の
  どのフィールドまで含めるかは規定していなかった。Data 本体(ScriptableObject の全フィールド)を
  リフレクションで走査する案は、①Unity Object 参照・浮動小数点・配列順序等が絡み決定性の保証コストが
  高い ②「Host/Client の資産定義が食い違っていないか」の検出という目的には、通常カタログの
  Entry(登録されている ID・種別・Address・NetMode)自体が変わることで表面化する、という 2 点から見送った。
  チケット指示にある既定方針(無ければ「ID・種別・アドレス・NetMode 等ネット同期に効くフィールドに絞る」)を
  採用し、`CatalogEntry`(Id/Type/Address/Flags.Net)だけを対象にした。Data 本体の内容(調整値等)まで
  含めたい場合は、将来 `AssetDataBase.Version`(6-3 の保存カウンタ)を Entry 側に持たせてから混ぜる拡張が
  考えられる(要判断)。
- **決定性・順序非依存**: 64bit FNV-1a 風にミックスした Entry 単体のハッシュを、カタログ内・カタログ間の
  両方で **XOR 合成**する。XOR は可換・結合的なため、Entry の列挙順・カタログをどう分けて登録したかに
  一切依存せず、複数カタログの結果を XOR したものは全 Entry を 1 つに flatten して計算した結果と必ず一致する
  (`CatalogContentHasherTests.CombineCatalogHashes_OrderIndependent_AndMatchesFlattenedSingleCatalog` で検証)。
  暗号学的な強度は無い(コンテンツのズレ検出が目的であり、悪意ある偽装への耐性は要求していない。下記
  「セキュリティ上の限界」参照)。
- **生成タイミング(ビルド前処理を追加しなかった判断)**: このハッシュは `CatalogEntry` の構造的フィールドの
  みに基づき Data 本体のロードを要さないため、`DDriveRuntimeBootstrap.RegisterCatalogsAsync()` 完了時点
  (Editor Play Mode・実ビルドいずれも同じコードパス)で毎回同一の結果を計算できる。Host/Client が同一
  ビルドを実行する限り、ビルド前処理(`IPreprocessBuildWithReport`)で別ファイル(ScriptableObject /
  StreamingAssets)へ事前計算・embed する追加のパイプラインは不要と判断し、実装しなかった。チケットが
  提示した「既存の Preload 集計と同じ `IPreprocessBuildWithReport` 系」からの意図的な逸脱であり、理由は
  上記のとおり(要判断: 将来 Data 本体まで含める設計に広げ、かつ計算コストが問題になった場合はビルド前処理
  での事前計算を検討すること)。
- **接続時照合の流れ(Host 権威、非対称)**: Client は接続確立(`INetBridge.ClientConnected` — NGO の
  `OnClientConnectedCallback` は自分自身の接続でも発火する。[14] §5(5-9) と同じ)かつ自分のカタログ登録
  完了の両方が揃った時点で、`CatalogContentHashMsg{ CombinedHash, Catalogs[](カタログ名+Hash+Entry数) }` を
  1 回だけ `Broadcast`(Client 発は既存の Client→Host 依頼経路に乗る)する。**Host は自分のハッシュを
  送り返さない**(比較・不一致判定は必ず Host 側で行う。Host 権威の他機構(PrefabSpawnRequestMsg 等)と
  同じ非対称設計)。Host は比較結果(`CatalogContentHashResultMsg{ Matched, Descriptions[] }`)を該当
  Client へ `SendTo` する(一致時も送る。Client 側の状態表示が「検証中」のまま止まらないようにするため)。
- **タイムアウト(偽装対策)**: Host は `ClientConnected` ごとに `NetworkTime + タイムアウト秒`(既定 5 秒、
  `DDriveRuntimeBootstrap.ContentHashTimeoutSeconds`)の期限を記録し、その期限内に
  `CatalogContentHashMsg` を受信できなければ「不一致」と同じ方針分岐にかける(ハッシュを送らない/遅延させる
  クライアントへの対処)。`DDriveRuntimeBootstrap.Update()` から毎フレーム `NetHashGate.Tick(NetworkTime)`
  を呼ぶ(Dictionary が空なら即 return するだけで実質無害)。
- **開発ビルド/エディタは継続、リリースビルドは切断(2026-09-15 ユーザー決定)**: 判定は
  `CatalogContentHashPolicy.Decide(bool isDevelopmentOrEditor)`(純関数、テスト容易)に集約し、
  呼び出し側(`DDriveRuntimeBootstrap`)が `Debug.isDebugBuild`(Editor 実行時、または Development Build を
  付けたプレイヤーで true。`NgoNetBridge.ConfigureAppLayerSimLatency` と同じ判定基準)を渡す。不一致・
  タイムアウトのどちらでも同じ方針を適用する(「一定時間内に届かなければ不一致扱い」の要求どおり)。
  継続時は Host が `Debug.LogWarning` + `NetDebugOverlay`(`CatalogContentHashGate.LastStatusText`)に
  「どのカタログが違うか」を **カタログ名 + Entry 数だけ**(実データは送らない)で表示し、`SendTo` で
  該当 Client にも同じ情報を返す(Client 側も `Debug.LogWarning` + 同じ `LastStatusText` を更新。
  「双方に警告ログ」の要求)。切断時は `NetworkManager.DisconnectClient(clientId, reason)` を呼ぶ
  (Client の `NetworkManager.DisconnectReason` に reason がそのまま届く。既存の `HandleClientDisconnected`
  と同じ仕組み)。切断が決まった場合、`CatalogContentHashResultMsg` は送らない(切断済み Client への送信は
  無意味なため)。
- **セキュリティ上の限界(要判断)**: Host は Client から届いた `CombinedHash` を信じて比較するだけであり、
  改造 Client が「期待される値」を知っていれば偽装できる(暗号学的な検証機構は無い)。これは
  `HandleNetKey` の発行者検証(§9、実際のゲーム進行の整合性を守る)とは目的が異なり、本チケットの
  ContentHash は「デザイナー/プログラマーが GameData の更新を反映し忘れた」等の**正直な版ズレの検出**が
  目的であり、悪意あるクライアントへの耐性は要求されていないと判断した(MS2026 の実際の対人戦
  1v1・LAN 内という前提とも整合する)。
- **見送り**: `NetChannel.Unreliable` は使わない(ハンドシェイクは接続時 1 回だけで頻度が低く、欠落してよい
  類のイベントでもないため、Presentation の Play/Signal/Cancel と同じ判断で `ReliableOrdered` にした)。
  複数 Client(1v1 を超える構成)は `Dictionary<ulong,double>` で自然に扱える設計にしてあるが、実機確認は
  MS2026 の 1v1 前提のまま(6-0/6-6 の既存確認環境を再利用)。

### 実装メモ（2026-09-18、docs/41 テストの穴 1: 偽造 `CatalogContentHashResultMsg` の修正）

上記「セキュリティ上の限界」は **Client→Host** に届く `CombinedHash`(自己申告値)が偽装可能であることを
指しており、これは意図的に許容している。今回見つかったのは別方向の穴: **Host→Client** の判定結果
(`CatalogContentHashResultMsg`)自体を、Host 以外が偽装して上書きできる問題(`CatalogContentHashGate.
OnReceiveResultMsg` が `senderId` を一切見ていなかった)。

`NgoNetBridge.Broadcast` は Client 発でも `RequestBroadcastRpc`(`[Rpc(SendTo.Server, InvokePermission =
RpcInvokePermission.Everyone)]`)経由で「型登録済み(`_keyToType` に載っている)なら」Host が中継してしまう。
`CatalogContentHashResultMsg` も `CatalogContentHashGate` のコンストラクタで `Subscribe` される(Host/Client
双方が同じ Gate を持つため型登録される)ため対象になり、改造 Client が `Matched=true` を騙って
`Broadcast` すると、Host が中継した先(自分自身を含む全 Client)で本物の Host 判定(不一致警告)を
「OK」に上書きできてしまっていた。「双方に警告ログ」という設計意図(§7 実装メモ 2026-09-15)そのものを
無効化できる欠陥だったため、意図的に許容した「ハッシュ自己申告の偽装」とは切り分けて修正した。

修正: `CatalogContentHashGate` に `HostClientId = 0UL`(`PresentationManager.TrustedRelayClientId` と同じ、
NGO の `ServerClientId` は常に 0)を追加し、`OnReceiveResultMsg` の先頭で `senderId != HostClientId` を
弾くようにした(`PresentationManager.IsAuthorizedSender` と同じ考え方の発行者検証)。回帰テストは
`Tests/Runtime/CatalogContentHashGateTests.cs` の `ClientSide_ForgedResultFromNonHostSender_IsIgnored`
(修正前は red: 偽の `Matched=true` で `LastStatusText` が "OK" に上書きされていた)。既存の
`ClientSide_ReceivesMismatchResult_UpdatesStatusAndLogs` / `ClientSide_ReceivesMatchedResult_SetsStatusOk`
も、`bridge.SendTo(42, ...)`(実質「自分自身から」という非現実的な模擬になっていた)から
`bridge.InjectReceive(0UL, ...)`(Host から、を明示)に修正した。

同じレビュー([docs/41](41_phase6_review_2026-09-17.md) テストの穴 2〜5)で追加した他 4 件のテスト
(保留のフラッシュ・タイムアウト時のイベント発火・保留バッファ経由の偽造/保留 Cancel の Play 後適用・
Cancel のレート制限)はすべて green で、実装側の修正は不要だった。

### 実装メモ（2026-09-19、[docs/44](44_review_2026-09-19.md) P2-1: `NgoNetBridge.OnPongMsgReceived` の送信元検証）

`CatalogContentHashResultMsg` に対して上の実装メモで塞いだのとまったく同じ形の穴が `NetPongMsg` にも
残っていた。`NgoNetBridge.OnPongMsgReceived` は `senderId` を一切見ずに
`_appRoundTripTracker.OnPongReceived(measuredMs)` を呼んでいたため、改造 Client が
`Broadcast(new NetPongMsg{...})` すると(`NetPongMsg` も `OnNetworkSpawn` で `Subscribe` され型登録される
ため中継されてしまう)、受け取った側の `AppRoundTripMs`/`IsAppRoundTripMsStale` が任意の値に化けた。
本来 `null` のはずの Host 側にも `AppRoundTripMs` に値が入ってしまう副作用もあった(`NgoNetBridge.cs` の
「Host 自身は計測しない=常に null」というコメントと矛盾する状態)。

修正: `OnPongMsgReceived` の先頭で `IsServer` なら常に破棄する(Host は `PingLoopAsync` を自分では
起動しない=`!IsServer` 限定なので、正当な Pong の宛先には絶対にならない)。Client 側は
`senderId != NetworkManager.ServerClientId`(常に 0、Host)を弾く。いずれも開発ビルドのみ 1 回だけ
`Debug.LogWarning` する(`PresentationManager.WarnUnknownKeyDiscardedOnce` と同じ考え方、
`NgoNetBridge._warnedForgedPong`)。

`CatalogContentHashGate` の修正と異なり、送信元との一致判定・状態更新そのものは
`AppRoundTripTracker.OnPongReceived(double measuredMs, ulong senderId, ulong trustedSenderId)`
(新規オーバーロード、既存の `OnPongReceived(double)` はそのまま残す)に委ねた。`NgoNetBridge` は
`NetworkBehaviour` 派生で EditMode から直接テストできない([docs/29](29_network_device_test.md)
§7/§9/§11 の既存の慣習)ため、「送信元が信頼できる相手と一致しない Pong は状態を変えずに無視する」
という不変条件を Unity API 非依存の `AppRoundTripTracker` 側に持たせることで、
`Tests/Editor/AppRoundTripTrackerTests.cs` の
`OnPongReceived_WithSenderValidation_IgnoresPongFromUntrustedSender` で EditMode のまま固定できるように
した。`NgoNetBridge` 側の `IsServer`/`senderId` 分岐そのもの(NGO 接続が要る部分)は
[docs/29](29_network_device_test.md) の次回実機確認項目に追加した(「偽 Pong の破棄」)。

### 実装メモ(2026-09-20、[docs/42_distribution.md](42_distribution.md) §5.6/P-8: `ProtocolVersion` による版照合)

D-Drive の版が違う Host/Client が繋がると、従来は「ContentHash 不一致」としてしか見えず原因(GameData の
ズレなのか D-Drive 自体の版差なのか)が分からなかった。`CatalogContentHashMsg` に `PackageVersion`
(string、表示専用)・`ProtocolVersion`(int、`Foundation/Net/DDriveProtocol.cs` の `Current = 1`)を
フィールド追加し、`CatalogContentHashGate.ProcessHostSide` が **ContentHash の比較より先に**
`ProtocolVersion` を照合するようにした。

- 不一致(`msg.ProtocolVersion != DDriveProtocol.Current`。旧版 Client〔このフィールドが無い版〕は
  `JsonUtility` の既定値のまま `0` で届くため同じ扱いになる)は、本節の**開発ビルド/エディタは警告のみで
  継続、リリースビルドは切断**という既存方針(`CatalogContentHashPolicy.Decide`)をそのまま適用する。
  理由文には「D-Drive の版が違います(自: x(Protocol n) / 相手: y(Protocol m))」のように双方の
  `PackageVersion`/`ProtocolVersion` を含める(Host のログ・`NetDebugOverlay`・Client への
  `CatalogContentHashResultMsg` いずれにも同じ文言が乗る)
- 一致すれば従来どおり `CombinedHash` の比較に進む(ContentHash 不一致の分岐と ProtocolVersion 不一致の
  分岐は排他。両方一致して初めて `LastStatusText = "OK"` になる)
- `PackageVersion` 自体は**照合しない**(表示専用)。MINOR/PATCH の版差は互換なので、揃える必要があるのは
  ワイヤ形式そのものを表す `ProtocolVersion` だけ。`ProtocolVersion` を上げるのは
  [docs/42_distribution.md](42_distribution.md) §5.6 のネットメッセージ互換を破る MAJOR 変更のときだけ
- `NetDebugOverlay`(`#if DDRIVE_NGO`)に自分の版(`CatalogContentHashGate.LocalPackageVersion`)と、
  分かる範囲での相手の版(`LastKnownRemotePackageVersion`。Host は受信した `CatalogContentHashMsg` から
  都度更新できるが、Client 側は現状 Host の版を受け取る経路が無い〔`CatalogContentHashResultMsg` には
  フィールドを追加していない〕ため空のまま)を表示する行を追加した
- テスト: `Tests/Runtime/CatalogContentHashGateTests.cs` に `HostSide_ProtocolVersionMismatch_InDevelopment_WarnsAndContinues_WithVersionsInReason`・
  `HostSide_ProtocolVersionMismatch_InRelease_Disconnects_EvenWhenHashMatches`(`CombinedHash` が一致
  していても `ProtocolVersion` 不一致だけで切断される = 先に判定されることの証明)・
  `HostSide_ProtocolVersionMatches_ProceedsToContentHashComparison` を追加

## 8. 帯域・最適化

- ID は ulong(8B) だが、接続時に「セッション ID テーブル」（登場しうる ID → u16 インデックス）を交換し **2B に圧縮**（オプション。v1 は ulong 直送で可）
- Cosmetic は Unreliable + 集約（同フレームの複数演出を 1 パケットにバッチ）
- 距離カリング: `NetRelevanceRadius` を AssetFlags に追加可能（遠くのプレイヤーの足音 SE は送らない）— NetBridge の関心管理(Interest Management)に委譲

## 9. セキュリティ / チート耐性

- クライアント発の `PlayMsg` をサーバーは無条件中継しない: Simulated は必ずサーバー生成。Cosmetic の中継もレート制限 + 発信者の状態検証（死亡中に攻撃演出を送っていないか等はゲームロジック側フック）
- ID 存在検証: 受信 ID が Registry に無い → 破棄 + ログ（Placeholder は**ローカル開発時のみ**。ネット受信では出さない）→ **2026-09-15 実装(6-6)**: `PresentationManager.OnReceivePlayMsgInternal` が `IAssetRegistry.IsRegistered(id, type)`（新規、副作用なしの存在確認。`ResolveOrPlaceholder`/`OnPlaceholderUsed` を経由しない）で未登録・種別不一致（範囲外）を先に検出し、破棄 + ログしてから初めて `ResolveOrPlaceholder` を呼ぶ。以前は無条件に `ResolveOrPlaceholder` を呼んでいたため、ネット受信でも Placeholder(尺 0 秒)の Instance が実際に生成されてしまっていた。`VfxManager`/`AudioManager` の `OnReceiveCosmeticBatch`（VfxNetMsg/SeNetMsg）は同じ特性（`ResolveOrPlaceholder` を無条件に呼ぶ）を持つが、6-6 のスコープ外として見送った(要判断: 同じ理由で直す価値がある。次のネット関連チケットで対応候補)。

## 10. Validation（ネットワーク関連）

| 検査 | 重度 | 状態 |
|---|---|---|
| NetMode=Simulated の Prefab に NetworkObject 相当が無い | Error | ✅ 4-13(`PrefabDataValidator`) |
| Presentation 内に Simulated トラックと PredictLocal の競合 | Error | ✅ 2026-09-15(6-6、`PresentationDataValidator`) |
| Cosmetic なのに Reliable 大容量パラメータ（Texture 等）をイベント送信 | Warning | ✅ 2026-09-15(6-6、`PresentationDataValidator`) |
| NetMode 未設定（既定値のまま大量放置） | Info（レポート） | ✅ 2026-09-15(6-6、`NetModeUnsetValidator`) |
| ContentHash 生成対象外のカタログ | Error（CI） | ✅ 2026-09-15(6-5、`ContentHashCatalogCoverageValidator`)。「種別」側は生成器が `CatalogEntry` の構造的フィールドのみを対象にし種別ごとの登録リストを持たないため、構造的に発生しない(§7 実装メモ参照) |

### 実装メモ（2026-09-15、6-6: 受信検証・レート制限 + ネット Validator）

実装: `Foundation/Registry/{IAssetRegistry,AssetRegistry}.cs`（`IsRegistered(id, type)` 追加）、
`Runtime/Presentation/PresentationManager.cs`（ID 存在検証・Signal/Cancel レート制限・K3 未知キー保留）、
`Runtime/Presentation/PresentationDataValidator.cs`（Simulated+PredictLocal 競合 Error・大容量 Params
Warning）、`Runtime/Net/NetModeUnsetValidator.cs`（新規、NetMode 未設定 Info）、`Runtime/Net/NgoNetBridge.cs`
（K3: アプリ層遅延キューの FIFO 化、K2: App RTT の経過時間フォールバック）、`Runtime/Net/NetDebugOverlay.cs`
/`Samples/NetCheckRunner.cs`（K2 の `(stale)`/`rtt_app_stale` 表示）。テストは
`Tests/Runtime/{PresentationNetValidationTests.cs（新規）,PresentationNetDeviceFixTests.cs（追記）}`。

- **PredictLocal と Simulated トラックの競合(Error)**: `PresentationDataValidator` に
  `TryFindTrackAssetNetMode(ctx, track.Asset, out netMode)`（`AnchorDataValidator.FindAnchor` と同じ
  `ctx.AllAssets` の線形走査、LINQ 不使用）を追加した。`presentation.PredictLocal==true` かつトラックの
  参照先アセット(Vfx/Se/Prefab 等)の `Flags.Net==NetMode.Simulated` の場合に Error にする(行為者クライアントの
  予測再生がサーバー権威の生成と矛盾するため)。
- **Cosmetic の大容量 Params(Warning)**: しきい値は docs に定めが無かったため定数で置いた
  (`PresentationDataValidator.LargeParamKeyCountWarnThreshold = 8`)。`ParamValueType.Object`(Texture/Mesh
  等の `UnityEngine.Object` 参照)は件数を問わず常に対象、`Curve`/`Gradient` はキー数がこの値を超えたときだけ
  対象にする。現状 Params はどの Manager もネットワーク越しに同期しない(§4 実装メモ「paramOverrides の
  同期も未実装」)ため、これは「将来 Params 同期を実装したら帯域を圧迫する/今は各クライアントのローカル
  デフォルト値で再生されて見た目が食い違う」という予防的な警告になる。
- **NetMode 未設定の大量放置(Info)**: `NetModeUnsetValidator`(新規、`IUniversalValidator`)を追加した。
  意味を持つ種別(§4 の表: Se/Bgm/Vfx/Prefab/Material/Presentation)に限定し、`Flags.Net==NetMode.Local`
  (既定値)のアセットごとに Info を 1 件出す。**要判断**: `ValidatorRegistry`(Foundation/Validation)の
  `RunAll` は 1 アセットにつき 1 回 `Validate` を呼ぶ設計で、全アセット走査後にまとめて 1 件の「集計」を
  出すフックが無い。専用の集計 API を追加するのは `ValidatorRegistry` 自体の設計変更を伴うため 6-6 の
  スコープ外と判断し、Validation ウィンドウの一覧に並ぶ件数自体を集計として使う方針にした。
- **Signal/Cancel のクライアント別レート制限**: `NgoNetBridge.RequestBroadcastRpc` の中継レート制限
  (60/秒/クライアント、[14] §9)は Broadcast() 経由の全メッセージ種別を合算したものであり、Presentation の
  Signal/Cancel だけを狙った高頻度送信を個別に制限できない。`PresentationManager` に
  `ConsumeSignalCancelBudget(senderId)`(同じ既定値 60/秒/クライアント、`NgoNetBridge.ConsumeRelayBudget`
  と同じロジックをトランスポート実装に依存しない形で複製)を追加し、`OnReceiveSignalMsgInternal`/
  `OnReceiveCancelMsgInternal` の入口(発行者検証の直後)で消費する。Host(`TrustedRelayClientId`)は対象外。
- **K3(Signal/Cancel が Play より先に届く)の真因**: `NgoNetBridge` のアプリ層遅延キュー
  (`-ddrive-sim-latency` 用、6-0 修正1)が、メッセージ 1 件ごとに独立した `UniTask.Delay(...).Forget()` を
  fire-and-forget していたため、ほぼ同時に複数メッセージが積まれた場合に「実際に送信/配送される順序」が
  実装上保証されていなかった(同一長さの Delay が同一フレームで満了する場合、UniTask の内部スケジューラが
  どの順で継続処理するかは未規定)。実機確認 v4(200ms 遅延・ホットスポットが数秒止まってまとめて届いた
  区間)で `PresentationSignalMsg` が対応する `PresentationPlayMsg` より先に処理され「未知のキー」として
  破棄される実バグとして観測された([docs/29] §12)。**修正**: 3 つの遅延経路(`SendToAll`/`SendTo`/
  `Dispatch`)それぞれを `Queue<AppLayerQueueEntry>` による本物の FIFO に置き換えた。`Update()`(毎フレーム)
  が各キューの先頭から「解放予定時刻(`Time.time` 基準)を過ぎたものだけ」取り出す。3 キューとも同じ
  `_appLayerSimLatencyMs` を使うため、先頭が未到達ならそれより後ろも必ず未到達であり、早期 break しながら
  厳密な送信/受信順を保てる。切断時のキュー破棄も `CancellationTokenSource.Cancel()` から `Queue<T>.Clear()`
  に変えた(実装が簡潔になった副産物。挙動は変わらない)。
- **K3 の受信側防波堤(保留)**: 上記の真因修正に加え、Late Join・再送・将来の他 `INetBridge` 実装でも
  同種の順序崩れが起こりうるため、`PresentationManager` 側にも防波堤を置いた。`OnReceiveSignalMsgInternal`/
  `OnReceiveCancelMsgInternal` は対象の `HandleNetKey` が `_networkedHandles` に見つからない場合、即座に
  破棄せず固定長リングバッファ(`PendingUnknownKeyCapacity=16`、事前確保した `struct` 配列。Tick/Play の
  定常経路での alloc を避ける)へ `_pendingUnknownKeyHoldSec`(既定 `remoteOneShotGraceSec` の 2 倍 = 1.0 秒、
  要求どおり)秒だけ保留する。対応する `PresentationPlayMsg` が到着し `PlayLocalInternal` が
  `_networkedHandles[handleNetKey]` を登録した直後に `FlushPendingUnknownKey` を呼び、保留中の同じキーの
  エントリを到着順(`InsertSeq` 昇順)に適用する(発行者検証は保留解除時にも再実施する。要求どおり)。
  期限切れ(対応する Play が来なかった場合)は `Tick()` が掃除し、従来どおりの破棄ログ(`WarnUnknownKeyDiscardedOnce`)
  を出す。バッファが満杯(16件)のときは最も古い(期限が最も近い)エントリを退避させて上書きし、開発ビルドで
  1 回警告する(無制限に貯め込まない上限。フラッド対策の主防波堤は `ConsumeSignalCancelBudget` の方)。
- **既存テストの調整**: `PresentationNetDeviceFixTests.UnknownHandleNetKey_{Cancel,Signal}_LogsDiscardWarning`
  は「即座に破棄ログが出る」前提だったが、K3 の保留により破棄ログは期限切れ時(`Tick()`)まで遅延するため、
  `bridge.NetworkTime` を保留期限の先まで進めてから `Tick()` を呼ぶように修正した(挙動の変更を反映した
  意図的なテスト更新。回帰ではない)。
- **K2(通信停止中、`rtt_app_ms` が固着する)**: `NgoNetBridge.AppRoundTripMs` は Pong を受信した時点の値を
  保持するだけだったため、Pong が途絶えている間は最後の実測値を表示・ログし続けていた(実機確認 v4、
  200ms ラウンドでホットスポットが数秒止まった区間で観測)。`AppRoundTripMs` を計算プロパティに変更し、
  直近の Ping 送信(`_lastPingSentRealtime`、`Time.unscaledTimeAsDouble`)から Pong 未応答のまま
  (`_awaitingPong`)の間は「最後の Ping 送信からの経過時間」を、それが最後の実測値を上回っている場合に
  限り返す(下回っている間はまだ正常な RTT 範囲内なので実測値をそのまま返す)。Pong を受け取ると
  `_awaitingPong=false` に戻り実測値表示に戻る。新設の `IsAppRoundTripMsStale`(true の間は上記の下限推定
  であることを示す)を `NetDebugOverlay`(`App RTT: … ms (stale)`)と `NetCheckRunner`(heartbeat に
  `rtt_app_stale=0|1` を追加)の両方に反映した。切断時(`HandleClientDisconnected` の Client 分岐)は
  `_awaitingPong`/`_lastPingSentRealtime` も明示的にリセットする(既存の `AppRoundTripMs=null` だけでは
  新しい計算プロパティのゲッターが古い `_lastPingSentRealtime` を使って経過時間を返し続けてしまうため)。
- **未検証(Unity 未接続)**: 本チケットはワークツリー内で実装し、Unity Editor が開いているメイン
  リポジトリでのコンパイル・テスト確認はできていない(ワークツリーでは Unity MCP を使わない運用、
  [ddrive-agent-workflow スキル] §3)。マージ後に親セッションが EditMode/PlayMode 両方の green を確認する。
  K2/K3 とも NgoNetBridge(実 NGO 接続)に閉じた変更を含むため、既存の慣習([docs/29] §7/§9/§11)に合わせて
  ユニットテストだけでなく実機/ローカル結合確認が必要(下記 v5 手順参照)。

### 実装メモ（2026-09-18、K2 再修正: `rtt_app_ms`/`rtt_app_stale` の実バグ修正）

v5 実機確認([docs/29] §16.2)で、上記 K2 の修正が「実機でしか出ない実時間依存のバグ」を含んでいたことが
判明した。真因は 2 つ:

1. `_lastPingSentRealtime` を「経過時間の基準点」に使っていたが、Ping ループが Pong の有無に関係なく
   1 秒ごとにこの値を上書きしていた。そのため通信停止中も基準点が毎秒リセットされ、`AppRoundTripMs` は
   1 秒周期のノコギリ波(実測 372/714/370/370/370/633/… で上下)にしかならず、停止がどれだけ長引いても
   増え続けなかった。
2. `IsAppRoundTripMsStale` は `_awaitingPong`(Ping 送信〜Pong 到達の間、Ping 周期 1 秒に対して RTT は
   数百 ms)を見るだけだったため、**平常運用でも毎秒約 20% の時間 true になっていた**。実際、復旧後の
   正常な行(`rtt_app_ms` が実測値どおり)でも `rtt_app_stale=1` になる矛盾が観測された。

修正方針(いずれも今回見逃した「実時間経過」を EditMode テストで再現できるよう、Unity API 非依存の
`AppRoundTripTracker`(`Runtime/Net/AppRoundTripTracker.cs`)に状態遷移を切り出し、`NgoNetBridge` はこれに
委譲する形にリファクタした):

- **経過時間の基準**: 「未応答のまま最も古い Ping の送信時刻」(応答待ちのストリークが始まった時刻)に
  変更した。Ping が何回再送されてもこの基準点は動かず、Pong を受信した瞬間だけクリアされる。「最後に
  Pong を受信した時刻」を基準にする案も検討したが、(a) まだ 1 度も Pong を受信できていない接続直後からの
  断線を素直に扱えない、(b) 応答待ちが始まった時点そのものを指すためダウンタイムの下限としてより正確
  (最大 1 Ping 周期ぶん過大評価しない)、の 2 点で採用しなかった。
- **`rtt_app_stale` の意味**: 「連続 3 回(`AppRoundTripTracker.DefaultStalePongMissThreshold`)Pong が
  返っていない」場合だけ true にするよう変更した。Pong は `NetChannel.Unreliable` で配送されるため単発の
  ロスは日常的に起こりうる(1 回の未達だけでは stale にしない)が、3 回連続(≒3 秒間無応答)は実際の通信
  途絶とみなせる、かつ [docs/29] §16 の実機確認(6 秒切断)の範囲内で十分早く検出できる、という理由で選んだ。
- **`NetDebugOverlay`**(表示: `App RTT: … ms (stale)` → `App RTT: … ms (途絶疑い)`)と `NetCheckRunner`
  (heartbeat の `rtt_app_stale` ログのコメント)も新しい意味に合わせて更新した。
- **単体テスト**: `Tests/Editor/AppRoundTripTrackerTests.cs` に、今回見逃した「1 秒周期で Ping を送り続けた
  まま Pong が返らない」状況を明示的に再現するテスト(`AppRoundTripMs_GrowsMonotonically_...`。次の Ping
  送信直前という修正前バグが観測されたのと同じタイミングでサンプリングし単調増加を確認する)と、平常運用
  中は `IsAppRoundTripMsStale` が一度も true にならないことを確認するテストを追加した。
- **検証**: compile 0 エラー、EditMode 814 件(旧 808 件 + 追加 6 件)/ PlayMode 689 件がいずれも green
  (2026-09-18)。**実機での再確認(ビルドを作り直して [docs/29] §13 項目 1 を再実施)は未実施**([docs/29]
  §16.3 参照)。

### 実装メモ（2026-09-15、6-5: ContentHash 生成対象外のカタログ Validator）

`ContentHashCatalogCoverageValidator`(`Editor/Validation/`)は `AssetCatalog` が `AssetDataBase` を
継承しないため、`ValidatorRegistry.RunAll`(`AssetDataBase` だけを列挙)には自然に乗らない。
`IUniversalValidator` として登録しつつ渡された `data` は無視し、`ValidationContext` ごとに 1 回だけ
プロジェクト内の全カタログ(`AddressablesSync.FindCatalogs`)を走査する
(`AddressablesRegistrationValidator._noSettingsReported` と同じ「1 回だけ実行」ガード手法)。
判定基準は「Addressables に登録され、かつ `DDriveCatalog` ラベルを持つか」— これは
`AddressablesSync.SyncAll()`/`EnsureCatalogEntry` が全カタログに一律付与することを前提にした既存の
システム不変条件([02_core_framework.md] §5「カタログ自体もグループ DDrive_Catalogs にラベル
DDriveCatalog で登録され、起動オブジェクトがラベルから集められる」)をそのまま検査するものであり、
新しいルールを追加したわけではない。「新しい AssetType が生成器に未登録」という失敗モードは、
ContentHash 生成器(`CatalogContentHasher`)が種別ごとの登録リストを持たない設計のため構造的に
発生しない(§7 実装メモ参照)。詳細は §7 実装メモ、テストは
`Tests/Editor/ContentHashCatalogCoverageValidatorTests.cs`。

## 11. 導入方針（マルチプレイは最初から対応）

マルチプレイは将来拡張ではなく **v1 の必須要件**とする。各機能は単体で作ってから通信対応を後付けするのではなく、**対応する Manager の実装と同じフェーズで通信対応まで完成させる**（[11_tasks.md](11_tasks.md) に組込済み）。

| 時期 | 実装内容 |
|---|---|
| Phase 0 | NetBridge 抽象 + Loopback + **NGO アダプタ** / ITimeSource(NetworkTime) / NetMode フラグ / Seed 決定的乱数 |
| Phase 2 | VFX/SE の Cosmetic 配送（Broadcast + Unreliable バッチ） |
| Phase 4 | Prefab の Simulated Spawn（サーバー権威生成 + NetworkObject 検証） ✅ 2026-09-11(4-13。NetworkObject の実複製は Phase 6 で接続) |
| Phase 5 | Presentation ネット再生（開始時刻シーク / Signal 中継 / 予測再生）+ Late Join 復元 |
| Phase 6 | カタログ ContentHash 照合 / 受信検証・レート制限 / ネット Validator / 2 クライアント自動テスト |

- 各マイルストーンのデモは**常に 2 クライアント + サーバー構成で実施**する（[12_review.md](12_review.md)）。シングル動作のみのデモは合格としない
- CI に Loopback ⇔ NGO の両ブリッジでの PlayMode テストを含め、「シングルでしか動かない実装」の混入を機械的に防ぐ

## 12. MS2026 移植方針（統一ルール・2026-09-08）

D-Drive は最終的に **MS2026（LAN 内 1v1、NGO 2.13.2、Host+Client 方式）** へ移植する。ネットワーク関連は MS2026 の `Docs/Networking.md` を正とし、D-Drive 側の用語・実装・パッケージをそれに揃える。

| 項目 | MS2026 のルール | D-Drive での対応 |
|---|---|---|
| ネットコード | NGO **2.13.2** 固定（勝手に上げない。上げるときは全員同時） | `manifest.json` を 2.13.2 に統一（旧 2.2.0 から更新）。Multiplayer Play Mode 2.0.2 / Multiplayer Center 1.0.1 も同一 |
| 接続モデル | Host（= Server + Client）+ Client。専用サーバなし。IP 直打ち | D-Drive の「Server」は MS2026 の「Host」を指す。`INetBridge.IsServer` = Host 判定 |
| 権威 | 判定・スコア・勝敗は Host。クライアントは入力を送り、結果を受け取って描くだけ | `NetMode.Simulated` = Host 権威生成（Phase 4）。`NetMode.Cosmetic` = 「きっかけ（イベント）」を配って各自ローカル再生（§3/§4 と同じ思想）。Client 発の Cosmetic は Host 経由で中継（§2） |
| 同期してよいもの | 位置・状態・HP・「攻撃した/被弾した」イベント。パーティクル 1 粒・UI・カメラ揺れは同期しない | VFX/SE は ID + 位置だけを送る `VfxNetMsg`/`SeNetMsg`。Canvas/UI/CameraShake は `Local`。HitStop/Shake は Presentation の Signal を受けて各自ローカル発火（§5） |
| 乱数 | `Random.Range` をホストとクライアントで別々に呼ばない。ホストで引いて送る／シード同期 | `SeedRandom` + `PresentationPlayMsg.Seed`（§6）。`AnchorPoint` のランダム散らばりは **見た目専用**なので各自ローカルで可（結果に影響しない） |
| 時刻 | `Time.time` 基準の同期判定禁止。ネットワーク時刻を使う | `ITimeSource` 注入。`NetworkTimeSource` が `NetworkManager.ServerTime` を返す（§6） |
| static 状態 | ゲーム状態を static に持たない | D-Drive の静的ファサード（`Vfx`/`Audio`）は **Manager 実体への参照だけ**を持ち、状態は Manager インスタンス側。Host/Client は別プロセスなので衝突しない（MPPM の Virtual Player も別プロセス） |
| RPC 頻度 | `Update()` 内で毎フレーム RPC を投げない | Cosmetic は Tick 内でバッチ → 1 パケット（§8）。Client→Host 依頼はレート制限 |
| Prefab 登録 | NetworkObject を持つ Prefab は NetworkPrefabsList 登録必須。`Assets/DefaultNetworkPrefabs.asset` は `Assets/` 直下のまま | `NgoNetBridge` を載せる Prefab も登録対象。`DefaultNetworkPrefabs.asset` は移動しない（D-Drive でも `Assets/` 直下） |
| 初期化 | `Awake()` ではなく `OnNetworkSpawn()` | `NgoNetBridge` は状態を持たない。購読は各 Manager のコンストラクタ、Bind は `OnNetworkSpawn` 以降に行う |
| ログ | `[Net]`、Host/Client を分けて `[Net/Host]` `[Net/Client]` | `NgoNetBridge` のログを同プレフィックスに統一 |
| テスト | 1 台での確認だけで「動いた」と言わない。MPPM → 実機 2 台 + 実 LAN | [12_review.md](12_review.md) §5 の「M2 以降は 2 クライアント + サーバー構成」と同じ。`NetBridgeSmokeTest` を MPPM で回す |

**移植時の配置（2026-09-20 改訂、P-1）**: 旧方針（`Assets/DDrive/` をそのままコピーして持ち込む）は撤回した。[42_distribution.md](42_distribution.md) §3.2 の決定により、D-Drive は **UPM パッケージ（git URL 参照）** として持ち込む。

```json
"com.ddrive.core": "git+ssh://git@github.com/wrenchsun/D-Drive.git?path=Packages/com.ddrive.core#v1.0.0"
```

の 1 行を MS2026 の `Packages/manifest.json` に追加する（`?path=` でリポジトリ内のサブフォルダ `Packages/com.ddrive.core/` を指定、`#v1.0.0` でタグ固定。リポジトリは private のままなので MS2026 側にも `git+ssh` の認証（SSH 鍵）が要る）。ゲームコード（`Assets/_Project/Scripts/`）は `DDrive.Runtime`（と `DDrive.Foundation`）のみを参照する（`DDrive.Editor` 参照禁止、この方針は変わらない）。`Assets/GameData/` はカタログごと**移すのではなく MS2026 側で新規に作る**（[42] §2.1 の分類「G」。パッケージは版固定の読み取り専用、データは持ち込み先ごとに持つ）。更新は「manifest のタグを書き換えるだけ」（[42] §4.2）。詳細な線引き・境界違反・互換性ポリシーは [42_distribution.md] を参照。

MS2026 側の `Docs/Networking.md` が `[ServerRpc]`/`[ClientRpc]` を主要 API として挙げているが、NGO 2.x では統一 RPC `[Rpc(SendTo.*)]` が推奨（旧属性の `RequireOwnership` は Obsolete 警告）であり、D-Drive の bridge は統一 RPC で書く。移植時に MS2026 側の文書も同じ記述に揃える。

## 13. NGO を任意依存にする asmdef 分離（2026-09-20、P1-1）

[42_distribution.md] §2.3-9/§7 A-7 の決定（`versionDefines` で `DDRIVE_NGO` を切る）を実際に asmdef 分離まで進めた。従来は `DDrive.Runtime.asmdef` が `Unity.Netcode.Runtime` を直接 `references` していたため、`versionDefines` で `DDRIVE_NGO` シンボルを立てても **参照自体は外れておらず**、NGO 未導入の持ち込み先では `DDrive.Runtime` アセンブリごとコンパイル対象外になり、D-Drive 全体が動かなくなる欠陥があった（[47_review_p_tickets_2026-09-20.md] P1-1）。

**構成**:

| アセンブリ | 依存 | 存在条件 |
|---|---|---|
| `DDrive.Runtime`（既存） | `Unity.Netcode.Runtime` を**参照しない** | 常に存在 |
| `DDrive.Runtime.Ngo`（新設、`Packages/com.ddrive.core/Runtime/Ngo/`） | `DDrive.Foundation`/`DDrive.Runtime`/`Unity.Netcode.Runtime` | `defineConstraints: ["DDRIVE_NGO"]` + 自身の `versionDefines`(`com.unity.netcode.gameobjects` → `DDRIVE_NGO`)。NGO 未導入時はアセンブリごとコンパイル対象外 |
| `DDrive.Samples.NetCheck`（新設、`Samples~/NetCheck/`） | 同上 + `DDrive.Runtime.Ngo` | 同上。`NetCheckRunner`/`NetBridgeSmokeTest` はここに移設し、`Samples~/Demo`(`PresentationSkillSlashDemo` のみ)から分離した |
| `DDrive.Tests.Runtime.Ngo`（新設、`Tests/Runtime/Ngo/`） | 同上 | 同上。`PrefabNetworkObjectValidatorTests` 等 NGO 型に依存するテストのみ |

`NgoNetBridge`/`NgoTransportConfigurator`/`NetDebugOverlay` は `Runtime/Net/` から `Runtime/Ngo/` へ移設した（`.meta` ごと移動、GUID 不変。namespace は互換のため `DDrive.Runtime.Net` のまま変えていない）。

**Bootstrap との接続**: `DDriveRuntimeBootstrap`（`DDrive.Runtime`、NGO 非依存）は NGO 型を一切参照しない。`Runtime/Net/NetBridgeFactory.cs`（`DDrive.Runtime`）に `INgoBridgeFactory`/`NetBridgeFactoryRegistry`(静的レジストリ)を定義し、`DDrive.Runtime.Ngo` 側の `NgoBridgeFactoryInstaller` が `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` で自分自身をここへ登録する。`DDriveRuntimeBootstrap.ResolveNetBridge()` は `NetBridgeFactoryRegistry.Current` が登録されていればそれを使い（NetworkManager/NgoNetBridge の解決・トランスポート設定・デバッグオーバーレイ生成・`StartHost`/`StartClient` の遅延実行をすべて `NgoBridgeFactory` 側が行う）、無ければ（NGO 未導入、またはシーンに `NetworkManager` が無い）警告のうえ `LocalLoopbackBridge` にフォールバックする。NGO 未導入時は登録が一切起きないため、Bootstrap 側は null チェックだけで安全に分岐できる（詳細は [02_core_framework.md] §14）。

**Inspector 直参照の移設**: Bootstrap が持っていた `NetworkManagerRef`/`NgoBridgeRef`（NGO 型の Inspector 直参照）は `DDrive.Runtime.Ngo` アセンブリの補助コンポーネント `DDriveNgoBootstrapHook` へ移した。NGO を使うシーン（`NetCheckScene.unity` 等）では、Bootstrap と同じ GameObject にこのコンポーネントを追加し、`NetworkManager`/`NgoNetBridge` を明示的に割り当てる（未設定ならシーンから自動検索する。既存の挙動を変えない）。`ClientDisconnected` の購読は `INetBridge` 自体が持つイベントに一本化したため（`NetBridge.ClientDisconnected`）、NGO 型を経由しない。

**`PrefabDataValidator` の NetworkObject 検査**: `DDrive.Runtime.Prefab.PrefabDataValidator`（NGO 非依存）から NetworkObject 型を直接参照する検査だけを `DDrive.Runtime.Ngo` の `PrefabNetworkObjectValidator`（別クラス、同じ `AssetType.Prefab` を対象にする `IValidator`）へ切り出した。`CI.DiscoverValidators`(TypeCache、全ロード済みアセンブリが対象)が自動発見するため、`Validation > Run All` への登録作業は不要。

**互換性への影響**: `DDriveRuntimeBootstrap` の public フィールド `NetworkManagerRef`/`NgoBridgeRef` の削除はシリアライズ形式・公開 API の破壊的変更にあたる（1.0.0 発効前の例外として実施。CHANGELOG.md の互換性節・[42_distribution.md] §5.13 参照）。`Samples~/Demo` の構成変更（`NetCheckRunner`/`NetBridgeSmokeTest` を `Samples~/NetCheck` へ移設）も `package.json` の `samples` を 2 件に変更した。

## 14. 実装メモ（2026-09-22、N-1: 開発用の手動接続 API）

**背景**: D-Drive は MS2026（4 人対戦: Host 1 + Client 3）へ持ち込まれる。開発用テストプレイで「LAN 外の特定 IP を入力 → 接続 → テストプレイ」をしたいが、6-0 で実装した `DDriveRuntimeBootstrap` は `Start()` の `StartNetworkingIfPending()` が常に自動で `StartHost()`/`StartClient()` を呼んでしまうため、実行中に IP を選ぶ余地が無かった（起動引数 `-ddrive-host`/`-ddrive-port` は起動前に固定する必要がある）。追加のみ（[42_distribution.md] §5 の互換性ポリシー）で、既存の Auto 起動の挙動・既定値は一切変えていない。

**設計**:

- `NetLaunchRole` に `Manual`(末尾追加)を追加。`-ddrive-net manual` でも指定できる（`NetLaunchArgs.Parse`）。
- `DDriveRuntimeBootstrap.NetStartMode`(新規 enum、`Auto`/`Manual`)+ `DefaultNetStart`(既定 `Auto`)。`DefaultNetBridge=Ngo` かつ `DefaultNetStart=Manual` のとき、CLI 未指定なら実効役割は `Manual` になる。判定は Unity API 非依存の純関数 `NetLaunchArgs.ResolveEffectiveRole(cliRole, defaultBridgeIsNgo, defaultStartIsManual)` に切り出し、EditMode テスト（`NetLaunchArgsTests`）で「既定値(Loopback/Auto)なら常に Off を返す」ことを含めて検証している。CLI の `-ddrive-net` が指定されていれば常にそちらが優先される（既存どおり）。
- 役割が `Manual` のとき、`NgoBridgeFactory.Create()`（`Runtime/Ngo/NgoBridgeFactoryInstaller.cs`）は `NetworkManager`/`NgoNetBridge` の解決・`NetDebugOverlay` の生成までは行うが、`NgoTransportConfigurator.TryConfigure`/`ConfigureAppLayerSimLatency`/`StartHost`/`StartClient` の呼び出しはすべて後述の API 呼び出し時まで遅延する（`NgoBridgeCreateResult.PendingStart` は `null` になり、`Start()` の `StartNetworkingIfPending()` は無害な no-op で終わる）。
- `DDriveRuntimeBootstrap` に公開 API を追加:
  - `public bool StartHost(ushort port)` — 指定 Port で Host として開始（接続先アドレス表示は既存の `DefaultHostAddress`/`-ddrive-host` のまま。既にリスニング中なら警告して `false`）。**手動 Host は `"0.0.0.0"` で listen する**（レビュー指摘、2026-09-22 追記。下記「listenAddress」参照。Auto の Host は従来どおり `DefaultHostAddress`/`-ddrive-host` に bind する挙動を変えていない）。
  - `public bool StartClient(string address, ushort port)` — 指定 IP:Port へ Client として接続。
  - `public void StopNetworking()` — `NetworkManager.Shutdown()`。未接続なら警告して no-op。
  - `public bool IsNetworkStarted` — `NetworkManager.IsListening` を薄くラップした読み取り専用プロパティ。
  - 現在の役割（Host/Client）は重複を避けるため新規プロパティを設けず、既存の `NetBridge.IsServer`/`NetBridge.IsClient`（`NetDebugOverlay` と同じ判定基準）をそのまま使う。
  - これらは `NetBridgeMode.Loopback` のとき、または NGO 未導入/シーンに `NetworkManager`+`NgoNetBridge` が無いとき（`_manualStartHost`/`_manualStartClient`/`_manualStop`/`_isNetworkStartedQuery` が `null` のまま）は警告してから no-op / `false` を返す（例外で止めない、CLAUDE.md §0-4）。`StartHost`/`StartClient` は Auto/Manual どちらの役割でも「既に接続中」なら警告して `false` を返す実装は `NgoBridgeFactory` 側の delegate 内に閉じており、Bootstrap の Update ループやフィールドで重複して状態を持たない。
  - 実処理は `NetworkManager`/`NgoNetBridge` 型を `DDrive.Runtime` へ露出させないため、既存の `PendingStart`/`AssignHashGate` と同じパターンで `NgoBridgeCreateResult` に `Func<bool> IsListening`/`Func<ushort,bool> ManualStartHost`/`Func<string,ushort,bool> ManualStartClient`/`Action ManualStop` を追加し、`DDrive.Runtime.Ngo` 側（`NgoBridgeFactoryInstaller.cs`）が実装する。これらは役割（Host/Client/Manual）に関わらず常に用意されるため、Auto で自動起動したセッションを後から `StopNetworking()` で止める用途にも使える。

**listenAddress（レビュー指摘、2026-09-22 追記）**: `NgoTransportConfigurator.TryConfigure` に省略可能引数 `string listenAddress = null`（既存呼び出しは無変更 = 挙動不変、`DDrive.Runtime.Ngo` アセンブリの API のため互換性スナップショット〔`DDrive.Foundation`/`DDrive.Runtime` のみ対象〕には現れない）を追加し、内部で `SetConnectionData(host, port, listenAddress)` へそのまま渡す。UnityTransport の `SetConnectionData(ipv4, port, listenAddress=null)` は `ServerListenAddress = listenAddress ?? ipv4` になるため、`listenAddress` 省略時（Auto の Host はこちら）は接続先アドレス（`DefaultHostAddress`/`-ddrive-host`、既定 `"192.168.137.1"` はホットスポット時代の値）にそのまま bind される。**手動 Host（`DoManualStartHost`）はこれだと LAN 外・別 LAN からのテストプレイでそのマシンに存在しない IP へ bind しようとして listen に失敗する**ため、`listenAddress: "0.0.0.0"`（全インタフェースで listen）を明示的に渡すよう修正した。`Address` 側（接続先として案内する IP）は従来どおり `host` のまま。ログも `listen=0.0.0.0:{port}` に変更した。

**見送り（本チケットのスコープ外、N-2〜N-4 として `docs/11_tasks.md` にチケット枠を追加済み）**:

- N-2: 開発用の接続 UI（IP 入力欄 + Host/Client ボタン、`StartHost`/`StartClient`/`StopNetworking`/`IsNetworkStarted` を呼ぶだけの薄い EditorWindow or ランタイム UI）。
- N-3: `NetCheckScene`/`NetCheckRunner` の N クライアント対応（現状は 1v1 前提）。`NetDebugOverlay`/Host 側の heartbeat ログに接続クライアント数を出す。
- N-4: 1v1 前提で書かれている当事者判定（HitStop 等、[14_networking.md] 各所の「Client」を単数として扱っている箇所）の 4 人（Host+3 Client）対応。

**テスト**: `Packages/com.ddrive.core/Tests/Editor/NetLaunchArgsTests.cs` に `-ddrive-net manual` のパースと `ResolveEffectiveRole` の 4 パターン（CLI 優先・既定 Loopback は常に Off・Ngo+Auto=Host・Ngo+Manual=Manual）を追加。`NgoNetBridge`/`NgoBridgeFactoryInstaller` は `NetworkBehaviour`/`NetworkManager` 依存のため EditMode 化できず、実機/PlayMode での手動確認が必要（[29_network_device_test.md] の手順を流用可能）。

**実機確認が必要な項目（未検証、レビュー指摘 2026-09-22 追記）**:

- 手動 Host が実際に `0.0.0.0` で listen し、別 LAN・LAN 外のマシンから `StartClient(その IP, port)` で接続できること（本チケットの主目的そのもの）。
- `StopNetworking()` → 再度 `StartHost`/`StartClient` を呼ぶ「再起動」経路。シーンに配置した `NgoNetBridge` は `NetworkObject` であり、`NetworkManager.Shutdown()` 後にその `NetworkObject` が再 Spawn される（= 2 回目の `StartHost`/`StartClient` でも `NgoNetBridge` が機能する）かは NGO のシーン管理の実装依存で、EditMode では検証できない。実機/PlayMode で「切断 → 再接続」を実際に試すこと。

## 15. 実装メモ（2026-09-22、N-2: 開発用の接続 UI）

**背景**: N-1 で追加した手動接続 API（`StartHost`/`StartClient`/`StopNetworking`/`IsNetworkStarted`）はコードから呼ぶ薄い API のみで、実行中に IP を入力する導線が無かった。実機（Unity の無いビルド済み exe を動かす PC）ではスクリプトから叩けないため、EditorWindow ではなく**ランタイム UI**が要る。追加のみ（[42_distribution.md] §5 の互換性ポリシー）。

**設計**:

- `Runtime/Net/NetManualConnectInput.cs`（`DDrive.Runtime` アセンブリ、Unity API 非依存の `static class`）: UI の入力検証を切り出した純関数。
  - `TryParsePort(string port, out ushort portValue, out string error)` — Host 開始は Port だけが要る（接続先アドレスは既存の `DefaultHostAddress`/`-ddrive-host` のまま）ため単独で公開。空/空白/非数値/0/65536 以上は失敗、1〜65535 は成功。
  - `TryParse(string ip, string port, out string address, out ushort portValue, out string error)` — Client 接続用。内部で `TryParsePort` を先に呼ぶ（Port が不正なら IP を見るまでもなく失敗）。IP は **IPv4 のドット表記のみ許可**（ホスト名は不可。DNS 解決に処理を依存させないため）。`"localhost"`（大小無視）だけ例外的に `"127.0.0.1"` に読み替える。`System.Net.IPAddress.TryParse` は使わない（IPv6・短縮表記まで受理してしまうため、独自に「0-255 を `.` で 4 つ区切っただけの表記」を検証する）。
  - テスト: `Tests/Editor/NetManualConnectInputTests.cs`（EditMode、25 件。空/空白/null/非数値/0/65535/65536/前後空白/ホスト名/IPv6/オクテット数不正/オクテット範囲外/`localhost`/大文字小文字/通常系/前後空白/全 0/全 255 を網羅）。
- `Runtime/Ngo/NetManualConnectOverlay.cs`（`#if DDRIVE_NGO`、`NetDebugOverlay` と同じ「確認用のためだけ」の `OnGUI` 方式。専用の uGUI Canvas は作らない。ADR-4 とは無関係）: IP/Port 入力欄 + 「Host で開始」「Client で接続」「切断」ボタン + 状態 1 行を描画する。
  - 表示位置は画面**左下**（`Rect(8, Screen.height-height-8, 240, 150)`）。`NetDebugOverlay` は左上（`Rect(8,8,260,168)`）のため重ならない。
  - 状態 1 行（未接続 / Host listening / Client 接続中 / 切断）は `IsNetworkStarted`（≒ `NetworkManager.IsListening`）・`Bridge.IsServer`/`Bridge.IsClient`・`NgoNetBridge.IsConnected` から導く（`NetDebugOverlay` の役割・接続状態判定と同じ基準）。文字列の組み立ては 4 値のいずれかが変わったときだけ行い（`RefreshStateTextIfChanged`）、`OnGUI` の毎フレーム経路では文字列連結・GC を発生させない（[CLAUDE.md] §0-3 の趣旨）。
  - 入力欄（IP/Port の `TextField`）は接続中（`IsNetworkStarted=true`）は `GUI.enabled=false` で編集不可にする。
  - ボタン押下時にだけ `NetManualConnectInput.TryParsePort`/`TryParse` を呼び、失敗ならエラーメッセージを状態行の下にもう 1 行表示する（例外を投げない）。
  - `NetworkManager`/`NgoNetBridge` 型を `DDrive.Runtime` へ露出させないため、実処理は N-1 で用意済みの `NgoBridgeCreateResult` の delegate（`IsListening`/`ManualStartHost`/`ManualStartClient`/`ManualStop`）をそのまま受け取って呼ぶ（`DDriveRuntimeBootstrap.StartHost`/`StartClient`/`StopNetworking`/`IsNetworkStarted` の実体と同じもの）。`DDriveRuntimeBootstrap`（`DDrive.Runtime.Loop` 名前空間、実は `DDrive.Runtime` アセンブリの一部なので `DDrive.Runtime.Ngo` から直接参照できるが）には依存しない設計にした。既存の `PendingStart`/`AssignHashGate` と同じパターンを踏襲するため。
  - 最後に接続した IP/Port は `PlayerPrefs`（キー `DDrive.Net.Manual.LastAddress`/`DDrive.Net.Manual.LastPort`）に保存し、次回の初期値にする（開発用ツールのため簡易な永続化で十分。`OptionStore`/`DDriveProjectSettings` 等の正式な永続化とは無関係）。
- `Runtime/Ngo/NgoBridgeFactoryInstaller.cs`（`NgoBridgeFactory.Create`）に生成箇所を追加: `role == NetLaunchRole.Manual` のときだけ、`Debug.isDebugBuild || Application.isEditor` を満たせば `NetManualConnectOverlay` を生成する（`NetDebugOverlay` と同じ場所、別の `GameObject` に `AddComponent`）。満たさない（= 開発ビルド/エディタ以外でリリースビルドに `-ddrive-net manual` が渡された）場合は生成せず、`NgoBridgeFactory` 内の `static bool` フラグで警告を 1 回だけ出す（`NgoTransportConfigurator.WarnOnce` と同じ考え方）。`DDriveRuntimeBootstrap` に新しい Inspector フィールドは追加していない（Manual 役割そのものが開発用のため）。

**テスト**: `Tests/Editor/NetManualConnectInputTests.cs`（EditMode、新規 25 件）。`NetManualConnectOverlay`/`NgoBridgeFactoryInstaller` の変更は `NetworkBehaviour`/`NetworkManager`/`OnGUI` 依存のため EditMode 化できず、実機/PlayMode での目視確認が必要（[29_network_device_test.md] §23 に手順を追加）。EditMode 1148/1148・PlayMode（`DDrive.Tests.Runtime`）754/754 green（Unity MCP 経由で確認済み）。互換性スナップショット（`public-api-DDrive.Runtime.txt`）は `NetManualConnectInput` の追加のみを反映して更新済み（`Tools > D-Drive > Compat > スナップショットを更新`）。

**未実施（メモリ制約）**: `NetCheckBuilder.Build()` での開発ビルド作成 → 2 プロセス（両方 `-ddrive-net manual`）での Host 開始/Client 接続/切断/`StopNetworking()` → 再 `StartHost` の実機確認は、実装完了時点で空きメモリが約 1.2GB（閾値 1.3GB 未満）だったため見送った。次回、空きメモリに余裕があるときに実施すること。`NetCheckRunner`（`Samples~/NetCheck/`）は `_role` を `Start()` 時点で 1 度だけ確定させており、Manual モードで `WhenReady` 完了時点ではまだ `StartHost`/`StartClient` を呼んでいないため `RoleOf(bootstrap)` が `"off"` に固定される問題があるが、これは `-ddrive-autotest` 経由の自動判定シナリオでのみ意味を持ち、本チケットの手動 UI 経由の接続確認では `NetCheckRunner` を使わないため、N-3 の範囲として手を付けなかった。

## 16. 実装メモ（2026-09-22、N-3: NetCheckScene/NetCheckRunner の N クライアント対応）

**背景**: MS2026 は Host 1 + Client 3 の 4 人対戦。6-7 で作った自動確認（`NetCheckRunner`/`Tools/CI/run-netcheck.cmd`）は Host 1 + Client 1 の 2 プロセス前提だった。加えて P-5/[47_review_p_tickets_2026-09-20.md] 対応（2026-09-20）で `NetCheckRunner`/`NetBridgeSmokeTest` が `Samples~/NetCheck/`（Unity が import しない領域）へ移されたため、開発リポジトリでは未コンパイルの状態になっており、`Assets/GameData/PreviewScenes/NetCheckScene.unity` が Runner（GUID `d282ccc47598e5d4bbf65db83cf4e65c`）を missing script として参照する状態になっていた。本チケットはこの復旧と N クライアント対応を合わせて行う。

**復旧（移設）**: `Samples~/NetCheck/NetCheckRunner.cs`/`NetBridgeSmokeTest.cs`（+ `.meta`）を `git mv` で `Runtime/Ngo/NetCheck/` へ移し、既存の `DDrive.Runtime.Ngo` アセンブリ（`defineConstraints: ["DDRIVE_NGO"]`、`autoReferenced: true` のため R3 は versionDefines 経由で自動参照される）に含めた。`.meta` を一緒に移動したため GUID は不変（`NetCheckScene.unity` の参照は壊れない）。名前空間を `DDrive.Samples` から `DDrive.Runtime.Net`（`DDrive.Runtime.Ngo.asmdef` の `rootNamespace` と同じ）に揃えた。`Samples~/NetCheck/DDrive.Samples.NetCheck.asmdef` は削除し、`package.json` の `samples` から NetCheck エントリを削除した（Demo のみ残る）。理由: MS2026 側でも同じ Runner を 4 人対戦の確認に使いたい・開発リポジトリの `run-netcheck.cmd` を常にコンパイル可能な状態に保ちたいため。

**副作用（ForbiddenApiScanner）**: `Samples~/`（または `/Samples/`）配下は `ForbiddenApiScanner`（[00_requirements.md] §5 の禁止 API を静的走査するツール、[12_review.md] §3）の対象外だが、`Runtime/Ngo/NetCheck/` は通常の製品コード配置のため対象に入り、`NetCheckRunner.cs`/`NetBridgeSmokeTest.cs` が使う `UnityEngine.Time.deltaTime`/`Time.time`（`ITimeSource` を使わない直接参照は禁止）に新規で引っかかることが分かった。これらは確認用シーン専用のヘッドレス自動テストコード（実プレイの定常経路 Tick/Spawn/Play ではない）で、移設前は Samples 扱いとして規約の対象外だった経緯があるため、`ForbiddenApiScanner` の除外パスに `/Runtime/Ngo/NetCheck/` を追加してこの扱いを維持した（回帰テスト `ForbiddenApiScannerTests.Scan_ExcludesRuntimeNgoNetCheckFolder`）。

**役割の遅延評価**: `NetCheckRunner` は `_role` を `Start()` 時点で確定させていたが、N-2 の Manual モード（`-ddrive-net manual`）では `WhenReady` 完了時点でまだ `StartHost`/`StartClient` が呼ばれておらず `_role` が `"off"` に固定される問題があった（N-2 実装メモの「未実施」節に記載）。`Update()` の先頭で `_role` が `"host"`/`"client"`/`"server"` のいずれでもない間だけ毎フレーム `RoleOf(bootstrap)` を再評価し、これらのいずれかに変わった瞬間に `ready` ログを出すよう修正した。Auto モードは `Start()` 時点で既に確定しているため、この分岐は最初の 1 回で条件が false になり以後は素通りする（既存の Auto 経路の挙動・ログは不変）。

**接続クライアント数**: `NgoNetBridge` に `public int ConnectedClientCount` を追加した。`IsServer` のときだけ、**Host 自身を除いたリモート Client の数**を返す（`NetworkManager.ConnectedClientsIds.Count` から、Host が `IsClient` でもある〔= Host、自分自身の 1 人分〕を引く。専用サーバー〔`IsServer` かつ `!IsClient`〕は引かない。負にならないよう Max(0, …)）。**2026-09-22 レビュー指摘で修正**: 当初は `NetworkManager.ConnectedClientsIds.Count` をそのまま返していた（NGO は `StartHost()` 時に Host 自身の `LocalClientId` も `ConnectedClientsIds` に含めるため、Host 1 + Client 3 が全員繋がった状態で 4 になっていた）。これだと `Run-NetCheck.ps1` が渡す `-ddrive-expect-clients 3`（=リモート Client の人数）に対して、Client が 2 人しか繋がっていなくても「Host+Client2人=3」で誤って `MaxConnectedClientsObserved>=ExpectedClientCount` を満たしてしまい、N-3 の主目的（Client 3 人全員が繋がったことの確認）を判定できない実バグだった。現在は Host 1 + Client 3 が全員繋がった状態で `ConnectedClientCount=3`（Host を除く）になり、MS2026 の「Clients: 3」という直感とも一致する。Client では常に 0（他クライアントの一覧は NGO のセキュリティ上取得できない）。`NetCheckRunner` の Heartbeat に `clients=<n>`（Host 役以外は `-1`。既存の `activeCount`/`vfx_active` の「対象外は -1」という表現と揃えた）を追加し、`NetDebugOverlay` にも「Clients: n」行を追加した（Host のときだけ表示。`NetManualConnectOverlay.RefreshStateTextIfChanged` と同じ「値が変わったときだけ文字列を組み立てる」パターンに従い、`OnGUI` の毎フレーム経路で無駄な文字列連結をしない）。

**N クライアント判定**: `NetLaunchArgs`/`NetLaunchOptions` に `-ddrive-expect-clients <n>`（`int?`、純関数パーサ + EditMode テスト）を追加した。`NetCheckCounters` に `ExpectedClientCount`/`MaxConnectedClientsObserved` を追加し、`NetCheckJudge.Evaluate` は `ExpectedClientCount > 0`（Host 役のときだけ非 0 になるよう `NetCheckRunner.EvaluateResult` 側で絞り込む。Client 役・未指定は 0 のまま = 従来どおりスキップ）のとき `MaxConnectedClientsObserved >= ExpectedClientCount` を PASS 条件に加える（`expected_clients_not_reached` で FAIL）。`MaxConnectedClientsObserved` は「一度でも到達した実績」の最大値なので、quad_leave のように途中で 1 人抜けても既に満たしていれば FAIL にならない。Host 側は `NgoNetBridge.ClientDisconnected` から、他 Client が抜けたことを示す `client_left=<clientId>` ログを（Host 役のときだけ、既存の `disconnected=1` 用の 1 回だけの dedupe フラグとは独立に、抜けるたびに）出す。

**互換性への影響**: `NetLaunchOptions`（フィールド追加 `ExpectedClientCount`）・`NetCheckCounters`（フィールド追加 `ExpectedClientCount`/`MaxConnectedClientsObserved`）・`NetLaunchArgs`（定数追加 `ExpectClientsFlag`）はいずれも `DDrive.Runtime` の公開 API への**追加のみ**（MINOR）。`public-api-DDrive.Runtime.txt` を更新した。`NgoNetBridge.ConnectedClientCount`・`NetDebugOverlay`/`NetCheckRunner`/`NetBridgeSmokeTest` の変更は `DDrive.Runtime.Ngo` アセンブリ（互換性スナップショット対象外）。`Samples~/NetCheck` をパッケージ本体（`DDrive.Runtime.Ngo`）へ移動し `package.json` の `samples` から削除したことも記録する（[42_distribution.md] §2.1/§2.2 の該当箇所を更新済み）。

**テスト**: `Tests/Editor/NetLaunchArgsTests.cs`（`-ddrive-expect-clients` のパース 3 件 + 既存の「全フラグ together」テストへの追加）・`Tests/Editor/NetCheckJudgeTests.cs`（`ExpectedClientCount`/`MaxConnectedClientsObserved` の 4 パターン）・`Tests/Editor/ForbiddenApiScannerTests.cs`（`Runtime/Ngo/NetCheck/` 除外の回帰）を追加。`NetCheckRunner`/`NgoNetBridge.ConnectedClientCount`/`NetDebugOverlay` の変更自体は `NetworkBehaviour`/`NetworkManager`/`OnGUI` 依存のため EditMode 化できず、`Tools/CI/run-netcheck.cmd` の quad シナリオ（[29_network_device_test.md] §24）と実機（§25）での確認が必要。

**シナリオ**: `Tools/CI/Run-NetCheck.ps1` に既存 4 シナリオ（`pair0`/`pair200`/`latejoin`/`disconnect`、無改修）とは別の配列 `$quadScenarios` で `quad0`/`quad_latejoin`/`quad_leave`/`quad_hostquit`（Host 1 + Client 3）を追加した。ポートは既存（7801/7811/7821/7831）と重ならない 7841/7851/7861/7871 を使う。判定は既存と同じ 2 段構え（各プロセス自身の `RESULT=PASS|FAIL` 行 + このスクリプトによるクロスログの Signal 位相差）を Client 3 本ぶん繰り返し、`quad_leave` だけ追加で Host の `client_left`/`clients` 減少を確認する（`Test-ClientLeftAndCountDecrease`）。詳細・実行結果は [29_network_device_test.md] §24。

**検証状況（2026-09-22、PR レビュー対応時点）**: 実装完了直後は空きメモリが 1GB を切って不安定だったが、設定済みの MCP クライアント（isuzu-unity/CoplayDev）のポートが Unity 側の実ポートとズレていたため、instance ファイル（`%LOCALAPPDATA%\UnityMCP\instances\*.json`）から直接 JSON-RPC 経由で接続し、軽量な確認だけ先に実施した。**compile_request → error 0（1 件の実バグを発見・修正。下記「実装メモ（レビュー対応）」参照）**。**EditMode を絞って実行（`Compat|NetCheckJudge|NetLaunchArgs|ForbiddenApiScanner`）→ 99/99 green**（`DDrive.Tests.Editor.Compat.PublicApiSnapshotTests.Runtime_MatchesGolden` を含み、手動更新した `public-api-DDrive.Runtime.txt` が実際のリフレクション結果と一致することを確認できた）。**2026-09-22 追記（main〔N-4 マージ済み〕を取り込み後の再検証）**: `git merge main` で N-4（HitStop/CameraShake/Haptic の Scope）を取り込み（ファイルが異なるため衝突は docs/CHANGELOG のみ）、再度 `compile_status → error 0` を確認した上で **EditMode 全件 → 1164/1164 green**・**PlayMode 全件（`DDrive.Tests.Runtime`）→ 775/775 green** を実施した（詳細・所要時間は [29_network_device_test.md] §24）。**2026-09-22 追記（空きメモリ回復後、ビルド・run-netcheck を実施 → 原因切り分け → 修正 → 最終確認）**: `NetCheckBuilder.Build()` は成功。初回の `Tools\CI\run-netcheck.cmd`（8 シナリオ全部）は**8 件とも FAIL** し、原因は `placeholder_observed`（`ANC_Player_VFXPlayerSlashAnchor`/`SE_test_NewSound` が `Flags.Load=LazyLoad` のままで、同期解決経路〔`AnchorChain.Resolve`/`AssetEventDispatcher`〕では Placeholder に落ちる仕様どおりの挙動。データ側の実バグ）と、新規 quad シナリオの判定ロジック側の設計漏れ（`Test-SignalPhase` の分母が Client の退出時刻を考慮していなかった）の 2 種類と判明した。**両方を修正**（① Data の `Flags.Load` を Preload に変更 + カタログエントリを `AssetCreationService.RegisterExisting` で再同期、② `Test-SignalPhase` に `Get-ClientLastNetworkTime`〔当初「最後の行」ベースで disconnect シナリオに回帰を起こし、「最大値」ベースに再修正〕を追加して分母を Client の生存時間窓に限定）した上で再ビルド・再実行し、**既存 4 シナリオは全て PASS、quad 4 シナリオは新たに判明した別種の判定側の設計漏れ（`forged_cancel_mismatch`。quad 構成では各 Client のログに他 Client 分の破棄ログも見えるため 1v1 前提の集計が破綻する。実プロダクトは正常、判定ロジックのみの問題）だけが残り、それ以外（`placeholder_observed`・Signal 位相差・`client_left`/`clients` 減少）はすべて解消**したことを確認した。追加でユーザー報告に基づき `-ddrive-autotest` 実行時だけ `AudioListener.volume=0f` にする無音化も行った。詳細・ログ抜粋・`forged_cancel_mismatch` の分析は [29_network_device_test.md] §24。**2026-09-22 追記（`forged_cancel_mismatch` も修正）**: `NetCheckRunner` が自分の送った偽造キーを `HashSet<uint>` で覚え、破棄ログに含まれる `HandleNetKey`（`PresentationManager` 側の破棄ログに既に同じ書式で出力済み）がその鍵のときだけ `ForgedCancelDiscardedCount` を数えるよう修正（`NetCheckJudge` は無改修）。詳細は [29_network_device_test.md] §24。**2026-09-22 追記（鍵一致だけでは quad で残存した分の修正）**: 複数 Client が偶然同じ実キーを偽造対象に選ぶと鍵一致だけでは他 Client 分まで数えてしまうため、`NetCheckRunner.OnLogMessageReceived` は発行者不一致の破棄ログ（`PresentationManager` の「送信元 ClientId(N)」を含む文言。`N` は `RequestBroadcastRpc` が伝える真の発行者で Host 中継後も保持される）については `ClientId({自分の LocalClientId})` のときだけカウントするよう追加修正した（未知キー側の破棄ログには送信元が無いため鍵一致のみで判定、`PresentationManager` 側の文言変更は不要だった）。

**実装メモ（レビュー対応、2026-09-22）**: 上記のコンパイル確認で、`Samples~/NetCheck/` から `Runtime/Ngo/NetCheck/` への namespace 変更（`DDrive.Samples` → `DDrive.Runtime.Net`）が原因の実コンパイルエラー（`error CS0234`）を発見した。`DDrive.Runtime.Net` は `DDrive.Runtime` の子 namespace であり、`DDrive.Runtime.Presentation`（Presentation 静的ファサードクラスと同名の兄弟 namespace）も同じ親の下にあるため、未修飾の `Presentation.Play(...)` が namespace `DDrive.Runtime.Presentation` 自身に解決されてしまい、同名の静的クラスに解決されなかった（namespace メンバの直接解決は、ファイル先頭のコンパイル単位スコープにある `using` より優先順位が高いため）。`DDrive.Samples` 名前空間だったときはこの衝突が起きていなかった。`using Presentation = DDrive.Runtime.Presentation.Presentation;` という using エイリアスを **namespace ブロックの内側**に置くことで、この namespace 自身のスコープで先に解決されるようにして修正した（`NetCheckRunner.cs` 冒頭のコメント参照）。
## 17. 実装メモ（2026-09-22、N-4: HitStop/CameraShake/Haptic の Scope=ParticipantsOnly）

**背景**: §5 の 5-8 実装メモで「HitStop は全員が実行する（観戦者を区別しない、既定）」とした判断は MS2026 が 1v1（当事者は必ず 2 人）だった頃の前提であり、要判断として「将来 3 人以上の観戦者が入る構成になった場合、観戦者は HitStop すべきでない可能性がある」と明記していた。MS2026 は 4 人対戦（Host 1 + Client 3）へ移行したため、A が B を殴った瞬間に無関係な C・D の画面まで HitStop/CameraShake で止まる／揺れる問題が実際に起きる。本チケットは「当事者（Self/Target が自分のプレイヤーオブジェクト）だけに絞れる」選択肢を追加する（既定 Everyone は変えない。追加のみ、[42_distribution.md] §5 の互換性ポリシー）。

**設計**:

- `Runtime/Presentation/PresentationTrack.cs` に `enum PresentationEffectScope { Everyone = 0, ParticipantsOnly = 1 }` と、`PresentationTrack.Scope`（末尾追加フィールド、既定 0=Everyone）を追加。HitStop/CameraShake/Haptic の 3 トラックのみ意味を持つ（Vfx/Se 等、他 Kind は無視する。`RequiresAsset` と同じ Kind 限定の慣習）。
- `Runtime/Presentation/PresentationManager.cs` に当事者判定の共有ロジックを追加:
  - `private static bool IsLocalParticipant(INetBridge bridge, ulong selfNetId, ulong targetNetId)` — 「SelfNetId/TargetNetId のどちらかが `bridge.ResolveNetObject(netId)` → `bridge.IsLocalPlayerObject(transform)` で自分の所有物」かを判定する 0 alloc の純関数。未解決（`bridge==null`／`netId==0`／`ResolveNetObject` が null／自分の所有物でない）は `false`。
  - `public static bool IsParticipant(INetBridge bridge, ulong selfNetId, ulong targetNetId)` — `IsLocalParticipant` を包み、**SelfNetId/TargetNetId が両方 0（未解決）のときだけ `true` を返す**（安全側＝従来どおり全員実行。Loopback/シングルプレイの挙動を変えない）。`public static` にしたのは EditMode テストから直接検証するため（`ResolveContextRoot` と同じ設計）。
  - 既存の `FireHaptic`（6-0 で追加済みの `LocalPlayerOnly` 誤爆防止）を `IsLocalParticipant` 経由に書き換えた（従来は `instance.Ctx.Self`/`instance.Ctx.Target` から直接 `IsLocalPlayerObject` を呼んでいたが、`IsParticipant` と解決経路を共有するため `PresentationInstance` に `SelfNetId`/`TargetNetId`（ネット受信 Instance に限り、元の `PresentationPlayMsg.SelfNetId`/`TargetNetId` をそのまま保持。予測再生の Instance では未設定＝既定 0 のまま使わない）を追加し、両方ともそこから解決する形にした。重複コードにしないという要求どおり）。
- **LocalPlayerOnly と Scope の関係（AND で通す）**:

  | `HapticsData.LocalPlayerOnly` | `PresentationTrack.Scope` | 動作 |
  |---|---|---|
  | false | Everyone | 発火する（既定） |
  | false | ParticipantsOnly | 当事者のときだけ発火 |
  | true | Everyone | 自分の事象（Self/Target が自分）のときだけ発火（6-0 の既存挙動） |
  | true | ParticipantsOnly | 自分の事象 **かつ** 当事者（実質同じ判定を二重に通すだけで、意味的には LocalPlayerOnly 単体と同じ） |

  いずれも「ネット受信 Instance（`PlayedViaNetworkReceive=true`）」にのみ効く。予測再生の行為者自身（`PlayedViaNetworkReceive=false`）はどちらの条件も評価されず常に発火する。`LocalPlayerOnly` の未解決時デフォルト（false=鳴らさない）と `Scope` の未解決時デフォルト（true=全員発火）は目的が異なるため意図的に逆にしてある（LocalPlayerOnly は誤爆防止が主目的、Scope は既存の「全員実行」という既定挙動を壊さないことが主目的）。
- `FireCameraShake`/`FireHaptic`/`FireHitStop` に同じ形の早期 return を追加: `if (instance.PlayedViaNetworkReceive && track.Scope == PresentationEffectScope.ParticipantsOnly && !IsParticipant(_netBridge, instance.SelfNetId, instance.TargetNetId)) return;`。`FireHitStop` は本チケットまで `PresentationInstance` を受け取っていなかったため、シグネチャに `instance` 引数を追加した（内部専用の private メソッドのため公開 API への影響なし）。
- `Runtime/Presentation/PresentationDataValidator.cs`: `Scope=ParticipantsOnly` なのに `Flags.Net=Local`（そもそもネット再生されない）なトラックを Info 検査で警告する。
- `Editor/Presentation/PresentationEditorWindow.Tracks.cs`: トラック編集 Inspector で `Kind` が CameraShake/Haptic/HitStop のときだけ `Scope` フィールドを表示する（`PropertyField` + `SerializedObject` バインドの既存パターンをそのまま使い、Undo/`SetDirty` はバインド経由で自動的に行われる）。

**テスト**: `Tests/Editor/PresentationIsParticipantTests.cs`（新規、EditMode）— `IsParticipant` の純関数テスト（両 NetId 0 は安全側 true、片方でも非 0 なら未解決時は false、Self/Target それぞれが自分の所有物なら true 等）。`Tests/Runtime/PresentationParticipantScopeTests.cs`（新規、PlayMode）— HitStop/CameraShake/Haptic それぞれについて (a) Everyone は当事者でなくても発火する回帰、(b) 自分が Self なら発火、(c) 自分が Target なら発火、(d) 第三者は発火しない、(e) SelfNetId/TargetNetId 両方未解決なら安全側で発火、(f) 予測再生の行為者自身は Scope に関わらず発火、を検証（`FakeNetBridge` に `ResolveNetObject` の順引き（`netId → Transform`）が無かったため追加した。既存の `SetNetId`/`ResolveNetId`〔逆引き〕と対になる辞書を追加しただけで既存呼び出しの挙動は変えていない）。`Tests/Runtime/PresentationDataValidatorTests.cs` に Validator の Info 検査を追加。EditMode/PlayMode 実行結果は未検証（Unity MCP 未接続、下記参照）。

**未検証（Unity MCP 未接続）**: このセッションでは isuzu-unity/CoplayDev のどちらの MCP ブリッジにも接続できず、`compile_request`/`test_run` を一度も実行できなかった。コンパイル・EditMode/PlayMode テスト・互換性スナップショットの再生成（`Tools > D-Drive > Compat > スナップショットを更新`）はすべて未実施。次回 Unity Editor 上で必ず確認すること。

## 18. 実装メモ（2026-09-24、N-5: Host 引き継ぎ向けのリセット）

**背景**: MS2026（4 人対戦）は Host 切断時に Host 引き継ぎ（ホストマイグレーション）を行う。正本は
`MS2026/Docs/Spec/03_Network.md` §10（決定: 残っているプレイヤーのうち `PlayerIndex` が最小の人が Host を
引き継ぐ）で、D-Drive 側に必要な対応は同 §10.7 に D-1〜D-5 として一覧化されている。本チケットは D-1/D-3/D-4
（実装必須・仕様明文化）を対応する。D-2/D-5（Stop→再 Start の検証・自動確認シナリオ）は N-6 で対応する。

### D-Drive が前提とする呼び出し順（MS2026 §10.2 のシーケンス）

```
[全端末]     Host 切断を検知
   └─ 各端末が自分で NetworkManager.Shutdown() を呼んでから bootstrap.StopNetworking() を呼ぶ
       （NGO の Shutdown は非同期。同フレームで次の StartHost/StartClient を呼ぶ運用は不可)

[successor]  Migration/GraceSeconds(既定 1 秒、MS2026 Tuning)待ってから bootstrap.StartHost(port)
             （ConnectionData の設定はゲーム側の責務。D-Drive は触らない)

[その他]     Migration/GraceSeconds + 1 秒待ってから bootstrap.StartClient(successor.Address, port)
             （ConnectionData=(SessionToken, PlayerIndex) をゲーム側が NetworkManager.NetworkConfig.
             ConnectionData に入れてから呼ぶ。D-Drive はこの値の中身に関与しない)
```

D-Drive はこの手順を前提に、以下の 2 点を保証する:

1. **`StopNetworking()`(`DoManualStop`)は「MS2026 が先に `NetworkManager.Shutdown()` を呼んでいる」ことを
   想定する**。呼び出し時点で既に `!NetworkManager.IsListening` でも、`Shutdown()` の二重呼び出しはしない
   ものの、ネットワーク状態のリセット（下記「捨てるもの一覧」）は必ず実行する。以前は `!IsListening` で
   即座に警告 + 早期 return していたため、MS2026 の手順（自分で Shutdown 済みのあとに `StopNetworking()`
   を呼ぶ）ではリセットが一切走らなかった。
2. **`StartHost`/`StartClient` の再 Start 前提**: `DoManualStartHost`/`DoManualStartClient` は
   `NetworkManager.IsListening` に加えて `NetworkManager.ShutdownInProgress`(NGO の Shutdown が非同期で
   完了していない間 true)を見て、進行中なら警告して `false` を返す。`StopNetworking()` の直後、同フレームで
   `StartHost`/`StartClient` を呼ぶ運用は D-Drive としては不可（MS2026 側は `Migration/GraceSeconds` だけ
   待ってから呼ぶことでこれを回避する）。

### D-1: `CatalogContentHashGate` が再接続でハッシュを再送しない

`_clientHashSent`/`_clientConnectedFired`（および Host 側の期限辞書・`LastStatusText`）が一度立つと戻らない
ため、新 Host に再接続した Client はハッシュを送らず、リリースビルドでは `ContentHashTimeoutSeconds` 後に
切断される。→ `public void Reset()` を追加した（Client 側フラグ・Host 側の期限/結果辞書・`LastStatusText` を
初期状態に戻す。`SetLocalSummary` で設定したローカルのハッシュ〔`_localCombinedHash`/`_localCatalogs`〕・
`_registryReady` は保持する＝再接続のたびに再計算する必要が無い）。呼び出し箇所:

- `DDriveRuntimeBootstrap.StopNetworking()`
- Client 視点で自分が切断されたとき（`OnNetClientDisconnected` の `!NetBridge.IsServer` 分岐）

テスト: `Tests/Runtime/CatalogContentHashGateTests.cs` に `Reset_ClientSide_AllowsResendingHashAfterReconnect`
（Client が 1 度ハッシュ送信 → Reset → 再度 `ClientConnected` でもう 1 度送る）・
`Reset_HostSide_ClearsPendingDeadlines_AndTimeoutDoesNotFireLater`（Host 側 Reset で期限が消えタイムアウト
判定が走らない）・`Reset_ResetsLastStatusTextToVerifying_ButKeepsLocalSummary`・
`Reset_HostSide_DropsPendingBeforeReadyMessages` を追加した。

### D-3/D-4: 「Stop 時に全 Manager の networked 状態を捨てる」を仕様として保証

`DDriveRuntimeBootstrap` に `private void ResetNetworkedState()` を追加し、`StopNetworking()` と
Client 視点の切断時（`OnNetClientDisconnected`）の両方から呼ぶ。内容は各 Manager の `ResetNetworkedState()`
（新規 public API）をまとめて呼ぶだけ:

```csharp
private void ResetNetworkedState()
{
    Presentation?.ResetNetworkedState();
    Cutscene?.ResetNetworkedState();
    Prefabs?.ResetNetworkedState();
    Audio?.ResetNetworkedState();
    Vfx?.ResetNetworkedState();
}
```

- **`PresentationManager.ResetNetworkedState()`**: 既存の `CancelAllNetworked()`(§5 実装メモ「6-0 修正7」)を
  内包しつつ、ネット由来の台帳（`_networkedHandles`/`_activeNetworked`）・保留キュー
  （`_pendingNetMessages`・未知キー保留リング`_pendingUnknownKeyMessages`）・受信レート制限窓
  （`_signalCancelBudgets`）・未知キー警告済みセット（`_unknownKeyDiscardWarned`）を初期状態に戻す。
  `_registryReady` は変更しない（カタログ登録状態を表すフラグでネットワークの生死とは無関係）。
- **`CutsceneManager.ResetNetworkedState()`**: 同じ設計（`CancelAllNetworked()` を内包 + 台帳・保留キュー・
  `_seekCancelBudgets` をクリア）。
- **`PrefabsManager.ResetNetworkedState()`**: `NetworkManager.Shutdown()` は Host が権威生成した
  `NetworkObject` を巻き込んで破棄するため、`PrefabsManager` の Simulated 台帳（`IsSimulated==true` な
  Instance）はもう存在しない GameObject を指した stale entry になる。通常の `Despawn`（Pool 操作・ネット
  通知）は呼ばず、イベントセッションと台帳からの除去だけ行う。クライアント→サーバー Spawn 要求のレート
  制限窓（`_requestRateLimits`）も併せてクリアする。ローカル（`NetMode!=Simulated`）の Instance には触れない。
  **要判断（N-6 で確認、修正は見送り）**: 上記のとおり `ResetNetworkedState()` は台帳から Simulated
  Instance を外すだけで、その Instance が Pool から借りた（`Flags.Pool.Kind==Pooled`）ものだった場合でも
  `Pool.Return` を呼ばない。`NetworkManager.Shutdown()` 自体が対応する `NetworkObject`（＝ GameObject）を
  破棄してしまうため Pool 側の「貸出中」カウントは実質的な GameObject 実体を失ったまま残り続ける（実害は
  Pool の再利用数がその分減ることだけで、例外やメモリリークにはならない。次回同じ PrefabData を
  `Prefabs.Spawn` すると Pool は新規 Instantiate で補充するため、機能面の破綻はない）。Host 引き継ぎが
  頻発する運用（MS2026 は 1 試合に高々 1〜数回想定）でこの目減りが問題になるなら、`ResetNetworkedState()`
  に「Pooled だった Instance は `Pool.Return` 相当の後始末を行う（ただし GameObject 自体は
  `NetworkManager.Shutdown()` が既に破棄済みのため、Pool の内部カウントだけを補正する専用 API が要る）」を
  追加検討すること。
- **`AudioManager`/`VfxManager` の `ResetNetworkedState()`**: Tick 内でまとめて Broadcast する Cosmetic
  バッチ（`_pendingCosmeticBatch`）を破棄するだけ（このバッチは元々 Tick ごとに Flush される短命な
  リストのため、他に持ち越す状態は無い）。再生中の Instance には触れない。
- **`NgoNetBridge.ResetSessionState()`**: `IsConnected`/`ReceivedMessageCount`/App RTT トラッカー
  （`AppRoundTripTracker.Reset()`）/受信レート制限窓（`_relayBudgets`）/アプリ層遅延キュー（送信・送信先別・
  受信の 3 つ）/偽造 Pong 警告済みフラグを初期状態に戻す。`DoManualStop`（`StopNetworking()` の実処理）から
  呼ぶ。`HandleClientDisconnected` の Client 分岐と似た後始末だが、「切断された」ではなく「自分から明示的に
  Stop した」場合も含めて常に呼べるよう独立したメソッドにした。

**HandleNetKey と役割変更の関係**: `HandleNetKey` は発行時の `LocalClientId`（の下位 8bit）を上位 8bit に
埋め込む（[14] §9）。役割変更後（Client → Host 等）は新しい `LocalClientId` で発行され直す（既存のまま
変更していない）。旧役割で発行した鍵は `ResetNetworkedState()` による台帳クリアで消えるため、
`IsAuthorizedSender` に弾かれる経路（古い鍵の残骸との不一致）自体が発生しない。

**`NetworkTime` が新 Host で 0 から始まる**: `NgoNetBridge.NetworkTime = NetworkManager.ServerTime.Time` は
`StartHost`/`StartClient` のたびに新しい `NetworkManager` の時刻系列から測り直されるため、新 Host では 0 から
始まる（台帳クリアにより `StartNetTime` の比較相手〔旧セッションのネット経由 Instance〕が残っていないため、
これによる実害は無い）。ゲーム側の残り秒（試合時間）は MS2026 が `MatchEndServerTime = ServerTime + 残り秒`
を Freeze 時点の値から再設定することで吸収する（`MS2026/Docs/Spec/03_Network.md` §10.2）。

### MS2026 §10.7 との対応表

| MS2026 # | 内容 | D-Drive での対応 | 状態 |
|---|---|---|---|
| D-1 | `CatalogContentHashGate` が再接続で再送しない | `CatalogContentHashGate.Reset()` | ✅ N-5 |
| D-2 | Stop → 再 Start 経路が未検証 | ローカル複数プロセス（run-netcheck、NGO は 1 プロセスに `NetworkManager` を 1 つしか持てずインプロセス PlayMode 化は不可）で確認。詳細は §19/[docs/29] §26 | ✅ N-6 |
| D-3 | 役割変更（Client → Host）の想定が無い | 各 Manager の `ResetNetworkedState()` + `DDriveRuntimeBootstrap.ResetNetworkedState()` | ✅ N-5 |
| D-4 | `NetworkTime` が新 Host で 0 から始まる | 仕様として明文化(上記)。ゲーム側は `MatchEndServerTime` の再設定で吸収 | ✅ N-5(明文化) |
| D-5 | 4 人 + 引き継ぎの自動確認 | `NetCheckRunner` に host_migration シナリオを追加 | N-6（未着手） |

### テスト

- `Tests/Runtime/CatalogContentHashGateTests.cs`: 上記 D-1 の 4 件。
- `Tests/Runtime/NetResetForHostMigrationTests.cs`（新規）: `FakeNetBridge` を使い、
  (a) `PresentationManager.ResetNetworkedState()` がネット経由の Loop VFX（`StopOnCancel=true`）を強制停止
  しつつ台帳を空にし、ローカル（`NetMode.Local`）の再生には影響しないこと、
  (b) Reset 後に別の `LocalClientId`（役割変更後）で発行した Play/Signal が正常に通ること、
  (c) Registry 未準備の間に届いたネット受信（Presentation/Cutscene）が Reset で保留キューごと捨てられること、
  `PrefabsManager.ResetNetworkedState()` が Simulated 台帳だけを消しローカル Instance に触れないこと、
  `AudioManager`/`VfxManager` の `ResetNetworkedState()` が Tick 内 Cosmetic バッチを破棄すること、を検証する。
- `NgoNetBridge.ResetSessionState()` は `NetworkBehaviour` 派生で EditMode/PlayMode 化できないため、内部で
  使う `AppRoundTripTracker.Reset()` の既存テスト（`Tests/Editor/AppRoundTripTrackerTests.cs`）に委ねる
  （[14] §7 実装メモ「P2-1」と同じ既存の慣習）。`ResetSessionState()` 自体・`DoManualStartHost`/
  `DoManualStartClient` の `ShutdownInProgress` ガードは実機/PlayMode での確認が必要（N-6 のスコープ）。

## 19. 実装メモ（2026-09-24、N-6: Stop→再 Start のローカル確認 + host_migration 自動確認シナリオ）

N-5 で対応した D-1/D-3/D-4 に続き、MS2026 §10.7 の残り D-2（Stop→再 Start 経路の検証）・D-5（4 人 + 引き継ぎの
自動確認）を対応した。

### D-2 の検証方針

NGO は 1 プロセスに `NetworkManager` を 1 つしか持てないため、PlayMode でインプロセス Host+Client を組めない
（[docs/14] §14 の既存の制約と同じ）。D-2 は **ローカル複数プロセス（`run-netcheck`）** で検証する
（実行結果は [docs/29] §26）。

- **in-scene `NetworkObject`（`NgoNetBridge`）の再 Spawn**: NGO は通常、`StartHost()`/`StartServer()` の
  たびに in-scene 配置の `NetworkObject` を内部スイープで自動的に(再)Spawn する。理屈のうえでは
  `NetworkManager.Shutdown()` → 同一プロセスでの `StartHost()` でも同じ経路を通るはずだが、単体テスト
  （Fake/Delayed ブリッジ）はもちろん、PlayMode でも実際の `NetworkManager` を跨いだ Stop→再 Start は検証
  できない制約がある。**念のための保険として**、`NgoBridgeFactoryInstaller.DoManualStartHost` の
  `NetworkManager.StartHost()` 直後に `NgoNetBridge` の `NetworkObject.IsSpawned` を確認し、`false` の
  ときだけ明示的に `Spawn()` する処理を追加した（既に自動 Spawn 済みなら `IsSpawned=true` なので
  `Spawn()` は呼ばれず、二重 Spawn エラーにはならない。Client 側は Host からの同期で Spawn されるため
  同様の処置は不要 = 追加していない）。実際に自動 Spawn で足りていたか、この保険が発火したかは
  `run-netcheck` のログ（`[Net/Host] ... 明示的に Spawn しました` 警告の有無）で確認できる。結果は
  [docs/29] §26 参照。
- 確認項目: `StopNetworking()` → `StartHost`/`StartClient` 後に、`Broadcast`（successor の `signal_fire`
  に対して follower の `signal_recv`）が届くこと、Ping ループ（App RTT）が再開すること（`rtt_app_ms` が
  n/a → 数値に戻ること）、ContentHash が再度 `OK` になること（D-1 の実地確認）。

### `NetLaunchArgs`/`NetCheckRunner` への host_migration 追加

- `NetLaunchOptions` に `MigrationRole`（新規 `enum NetMigrationRole { None, Successor, Follower }`）・
  `MigrationHost`（string、既定 `null` = `-ddrive-host` にフォールバック）・`MigrationPort`（`int?`、既定
  `null` = `-ddrive-port` にフォールバック）を追加。対応する CLI フラグ `-ddrive-migrate successor|follower`・
  `-ddrive-migrate-host <ip>`・`-ddrive-migrate-port <port>` を `NetLaunchArgs.Parse` に追加した（純関数、
  `NetLaunchArgsTests` で検証）。`-ddrive-migrate` 未指定（既定 `None`）の既存 8 シナリオは一切の追加処理を
  行わない（追加のみ、[42_distribution.md] §5）。
- `NetCheckRunner`: Client 役の自分が Host との接続を失った（`_selfDisconnectedObserved` が立った）瞬間、
  `_migrationRole != None` かつ未着手なら `RunHostMigrationAsync` を 1 回だけ起動する。MS2026
  §10.2 と同じ手順を踏む:
  - **successor**: `Migration/GraceSeconds`（1 秒）待って `bootstrap.StopNetworking()` →
    `bootstrap.StartHost(port)`。`false` が返れば `Migration/RetryIntervalSeconds`（2 秒）ごとに再試行し、
    `Migration/ReconnectTimeoutSeconds`（15 秒）で諦めて `migration_failed=1` をログする。
  - **follower**: `Migration/GraceSeconds + 1 秒`（合計 2 秒）待って `StopNetworking()` →
    `StartClient(ip, port)`。同様に再試行・タイムアウト処理を行う。
  - 成功したら `migrated=1 role=host|client newClientId=<LocalClientId>` を 1 回ログする。この Runner
    自身の `_role` フィールドは起動時に一度 `"client"` に確定させたままなので、N-3 で追加した
    「off/unknown の間だけ毎フレーム再評価する」既存の遅延評価ロジックには乗らない（`_role` が既に
    `"client"` = 除外対象のため）。そのため `RunHostMigrationAsync` の成功パスで `_role = RoleOf(bootstrap)`
    を明示的に呼び直す。一方、`Update()` の `PlayAndSignal`/偽造 Cancel 送信の分岐は `_role` 文字列ではなく
    `bootstrap.NetBridge.IsServer` を直接見ているため、`StartHost()` が成功した瞬間から `_role` の更新を
    待たずに自動的に成立する（既存コードを変えていない）。
  - successor は `-ddrive-expect-clients` を「移行後の期待数」として使う（旧 Host 分の実績を引き継がない
    ため、`RunHostMigrationAsync` の成功パス手前で `_maxConnectedClientsObserved`/`_lastClientCount` を
    リセットしてから数え直す）。
  - 切断後カウンタ: follower のみ `_signalRecvAfterMigrationCount`（`_migrationCompleted && _role=="client"`
    の間に `signal_recv` を観測するたび加算）・`_contentHashOkAfterMigration`（同条件下で
    `NetHashGate.LastStatusText=="OK"` を一度でも観測したら sticky で true）を持つ。successor は
    「移行後の期待人数に届いたか」（既存の `ExpectedClientCount`/`MaxConnectedClientsObserved` 判定、上記）で
    確認する側なのでこれらは見ない。
- `NetCheckJudge`: `NetCheckCounters` に `MigrationExpected`/`MigrationCompleted`/`IsSuccessor`/
  `SignalRecvAfterMigrationCount`/`ContentHashOkAfterMigration` を追加。`MigrationExpected` のときだけ
  追加判定を行う（既存 8 シナリオは `MigrationExpected=false` のまま素通り）: `MigrationCompleted` が
  false なら `migration_not_completed` で FAIL。successor はここでは追加判定をせず（`ExpectedClientCount`
  の既存チェックに委ねる）、follower は `SignalRecvAfterMigrationCount<=0` なら `no_signal_recv_after_migration`、
  `ContentHashOkAfterMigration=false` なら `content_hash_not_ok_after_migration` で FAIL。既存の判定
  （⑤ 切断後の演出 0 等）はそのまま適用される（切断直後に一度 0 になる実績があれば満たす。successor/
  follower とも通常のクライアントと同じ切断検知経路を通るため回帰しない）。

### `Run-NetCheck.ps1` の `host_migration` シナリオ

`$migrationScenarios`（`$scenarios`/`$quadScenarios` とは別配列、既存 8 シナリオは無改修）に追加。

| シナリオ | 構成 | 目的 |
|---|---|---|
| `host_migration` | 旧 Host が 12 秒で終了 → Client1（successor）が Stop→StartHost で新 Host に昇格、Client2/Client3（follower）が Stop→StartClient で新 Host（Client1、同一 127.0.0.1:Port）へ再接続 | Host 引き継ぎの一連の流れ（切断検知・successor 昇格・follower 再接続・Signal 中継の復旧・ContentHash 再検証） |

`-ddrive-expect-clients` は旧 Host に `3`（successor+follower×2 全員の接続を終了前に満たす）、successor
（Client1）に `2`（移行後の期待数）を渡す。follower（Client2/3）には渡さない（既存の「Client 役では判定
スキップ」のまま）。全プロセス同一 Port（7881）・`-ddrive-host 127.0.0.1` のため、
`-ddrive-migrate-host`/`-migrate-port` の明示指定は省略している（NetCheckRunner 側の既定
フォールバックで足りる）。

**位相差判定の基準ログ**: 既存の `Test-SignalPhase`（Host ログ vs Client ログ）をそのまま流用し、「Host ログ」
の代わりに **successor（Client1）のログ** を渡す（`$clientLogs[0]`）。Client1 は移行前は Client 役のため
`PlayAndSignal`（`bootstrap.NetBridge.IsServer` のときだけ動く）を一度も呼ばず、ログに現れる `signal_fire`
は移行後の分だけになる。そのため `Get-SignalEvents -Kind fire` は自然に「移行後」だけを拾い、
`Test-SignalPhase` 自体は無改修で流用できる（`Get-ClientConnectNetworkTime`/`Get-ClientLastNetworkTime` は
follower 側の生存窓を見るだけで、基準ログが Host か Client かは問わない設計のため）。

`run-netcheck.cmd host_migration` で単独実行、`run-netcheck.cmd`（引数無し）で既存 8 シナリオと合わせて
9 本すべて実行する。詳細な実行結果・ログ抜粋は [docs/29] §26。

### N-5 で保留した要判断（`PrefabsManager.ResetNetworkedState()` の Pool 台帳残留、記録のみ・修正見送り）

§18 の `PrefabsManager.ResetNetworkedState()` 節に追記済み: Pooled な Simulated Prefab は
`NetworkManager.Shutdown()` で GameObject 実体ごと破棄されるため、`ResetNetworkedState()` が台帳から
Instance を外しても Pool 側の「貸出中」カウントは補正されない（実害は Pool の再利用数が目減りするだけ。
機能上の破綻は無い）。今回のスコープでは修正しない。
