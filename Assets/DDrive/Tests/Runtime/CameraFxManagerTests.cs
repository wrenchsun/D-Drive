using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.Camera;
using NUnit.Framework;
using UnityEngine;
using ShakeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Camera.ShakeMarker>;

namespace DDrive.Tests.Runtime
{
    // [16_camera_haptics.md] Part A / [11_tasks.md] 5-2 — Trauma 合成の上限・GlobalScale・Stop の減衰・
    // Pause を検証する。実カタログ・実 GameData には触れず、Id はテスト専用のダミー値を使う。
    public class CameraFxManagerTests
    {
        private FakeAssetLoader _loader;
        private AssetRegistry _registry;
        private CameraFxManager _manager;
        private GameObject _cameraGo;
        private ulong _nextId = 950001;

        [SetUp]
        public void SetUp()
        {
            _loader = new FakeAssetLoader();
            _registry = new AssetRegistry(_loader);
            _manager = new CameraFxManager(_registry);

            _cameraGo = new GameObject("MainCamera_Test");
            _cameraGo.AddComponent<UnityEngine.Camera>();
            _cameraGo.tag = "MainCamera";
        }

        [TearDown]
        public void TearDown()
        {
            if (_cameraGo != null)
            {
                // カメラを消す前にシェイクノードごと消えるよう、親子関係を辿って破棄する。
                var root = _cameraGo.transform.root;
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private CameraShakeData CreateData(ulong id, Vector3 posAmplitude, int maxStack = 3, float traumaWeight = 1f, float durationSec = 0.3f)
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            data.Id = id;
            data.Pattern = ShakePattern.Impulse; // 波形をなくして振幅の大小だけを検証しやすくする
            data.PosAmplitude = posAmplitude;
            data.RotAmplitude = Vector3.zero;
            data.MaxStack = maxStack;
            data.TraumaWeight = traumaWeight;
            var envelope = data.Envelope;
            envelope.Time = new TimeDef { Mode = TimeMode.Duration, Value = durationSec, SpeedScale = 1f };
            data.Envelope = envelope;
            return data;
        }

        // CameraFxManager はカメラ本体でなく、その直上に挿入した専用ノード(親)を揺らす([16] Part A)。
        // Tick() 実行後はカメラの親がそのノードになるため、オフセットはそちらの localPosition で見る。
        private Vector3 CurrentOffset() => _cameraGo.transform.parent != null ? _cameraGo.transform.parent.localPosition : Vector3.zero;

        [Test]
        public void ShakeData_SingleInstance_OffsetBoundedByAmplitude()
        {
            var data = CreateData(_nextId++, new Vector3(1f, 0f, 0f));
            _manager.ShakeData(data);

            _manager.Tick(0f); // Envelope(t=0) = From(1) なので shakeAmount はほぼ最大

            Assert.LessOrEqual(CurrentOffset().magnitude, 1.01f, "単一 Instance のオフセットは PosAmplitude を超えない");
        }

        [Test]
        public void ShakeData_ManyOverlappingInstances_DoesNotExceedMaxAmplitude()
        {
            // 多重発火で破綻しない(AC): 同じ振幅の Shake を MaxStack いっぱいまで積んでも、
            // 重み付き平均 × shakeAmount(<=1) の性質上、合成後のオフセットが単体の振幅を超えないことを確認する。
            var data = CreateData(_nextId++, new Vector3(1f, 0f, 0f), maxStack: 10, traumaWeight: 1f);

            for (var i = 0; i < 10; i++)
            {
                _manager.ShakeData(data);
            }

            Assert.AreEqual(10, _manager.ActiveCount);

            _manager.Tick(0f);

            Assert.LessOrEqual(CurrentOffset().magnitude, 1.01f, "10 重発火でも合成後オフセットは PosAmplitude(1)を大きく超えない");
        }

        [Test]
        public void ShakeData_MaxStackExceeded_IsIgnored()
        {
            var data = CreateData(_nextId++, Vector3.one, maxStack: 2);

            var h1 = _manager.ShakeData(data);
            var h2 = _manager.ShakeData(data);
            var h3 = _manager.ShakeData(data);

            Assert.IsTrue(_manager.IsPlaying(h1));
            Assert.IsTrue(_manager.IsPlaying(h2));
            Assert.AreEqual(Handle<ShakeMarker>.Invalid, h3, "MaxStack 超過は Invalid Handle を返す");
            Assert.AreEqual(2, _manager.ActiveCount, "MaxStack を超えた分は無視される");
        }

        [Test]
        public void SetGlobalScale_Zero_ProducesZeroOffset()
        {
            var data = CreateData(_nextId++, new Vector3(1f, 1f, 1f));
            _manager.ShakeData(data);
            _manager.SetGlobalScale(0f);

            _manager.Tick(0f);

            Assert.AreEqual(Vector3.zero, CurrentOffset(), "GlobalScale=0 は完全に無揺れ");
        }

        [Test]
        public void Stop_DecaysToZero_ThenRemovesInstance()
        {
            var data = CreateData(_nextId++, new Vector3(1f, 0f, 0f), durationSec: 10f); // 自然減衰では消えない尺
            var handle = _manager.ShakeData(data);
            _manager.Tick(0f);

            var beforeStop = CurrentOffset().magnitude;
            Assert.Greater(beforeStop, 0.5f);

            _manager.Stop(handle, 1f);
            _manager.Tick(0.5f); // フェード半分

            var mid = CurrentOffset().magnitude;
            Assert.Less(mid, beforeStop, "Stop 後は減衰していく");

            _manager.Tick(0.6f); // フェード完了

            Assert.IsFalse(_manager.IsPlaying(handle));
            Assert.AreEqual(Vector3.zero, CurrentOffset(), "フェード完了後はオフセット 0 に戻る");
        }

        [Test]
        public void OnPause_FreezesInstance_DoesNotAdvanceOrExpire()
        {
            var data = CreateData(_nextId++, Vector3.one, durationSec: 0.1f);
            data.Flags.Pause = PauseMode.PauseWithGame;
            var handle = _manager.ShakeData(data);

            _manager.OnPause(PauseChannel.Gameplay, true);
            _manager.Tick(1f); // Pause 中なので進行しないはず

            Assert.IsTrue(_manager.IsPlaying(handle), "Pause 中は Envelope の尺を超えても消えない");
        }

        [Test]
        public void StopAll_StopReason_ImmediatelyClearsAllInstances()
        {
            var data = CreateData(_nextId++, Vector3.one);
            _manager.ShakeData(data);
            _manager.ShakeData(data);

            _manager.StopAll(StopReason.SceneUnload);

            Assert.AreEqual(0, _manager.ActiveCount);
        }

        [Test]
        public void Facade_UnboundCameraFx_ShakeReturnsInvalidHandle_AndIsNoOp()
        {
            CameraFx.Bind(null);

            var handle = CameraFx.Shake(default(ShakeId));

            Assert.IsFalse(handle.IsPlaying());
            Assert.DoesNotThrow(() =>
            {
                handle.Stop();
                CameraFx.SetGlobalScale(0.5f);
                CameraFx.StopAll();
            });
        }
    }
}
