using System.Text.RegularExpressions;
using DDrive.Foundation.Data;
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
    // [11_tasks.md] 6-6(受信検証・レート制限 + ネット Validator) — docs/14_networking.md §9/§10 のうち
    // 「不正 ID 送信(未登録 AssetId・範囲外)が破棄・ログされる」と「PresentationSignalMsg/
    // PresentationCancelMsg のクライアント別レート制限」を検証する。PresentationNetSecurityTests.cs
    // (6-0 の発行者検証・偽造メッセージ)と同じ慣習(FakeNetBridge で受信ハンドラを直接叩く)を使う。
    public class PresentationNetValidationTests
    {
        private ulong _nextId = 900001;

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
            registry.ResolveAsync<PresentationData>(id).GetAwaiter().GetResult();
        }

        // ── 不正 ID 送信: 未登録 AssetId ──

        [Test]
        public void OnReceivePlayMsg_UnregisteredPresId_IsDiscarded_WithoutCreatingPlaceholderInstance()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            var placeholderUsed = false;
            registry.OnPlaceholderUsed += (_, _) => placeholderUsed = true;

            var unregisteredPresId = _nextId++; // カタログに一切登録しない

            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationPlayMsg.*未登録"));
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = unregisteredPresId, HandleNetKey = 0x77777777u, StartNetTime = 0d });

            Assert.AreEqual(0, manager.DebugActiveHandles().Count, "未登録 PresId の Play は Instance を生成しない(Placeholder フォールバックしない)");
            Assert.IsFalse(placeholderUsed, "ネット受信では ResolveOrPlaceholder に到達する前に破棄する([14_networking.md] §9)");
        }

        // ── 不正 ID 送信: 範囲外(種別が Presentation ではない ID) ──

        [Test]
        public void OnReceivePlayMsg_PresIdRegisteredAsDifferentType_IsDiscarded()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            var manager = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            // Presentation 用の PresentationPlayMsg で、実際は Vfx として登録されている ID を送る
            // (「範囲外」= 種別不一致の送信を模す)。
            var wrongTypeId = _nextId++;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new System.Collections.Generic.List<CatalogEntry> { new() { Id = wrongTypeId, Type = AssetType.Vfx, Address = "vfx/" + wrongTypeId } });
            registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();

            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationPlayMsg.*未登録"));
            bridge.InjectReceive(0UL, new PresentationPlayMsg { PresId = wrongTypeId, HandleNetKey = 0x77777778u, StartNetTime = 0d });

            Assert.AreEqual(0, manager.DebugActiveHandles().Count, "種別が Presentation ではない ID の Play は破棄される");
        }

        // ── PresentationSignalMsg/PresentationCancelMsg のクライアント別レート制限(既定 60/秒) ──

        [Test]
        public void OnReceiveSignalMsg_ExceedsRateLimit_IsDiscarded_ButWithinLimitIsProcessed()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            var time = new TimeService();
            var manager = new PresentationManager(registry, time, netBridge: bridge);

            var presId = _nextId++;
            var onHit = new PresentationTrack { Trigger = TrackTrigger.OnSignal, SignalKey = "hit", Kind = TrackKind.HitStop, Params = new[] { ParamValue.Of(0.1f) } };
            var data = CreateData(onHit);
            data.TotalDuration = 5f;
            RegisterPresentation(registry, loader, presId, data);

            // Client(ClientId=1)が行為者の Play を受信させ、以後の Signal を同じ発行者(issuer bit=1)から
            // 送れるようにする(IsAuthorizedSender は HandleNetKey の上位 8bit と senderId の下位 8bit を
            // 比較するため、上位 8bit=1 の HandleNetKey を使う)。
            const uint handleNetKey = (1u << 24) | 0x000001u; // issuer=1
            bridge.InjectReceive(1UL, new PresentationPlayMsg { PresId = presId, HandleNetKey = handleNetKey, StartNetTime = 0d });
            Assert.AreEqual(1, manager.DebugActiveHandles().Count);

            var signalKeyHash = HashSignalKeyForTest("hit");

            // 既知キー(1 回目で HitStop が発火、以後は Fired 済みで無害な no-op)を同じ Client から
            // 60 回送る(レート制限は消費されるが、未知キー保留バッファには一切触れないためノイズが無い)。
            for (var i = 0; i < 60; i++)
            {
                bridge.InjectReceive(1UL, new PresentationSignalMsg { HandleNetKey = handleNetKey, SignalKeyHash = signalKeyHash });
            }

            Assert.AreEqual(0f, time.TimeScale, "1 回目の Signal で HitStop が発火している");

            // 61 件目は authorization/lookup に進む前にレート制限(60/秒/クライアント)で破棄される。
            LogAssert.Expect(LogType.Warning, new Regex(@"PresentationSignalMsg.*レート制限"));
            bridge.InjectReceive(1UL, new PresentationSignalMsg { HandleNetKey = handleNetKey, SignalKeyHash = signalKeyHash });
        }

        // FNV-1a 16bit(PresentationManager.HashSignalKey と同じアルゴリズム)。private のためテスト側で
        // 複製する(PresentationNetSecurityTests.cs の HashSignalKeyForTest と同じ慣習)。
        private static ushort HashSignalKeyForTest(string key)
        {
            unchecked
            {
                const uint fnvPrime = 16777619u;
                var hash = 2166136261u;
                for (var i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= fnvPrime;
                }

                return (ushort)((hash ^ (hash >> 16)) & 0xFFFFu);
            }
        }

        [Test]
        public void OnReceiveSignalMsg_FromHost_IsExemptFromRateLimit()
        {
            var loader = new FakeAssetLoader();
            var registry = new AssetRegistry(loader);
            var bridge = new FakeNetBridge { IsServer = true, LocalClientId = 0UL };
            _ = new PresentationManager(registry, new TimeService(), netBridge: bridge);

            // Host(senderId=TrustedRelayClientId=0)からの Cancel は何回送ってもレート制限に引っかからない
            // (中継/再送を権威側が行うケースを塞がないため)。61 件とも同じ未知キーなので保留バッファ
            // (16件)の退避ログは出るが(無害、LogAssert で検査していないため失敗しない)、「レート制限」
            // のログだけは 1 度も出ないことを直接フックして確認する
            // (LogAssert.NoUnexpectedReceived は退避ログも「未検査」として拾ってしまい使えないため)。
            var rateLimitLogged = false;

            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("レート制限"))
                {
                    rateLimitLogged = true;
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                for (var i = 0; i < 61; i++)
                {
                    bridge.InjectReceive(0UL, new PresentationCancelMsg { HandleNetKey = 0xEEEEEEEEu });
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert.IsFalse(rateLimitLogged, "Host(TrustedRelayClientId)はレート制限の対象外");
        }
    }
}
