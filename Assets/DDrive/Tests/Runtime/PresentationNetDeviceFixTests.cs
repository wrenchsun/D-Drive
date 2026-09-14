using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Net;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Net;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DDrive.Tests.Runtime
{
    // [11_tasks.md] 6-0(実機確認で見つかった課題の修正) — docs/29_network_device_test.md §8 の
    // 「実機確認で見つかった課題」2/3/4 の回帰テスト。課題1(遅延シミュレーター)/5(切断ログ)は
    // NgoNetBridge(実 NGO 接続)に閉じた変更のため、ここでは PresentationManager 側のロジックだけを
    // FakeNetBridge で検証する(NGO 実接続そのものはローカル結合確認(docs/29 §7)で確認する)。
    public class PresentationNetDeviceFixTests
    {
        private ulong _nextId = 800001;

        private static PresentationData CreateData(params PresentationTrack[] tracks)
        {
            var data = ScriptableObject.CreateInstance<PresentationData>();
            data.Tracks = tracks;
            data.Interruptible = true;
            data.Flags.Net = NetMode.Cosmetic;
            return data;
        }

        private static void RegisterPresentation(AssetRegistry registry, FakeAssetLoader loader, ulong id, PresentationData data)
        {
            data.Id = id;
            var address = "presentation/" + id;
            loader.Assets[address] = data;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new System.Collections.Generic.List<CatalogEntry> { new() { Id = id, Type = AssetType.Presentation, Address = address } });
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            // ResolveOrPlaceholder は _loaded にキャッシュ済みでなければ常に Placeholder を返す(同期経路)。
            // カタログに登録しただけでは足りず、NetPeer.RegisterPresentation と同じく明示的に解決させておく。
            registry.ResolveAsync<PresentationData>(id).GetAwaiter().GetResult();
        }

        // ── 課題3: Registry 準備完了までネット受信の Play をキューへ保留する ──

        [Test]
        public void OnReceivePlayMsg_BeforeRegistryReady_IsQueued_AndFlushedWithoutPlaceholder_AfterReady()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, LocalClientId = 1UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var placeholderUsed = false;
            registry.OnPlaceholderUsed += (_, _) => placeholderUsed = true;

            manager.SetRegistryReady(false);

            var presId = _nextId++;
            const uint handleNetKey = 0x11111111u;

            // Registry にまだ presId が登録されていない状態で PlayMsg を受信させる(Late Join のスナップ
            // ショットがカタログ登録完了より先に届くケースを模す)。senderId=0(Host、TrustedRelayClientId)
            // なので発行者検証は常に通る。
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });

            Assert.AreEqual(0, manager.DebugActiveHandles().Count, "Registry 未 ready の間は即座に処理されず保留される");
            Assert.IsFalse(placeholderUsed, "保留中はまだ ResolveOrPlaceholder を呼んでいない");

            // カタログ登録がようやく完了する(DDriveRuntimeBootstrap.RegisterCatalogsAsync 完了に相当)。
            var t0 = new PresentationTrack { Trigger = TrackTrigger.AtTime, Time = 0f, Kind = TrackKind.Marker, SignalKey = "go" };
            var data = CreateData(t0);
            data.TotalDuration = 5f;
            RegisterPresentation(registry, loader, presId, data);

            manager.SetRegistryReady(true);

            Assert.AreEqual(1, manager.DebugActiveHandles().Count, "ready になった時点で保留分がまとめて処理される");
            Assert.IsFalse(placeholderUsed, "登録済みの ID を正しく解決できており、Placeholder には落ちていない(修正前は Unregistered AssetId 警告が出ていた)");
        }

        // ── 課題2: ネット受信で新規生成された Instance だけ OnNetworkReceivedPlay が発火する ──

        [Test]
        public void OnNetworkReceivedPlay_FiresOnce_ForNewInstance_NotForDuplicateEcho()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = false, LocalClientId = 1UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var presId = _nextId++;
            var t0 = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.Marker };
            var data = CreateData(t0);
            data.TotalDuration = 5f;
            RegisterPresentation(registry, loader, presId, data);

            var firedCount = 0;
            var firedKey = 0u;
            manager.OnNetworkReceivedPlay += (handle, key) =>
            {
                firedCount++;
                firedKey = key;
                Assert.IsTrue(manager.IsPlaying(handle), "通知される Handle は実際に再生中の Instance を指す");
            };

            const uint handleNetKey = 0x22222222u;
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });

            Assert.AreEqual(1, firedCount, "ネット受信で新規生成された Instance について 1 回発火する(6-0 修正2)");
            Assert.AreEqual(handleNetKey, firedKey);

            // 同じ HandleNetKey の確定エコー(既に予測再生/受信済みの分岐)では新規生成が起きないため
            // 再発火しない。
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });
            Assert.AreEqual(1, firedCount, "既存エントリの確定エコーでは再発火しない(二重通知しない)");
        }

        // ── 課題4: 未知の HandleNetKey(または対象が完了済み)の Cancel/Signal は開発ビルドで破棄ログを出す ──

        [Test]
        public void UnknownHandleNetKey_Cancel_LogsDiscardWarning()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            _ = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationCancelMsg.*未知のキー"));
            bridge.InjectReceive(0UL, new PresentationCancelMsg { HandleNetKey = 0x33333333u });
        }

        [Test]
        public void UnknownHandleNetKey_Signal_LogsDiscardWarning()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            _ = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationSignalMsg.*未知のキー"));
            bridge.InjectReceive(0UL, new PresentationSignalMsg { HandleNetKey = 0x44444444u, SignalKeyHash = 1 });
        }
    }
}
