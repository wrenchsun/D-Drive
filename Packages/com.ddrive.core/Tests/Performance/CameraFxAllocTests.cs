using DDrive.Foundation.Registry;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2 / [16_camera_haptics.md] Part A — CameraFxManager.Tick(毎フレーム、
    // Trauma 合成含む)が 0 alloc であることを検証する。
    public class CameraFxAllocTests
    {
        private AssetRegistry _registry;
        private CameraFxManager _manager;
        private GameObject _cameraGo;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetRegistry(new NullAssetLoader());
            _manager = new CameraFxManager(_registry);

            _cameraGo = new GameObject("PerfTestCamera");
            _cameraGo.AddComponent<Camera>();
            _cameraGo.tag = "MainCamera";
        }

        [TearDown]
        public void TearDown()
        {
            CameraFx.Bind(null);

            if (_cameraGo != null)
            {
                // CameraFxManager はカメラの直上に専用ノードを挿入するため、そのノードごと消えるよう
                // 親子関係を辿って破棄する(既存の CameraFxManagerTests と同じ手順)。
                var root = _cameraGo.transform.root;
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private static CameraShakeData CreateData(ulong id)
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            data.Id = id;
            data.Pattern = ShakePattern.Impulse;
            data.PosAmplitude = new Vector3(1f, 0f, 0f);
            data.RotAmplitude = Vector3.zero;
            data.MaxStack = 5;
            data.TraumaWeight = 1f;

            var envelope = data.Envelope;
            envelope.Time = new TimeDef { Mode = TimeMode.Duration, Value = 1000f, SpeedScale = 1f };
            data.Envelope = envelope;
            return data;
        }

        [Test, Performance]
        public void Tick_ActiveShake_AllocatesNothing()
        {
            var data = CreateData(1);
            _manager.ShakeData(data);

            AllocProbe.AssertZeroAlloc(
                "CameraFx.Tick",
                warmup: () => _manager.Tick(0.016f),
                measured: () => _manager.Tick(0.016f));
        }
    }
}
