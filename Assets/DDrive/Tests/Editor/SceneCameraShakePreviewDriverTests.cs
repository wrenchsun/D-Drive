using DDrive.Editor.CameraFx;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.CameraShake;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [16_camera_haptics.md] §C-2(5-2c) — ShakeEditor の実カメラプレビュー(SceneCameraShakePreviewDriver)。
    // CLAUDE.md §0-7「Edit Mode でカメラを揺らした後は必ず元の姿勢に戻す(Shake ノードは DontSave)」を検証する。
    public class SceneCameraShakePreviewDriverTests
    {
        private SceneCameraShakePreviewDriver _driver;
        private GameObject _rig;
        private GameObject _cameraGo;
        private readonly System.Collections.Generic.List<GameObject> _disabledOtherCameras = new();

        [SetUp]
        public void SetUp()
        {
            _driver = new SceneCameraShakePreviewDriver();

            // 開いているシーンに既存の "MainCamera" タグ付きオブジェクトがあると Camera.main がそちらを
            // 拾ってしまい、このテストのカメラが揺れなくなる。テスト中だけ一時的に無効化し、TearDown で必ず戻す。
            foreach (var go in GameObject.FindGameObjectsWithTag("MainCamera"))
            {
                if (go.activeSelf)
                {
                    _disabledOtherCameras.Add(go);
                    go.SetActive(false);
                }
            }

            _rig = new GameObject("Rig_ShakeDriverTest");
            _cameraGo = new GameObject("MainCamera_ShakeDriverTest");
            _cameraGo.AddComponent<Camera>();
            _cameraGo.tag = "MainCamera";
            _cameraGo.transform.SetParent(_rig.transform, false);
            _cameraGo.transform.localPosition = new Vector3(1f, 2f, 3f);
            _cameraGo.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
        }

        [TearDown]
        public void TearDown()
        {
            _driver.Dispose();
            if (_rig != null)
            {
                Object.DestroyImmediate(_rig); // カメラも子として一緒に消える
            }

            foreach (var go in _disabledOtherCameras)
            {
                if (go != null)
                {
                    go.SetActive(true);
                }
            }

            _disabledOtherCameras.Clear();
        }

        private static CameraShakeData CreateShakeData()
        {
            var data = ScriptableObject.CreateInstance<CameraShakeData>();
            data.Pattern = ShakePattern.Impulse;
            data.PosAmplitude = new Vector3(0.1f, 0.1f, 0f);
            data.RotAmplitude = Vector3.zero;
            data.Envelope = new ValueDef
            {
                Mode = ValueMode.Parametric,
                Parametric = EaseDef.Named(Ease.Linear),
                From = 1f,
                To = 1f,
                Time = new TimeDef { Mode = TimeMode.Duration, Value = 1f, SpeedScale = 1f },
                Loop = LoopMode.Once,
            };
            data.MaxStack = 5;
            data.TraumaWeight = 1f;
            return data;
        }

        [Test]
        public void Play_AttachesShakeNode_AndMarksDontSave()
        {
            var handle = _driver.Play(CreateShakeData());
            _driver.Tick(0.016f);

            var parent = _cameraGo.transform.parent;
            Assert.IsNotNull(parent);
            Assert.AreEqual("DDriveCameraShakeNode", parent.name);
            Assert.AreEqual(HideFlags.DontSave, parent.gameObject.hideFlags & HideFlags.DontSave, "揺れノードはシーンに保存されない(DontSave)");
            Assert.IsTrue(_driver.Manager.IsPlaying(handle));
            Assert.AreEqual(1, _driver.ActiveCount);
        }

        [Test]
        public void MultiplePlays_ComposeTrauma_WithoutBreaking()
        {
            // 連打テスト: 複数回 Play しても例外にならず、Trauma 合成で複数 Instance が共存する。
            for (var i = 0; i < 5; i++)
            {
                _driver.Play(CreateShakeData());
                _driver.Tick(0.016f);
            }

            Assert.AreEqual(5, _driver.ActiveCount);
        }

        [Test]
        public void StopAndRestore_RestoresOriginalParentSiblingAndPose()
        {
            var originalParent = _cameraGo.transform.parent;
            var originalSibling = _cameraGo.transform.GetSiblingIndex();
            var originalLocalPos = _cameraGo.transform.localPosition;
            var originalLocalRot = _cameraGo.transform.localRotation;

            _driver.Play(CreateShakeData());
            _driver.Tick(0.016f);
            Assert.AreNotEqual(originalParent, _cameraGo.transform.parent, "テスト前提: 一旦は揺れノードの子になっている");

            _driver.StopAndRestore();

            Assert.AreEqual(originalParent, _cameraGo.transform.parent, "元の親に戻る");
            Assert.AreEqual(originalSibling, _cameraGo.transform.GetSiblingIndex());
            Assert.AreEqual(originalLocalPos, _cameraGo.transform.localPosition);
            // Quaternion は浮動小数点の丸めで ULP レベルの差が出ることがあるため角度差で比較する(値そのものは一致)。
            Assert.Less(Quaternion.Angle(originalLocalRot, _cameraGo.transform.localRotation), 0.01f);
            Assert.IsNull(GameObject.Find("DDriveCameraShakeNode"), "揺れノードは破棄される");
        }

        [Test]
        public void Dispose_RestoresCamera_EvenWhilePlaying()
        {
            // ウィンドウを閉じたときにカメラが元の位置に戻ることを検証する(AC「閉じるとカメラが元の位置」)。
            _driver.Play(CreateShakeData());
            _driver.Tick(0.016f);

            _driver.Dispose();

            Assert.AreEqual(_rig.transform, _cameraGo.transform.parent);
            Assert.IsNull(GameObject.Find("DDriveCameraShakeNode"));
        }
    }
}
