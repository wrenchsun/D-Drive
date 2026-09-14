using System.Reflection;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Pause;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDrive.Tests.Editor
{
    // [04_vfx.md] §5(2026-07-28 改定) — SceneView 方式 VFX プレビューの検証。
    public class SceneVfxPreviewDriverTests
    {
        private SceneVfxPreviewDriver _driver;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _driver = new SceneVfxPreviewDriver();
            _prefab = new GameObject("SceneVfxTestPrefab");
            var ps = _prefab.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        [TearDown]
        public void TearDown()
        {
            _driver.Dispose();
            Object.DestroyImmediate(_prefab);
        }

        private VfxData CreateVfxData()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.Id = 1;
            data.Prefab = _prefab;
            data.LifeMode = VfxLifeMode.Loop;
            return data;
        }

        [Test]
        public void Play_SpawnsIntoActiveScene_WithDontSaveFlag()
        {
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);

            Assert.IsNotNull(go);
            Assert.AreEqual(SceneManager.GetActiveScene(), go.scene, "開いているシーンへ直接スポーンされる");
            Assert.AreEqual(HideFlags.DontSave, go.hideFlags, "シーンに保存されないフラグが付く");
            Assert.IsTrue(_driver.HasActive);
        }

        [Test]
        public void Tick_AdvancesWithoutError_AndCleansUpStoppedInstances()
        {
            var handle = _driver.Play(CreateVfxData());

            _driver.Tick(0.016f);
            Assert.IsTrue(_driver.HasActive);

            _driver.Manager.Kill(handle);
            _driver.Tick(0.016f);
            Assert.IsFalse(_driver.HasActive);
        }

        // P5 レビュー第 1 弾 5-4 追補(b、2026-09-14) — ScenePresentationPreviewDriver 経由で
        // TimeService(HitStop 中は TimeScale=0)を渡された場合、自前の Unscaled dt(EditorApplication.update
        // 由来)にも ScaledDeltaTime を掛けてから Tick する。EditorHapticsPreviewDriverTests と同じ手法
        // (private の _lastTickTime を「十分前」に書き換え、Mathf.Clamp で dt を 0.25s に確定させる)で検証する。
        [Test]
        public void HitStop_ScalesEditorTick_ToZero_PreventsShortDurationVfxFromExpiring()
        {
            var time = new TimeService();
            time.HitStop(10f, scale: 0f);

            var driver = new SceneVfxPreviewDriver(timeService: time);
            try
            {
                var data = CreateVfxData();
                data.LifeMode = VfxLifeMode.Duration;
                data.Duration = 0.1f;
                data.FadeOutSec = 0f;

                var handle = driver.Play(data);
                Assert.IsTrue(driver.Manager.IsPlaying(handle));

                SetLastTickSecondsAgo(driver, 10.0);
                InvokePrivateVoid(driver, "EditorTick");

                Assert.IsTrue(driver.Manager.IsPlaying(handle),
                    "HitStop(TimeScale=0)中は 0.25s 分の Unscaled dt もスケールされて 0 になるため、" +
                    "Duration=0.1s の VFX でも失効しない");
            }
            finally
            {
                driver.Dispose();
            }
        }

        [Test]
        public void WithoutTimeService_EditorTick_UsesRawDt_ExpiresShortDurationVfx()
        {
            var data = CreateVfxData();
            data.LifeMode = VfxLifeMode.Duration;
            data.Duration = 0.1f;
            data.FadeOutSec = 0f;

            var handle = _driver.Play(data);
            Assert.IsTrue(_driver.Manager.IsPlaying(handle));

            SetLastTickSecondsAgo(_driver, 10.0);
            InvokePrivateVoid(_driver, "EditorTick");

            Assert.IsFalse(_driver.Manager.IsPlaying(handle), "timeService 無しは従来どおり Unscaled なので失効する");
        }

        private static void SetLastTickSecondsAgo(SceneVfxPreviewDriver driver, double secondsAgo)
        {
            var field = typeof(SceneVfxPreviewDriver).GetField("_lastTickTime", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "SceneVfxPreviewDriver._lastTickTime が見つかりません(実装が変わった場合はテストを追従させてください)");
            field.SetValue(driver, EditorApplication.timeSinceStartup - secondsAgo);
        }

        private static void InvokePrivateVoid(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{instance.GetType().Name}.{methodName} が見つかりません(実装が変わった場合はテストを追従させてください)");
            method.Invoke(instance, null);
        }

        [Test]
        public void Dispose_RemovesSpawnedObjectsFromScene()
        {
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);
            Assert.IsNotNull(go);

            _driver.Dispose();

            Assert.IsTrue(go == null, "Dispose でシーンからスポーン物が破棄される");
        }

        [Test]
        public void Play_ParentsSpawnedObjectUnderPreviewRoot()
        {
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);

            Assert.IsNotNull(go.transform.parent);
            Assert.AreEqual(SceneVfxPreviewDriver.PreviewRootName, go.transform.parent.name);
            Assert.AreEqual(HideFlags.DontSave, go.transform.parent.hideFlags);
        }

        [Test]
        public void Dispose_RemovesPreviewRoot()
        {
            _driver.Play(CreateVfxData());
            var root = _driver.PreviewRoot;
            Assert.IsNotNull(root);

            _driver.Dispose();

            Assert.IsTrue(root == null, "Dispose でまとめ用ルートも破棄される");
        }

        [Test]
        public void ReapplyAnchorToAll_ReflectsEditedOffset()
        {
            var data = CreateVfxData();
            data.Anchor = new DDrive.Foundation.Data.AnchorDef { Space = DDrive.Foundation.Data.AnchorSpace.World, LocalScale = Vector3.one };
            var handle = _driver.Play(data);
            var go = _driver.Manager.GetGameObject(handle);

            data.Anchor.LocalOffset = new Vector3(0f, 0f, 7f);
            _driver.ReapplyAnchorToAll();

            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 7f), go.transform.position), 1e-4f);
        }

        [Test]
        public void Play_WithAttach_ResolvesAnchorUnderAttachTarget()
        {
            var rig = new GameObject("Rig");
            var anchor = new GameObject("Anchor_Test");
            anchor.transform.SetParent(rig.transform);
            anchor.transform.position = new Vector3(3f, 0f, 0f);

            var data = CreateVfxData();
            data.Anchor = new DDrive.Foundation.Data.AnchorDef
            {
                Space = DDrive.Foundation.Data.AnchorSpace.NamedObject,
                Path = "Anchor_Test",
                LocalScale = Vector3.one,
            };

            var handle = _driver.Play(data, rig.transform);
            var go = _driver.Manager.GetGameObject(handle);

            Assert.Less(Vector3.Distance(anchor.transform.position, go.transform.position), 1e-4f);

            Object.DestroyImmediate(rig);
        }
            // ── プレハブモード(Prefab Stage)内での再生 ──

        private const string StageTempFolder = "Assets/DDrive/Tests/Editor/TempPrefabStage";

        private static PrefabStage OpenTempPrefabStage()
        {
            if (!AssetDatabase.IsValidFolder(StageTempFolder))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempPrefabStage");
            }

            var host = new GameObject("StageHost");
            var path = $"{StageTempFolder}/StageHost.prefab";
            PrefabUtility.SaveAsPrefabAsset(host, path);
            Object.DestroyImmediate(host);
            return PrefabStageUtility.OpenPrefab(path);
        }

        private static void CloseTempPrefabStage()
        {
            StageUtility.GoToMainStage();
            if (AssetDatabase.IsValidFolder(StageTempFolder))
            {
                AssetDatabase.DeleteAsset(StageTempFolder);
            }
        }

        [Test]
        public void Play_InPrefabStage_SpawnsIntoStageScene()
        {
            var stage = OpenTempPrefabStage();
            try
            {
                Assert.IsNotNull(stage, "プレハブモードが開く");
                var handle = _driver.Play(CreateVfxData());
                var go = _driver.Manager.GetGameObject(handle);

                Assert.IsNotNull(go);
                Assert.AreEqual(stage.scene, go.scene, "プレハブモード中はステージのシーンへスポーンされる");
                Assert.AreEqual(stage.scene, _driver.PreviewRoot.scene);
                Assert.AreEqual(HideFlags.DontSave, go.hideFlags, "プレハブには保存されない");
            }
            finally
            {
                CloseTempPrefabStage();
            }
        }

        [Test]
        public void PrefabStageClose_ResetsPreview_AndNextPlayGoesToMainScene()
        {
            OpenTempPrefabStage();
            _driver.Play(CreateVfxData());
            Assert.IsTrue(_driver.HasActive);

            CloseTempPrefabStage();

            Assert.IsFalse(_driver.HasActive, "ステージを閉じたら台帳がリセットされる");
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);
            Assert.AreEqual(SceneManager.GetActiveScene(), go.scene, "閉じた後の Play はメインシーンへ戻る");
        }

        [Test]
        public void PrefabStageOpen_WhilePlayingInMainScene_DestroysOldSpawn()
        {
            var handle = _driver.Play(CreateVfxData());
            var go = _driver.Manager.GetGameObject(handle);
            Assert.IsNotNull(go);

            OpenTempPrefabStage();
            try
            {
                Assert.IsTrue(go == null, "メインシーンに残っていたスポーン物は破棄される");
                Assert.IsFalse(_driver.HasActive);
            }
            finally
            {
                CloseTempPrefabStage();
            }
        }
            // ── 対象 Prefab 自身のプレハブモード: その場再生(別インスタンスを出さない) ──

        private static (PrefabStage stage, GameObject prefabAsset) OpenTargetPrefabStage(bool oneShot)
        {
            if (!AssetDatabase.IsValidFolder(StageTempFolder))
            {
                AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "TempPrefabStage");
            }

            var src = new GameObject("TargetFx");
            var ps = src.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = !oneShot;
            main.duration = 0.2f;
            main.startLifetime = 0.1f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var path = $"{StageTempFolder}/TargetFx.prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(src, path);
            Object.DestroyImmediate(src);
            return (PrefabStageUtility.OpenPrefab(path), asset);
        }

        [Test]
        public void Play_InTargetPrefabStage_PlaysInPlace_WithoutSpawning()
        {
            var (stage, asset) = OpenTargetPrefabStage(oneShot: false);
            try
            {
                var data = CreateVfxData();
                data.Prefab = asset;
                Assert.IsTrue(_driver.IsInPlaceTarget(data));

                var handle = _driver.Play(data);

                Assert.AreEqual(SceneVfxPreviewDriver.InPlaceHandle, handle, "擬似ハンドルが返る");
                Assert.IsTrue(_driver.IsPlaying(handle));
                Assert.IsTrue(_driver.HasActive);
                Assert.AreEqual(0, _driver.Manager.ActiveCount, "Manager 経由のスポーンは行わない(二重表示しない)");
                Assert.IsTrue(stage.prefabContentsRoot.GetComponent<ParticleSystem>().isPlaying, "ステージ内の ParticleSystem 自身が再生される");

                _driver.Tick(0.05f);
                Assert.IsTrue(_driver.IsPlaying(handle));

                _driver.Kill(handle);
                Assert.IsFalse(_driver.IsPlaying(handle));
                Assert.IsFalse(_driver.HasActive);
                Assert.IsFalse(stage.prefabContentsRoot.GetComponent<ParticleSystem>().isPlaying);
            }
            finally
            {
                CloseTempPrefabStage();
            }
        }

        [Test]
        public void Play_InTargetPrefabStage_OneShotEndsByItself()
        {
            var (_, asset) = OpenTargetPrefabStage(oneShot: true);
            try
            {
                var data = CreateVfxData();
                data.Prefab = asset;
                data.LifeMode = VfxLifeMode.OneShot;

                var handle = _driver.Play(data);
                Assert.IsTrue(_driver.IsPlaying(handle));

                for (var i = 0; i < 40 && _driver.IsPlaying(handle); i++)
                {
                    _driver.Tick(0.05f);
                }

                Assert.IsFalse(_driver.IsPlaying(handle), "OneShot は粒子が尽きたら終わる");
            }
            finally
            {
                CloseTempPrefabStage();
            }
        }

        [Test]
        public void Play_InTargetPrefabStage_ManagerCallsWithPseudoHandle_AreNoOp()
        {
            var (_, asset) = OpenTargetPrefabStage(oneShot: false);
            try
            {
                var data = CreateVfxData();
                data.Prefab = asset;
                var handle = _driver.Play(data);

                // Manager に擬似ハンドルを渡しても警告なしで無効扱いになる(ウィンドウは Manager を直接呼ぶ箇所がある)。
                Assert.IsFalse(_driver.Manager.IsPlaying(handle));
                Assert.IsFalse(_driver.Manager.TryGetAnchorTarget(handle, out _));
                Assert.AreEqual(Vector3.zero, _driver.Manager.GetAnchorExtraOffset(handle));
            }
            finally
            {
                CloseTempPrefabStage();
            }
        }
            [Test]
        public void Tick_EditMode_OneShotEndsWhenParticlesRunOut()
        {
            var ps = _prefab.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = 0.1f;

            var data = CreateVfxData();
            data.LifeMode = VfxLifeMode.OneShot;
            var handle = _driver.Play(data);
            Assert.IsTrue(_driver.IsPlaying(handle));

            for (var i = 0; i < 40 && _driver.IsPlaying(handle); i++)
            {
                _driver.Tick(0.05f);
            }

            Assert.IsFalse(_driver.IsPlaying(handle), "EditMode の手動 Simulate でも OneShot は粒子が尽きたら終わる(リピートの前提)");
            Assert.IsFalse(_driver.HasActive);
        }
    }
}
