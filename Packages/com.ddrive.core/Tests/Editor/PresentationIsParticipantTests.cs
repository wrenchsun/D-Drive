using System;
using System.Collections.Generic;
using DDrive.Foundation.Net;
using DDrive.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] N-4 — PresentationManager.IsParticipant(純関数・0 alloc)の EditMode テスト。
    // [14_networking.md] §5 実装メモ(5-8)「HitStop は全員が実行する(観戦者を区別しない、既定)」の
    // 要判断(2026-09-14)を解消する当事者判定の中核関数。実際に HitStop/CameraShake/Haptic が発火するか
    // どうかは PlayMode の Tests/Runtime/PresentationParticipantScopeTests.cs で検証する(ここでは判定関数
    // そのものだけを最小の INetBridge スタブで検証する)。
    public class PresentationIsParticipantTests
    {
        // FakeNetBridge は Tests/Runtime asmdef 内にあり、DDrive.Tests.Editor からは参照できない
        // (asmdef references に含まれていない)ため、Editor テスト専用に必要最小限だけ実装する。
        private sealed class StubBridge : INetBridge
        {
            private readonly Dictionary<ulong, Transform> _objects = new();
            private readonly HashSet<Transform> _localPlayerObjects = new();

            public bool IsServer => false;
            public bool IsClient => true;
            public double NetworkTime => 0d;
            public ulong LocalClientId => 0UL;

            public void Broadcast<T>(in T msg, NetChannel channel) where T : INetMessage
            {
            }

            public void SendTo<T>(ulong clientId, in T msg, NetChannel channel) where T : INetMessage
            {
            }

            public IDisposable Subscribe<T>(Action<ulong, T> handler) where T : INetMessage => null;

            public Transform ResolveNetObject(ulong netId) => _objects.TryGetValue(netId, out var t) ? t : null;

            public ulong ResolveNetId(Transform transform) => 0UL;

            public bool IsLocalPlayerObject(Transform transform) => transform != null && _localPlayerObjects.Contains(transform);

            public ulong SpawnNetworked(GameObject root) => 0UL;

            public void DespawnNetworked(ulong netId, bool destroy)
            {
            }

            public event Action<ulong> ClientConnected;
            public event Action<ulong, string> ClientDisconnected;

            public void DisconnectClient(ulong clientId, string reason)
            {
            }

            public void Register(ulong netId, Transform transform) => _objects[netId] = transform;

            public void SetLocalPlayer(Transform transform) => _localPlayerObjects.Add(transform);
        }

        private GameObject _self;
        private GameObject _target;
        private GameObject _thirdParty;

        [SetUp]
        public void SetUp()
        {
            _self = new GameObject("Self");
            _target = new GameObject("Target");
            _thirdParty = new GameObject("ThirdParty");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_self);
            UnityEngine.Object.DestroyImmediate(_target);
            UnityEngine.Object.DestroyImmediate(_thirdParty);
        }

        [Test]
        public void BothNetIdsZero_ReturnsTrue_SafeDefault()
        {
            var bridge = new StubBridge();
            Assert.IsTrue(PresentationManager.IsParticipant(bridge, 0UL, 0UL));
        }

        [Test]
        public void BridgeIsNull_BothNetIdsZero_ReturnsTrue_SafeDefault()
        {
            Assert.IsTrue(PresentationManager.IsParticipant(null, 0UL, 0UL));
        }

        [Test]
        public void SelfNetId_ResolvesToLocalPlayerObject_ReturnsTrue()
        {
            var bridge = new StubBridge();
            bridge.Register(11UL, _self.transform);
            bridge.SetLocalPlayer(_self.transform);

            Assert.IsTrue(PresentationManager.IsParticipant(bridge, 11UL, 22UL));
        }

        [Test]
        public void TargetNetId_ResolvesToLocalPlayerObject_ReturnsTrue()
        {
            var bridge = new StubBridge();
            bridge.Register(22UL, _target.transform);
            bridge.SetLocalPlayer(_target.transform);

            Assert.IsTrue(PresentationManager.IsParticipant(bridge, 11UL, 22UL));
        }

        [Test]
        public void NeitherResolvesToLocalPlayerObject_ReturnsFalse()
        {
            var bridge = new StubBridge();
            bridge.Register(11UL, _thirdParty.transform);
            bridge.Register(22UL, _target.transform);
            // ThirdParty/Target のどちらも SetLocalPlayer していない = 自分の所有物ではない。

            Assert.IsFalse(PresentationManager.IsParticipant(bridge, 11UL, 22UL));
        }

        [Test]
        public void OneNetIdZero_OtherResolvesButNotLocal_ReturnsFalse()
        {
            var bridge = new StubBridge();
            bridge.Register(22UL, _target.transform);

            Assert.IsFalse(PresentationManager.IsParticipant(bridge, 0UL, 22UL));
        }

        [Test]
        public void NetIdUnresolvable_ButNonZero_ReturnsFalse_NotSafeDefault()
        {
            // 「未解決なら安全側で true」は SelfNetId/TargetNetId が両方 0 のときだけ。片方でも非 0 なら
            // (解決に失敗しても)false のままであることを確認する。
            var bridge = new StubBridge(); // 11 を登録しない = ResolveNetObject が null を返す。

            Assert.IsFalse(PresentationManager.IsParticipant(bridge, 11UL, 0UL));
        }

        [Test]
        public void BridgeIsNull_NonZeroNetId_ReturnsFalse()
        {
            Assert.IsFalse(PresentationManager.IsParticipant(null, 11UL, 0UL));
        }
    }
}
