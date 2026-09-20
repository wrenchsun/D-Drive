using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Editor.Anim;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Loader;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Model;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using VfxId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Vfx.VfxMarker>;

namespace DDrive.Tests.Editor
{
    // [05_model_animation.md] B-4(2026-09-09) — AnimEditor の「シーン(SceneView)で再生」の検証。
    public class SceneAnimPreviewDriverTests
    {
        private sealed class DictLoader : IAssetLoader
        {
            public readonly Dictionary<string, UnityEngine.Object> Assets = new();

            public UniTask<T> LoadAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object
            {
                Assets.TryGetValue(address, out var obj);
                return UniTask.FromResult(obj as T);
            }

            public void Release(string address)
            {
            }

            public UniTask PreloadAsync(IEnumerable<string> addresses, IProgress<float> progress) => UniTask.CompletedTask;
        }

        private SceneAnimPreviewDriver _driver;
        private GameObject _target;
        private Transform _bone;
        private GameObject _vfxPrefab;
        private AssetRegistry _registry;
        private DictLoader _loader;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("SceneAnimTarget");
            _target.AddComponent<Animator>();
            _bone = new GameObject("Bone").transform;
            _bone.SetParent(_target.transform);
            _bone.localPosition = Vector3.zero;

            _vfxPrefab = new GameObject("SceneAnimVfxPrefab");
            _vfxPrefab.AddComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            _loader = new DictLoader();
            _registry = new AssetRegistry(_loader);
            _driver = new SceneAnimPreviewDriver(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            _driver.Dispose();
            UnityEngine.Object.DestroyImmediate(_target);
            UnityEngine.Object.DestroyImmediate(_vfxPrefab);
        }

        private static AnimationClip Clip()
        {
            var clip = new AnimationClip { legacy = true, frameRate = 30f };
            clip.SetCurve("Bone", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            return clip;
        }

        private static AnimData Anim(params AssetEvent[] events)
        {
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.Clip = Clip();
            data.Events = events;
            return data;
        }

        private VfxData RegisterVfx(ulong id)
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            vfx.Id = id;
            vfx.Prefab = _vfxPrefab;
            vfx.LifeMode = VfxLifeMode.Loop;
            var address = $"vfx/{id}";
            _loader.Assets[address] = vfx;
            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            catalog.SetEntries(new List<CatalogEntry> { new() { Id = id, Type = AssetType.Vfx, Address = address } });
            _registry.RegisterCatalogAsync(catalog).GetAwaiter().GetResult();
            _registry.ResolveAsync<VfxData>(id).GetAwaiter().GetResult();
            return vfx;
        }

        [Test]
        public void Play_OnSceneAnimator_MovesPose_StopKeepsPose_RestoreResetsIt()
        {
            var animator = _target.GetComponent<Animator>();
            var handle = _driver.Play(Anim(), animator);
            Assert.IsTrue(_driver.Manager.IsPlaying(handle));
            Assert.AreSame(animator, _driver.Current);
            Assert.IsFalse(_driver.OwnsCurrent, "シーン上の Animator は借用扱い");

            _driver.Tick(0.5f);
            Assert.Greater(_bone.localPosition.x, 0.1f, "EditMode でも Clip がサンプリングされてポーズが進む");

            var proxy = _target.GetComponent<AnimatorProxy>();
            Assert.IsNotNull(proxy, "AnimManager が Proxy を付ける");
            Assert.AreEqual(HideFlags.DontSave, proxy.hideFlags, "こちらで付けた Proxy は保存されない");

            _driver.Stop();
            Assert.IsFalse(_driver.Manager.IsPlaying(handle));
            Assert.Greater(_bone.localPosition.x, 0.1f, "停止ではその瞬間のポーズを残す(2026-09-10 改定)");

            _driver.RestorePoseNow();
            Assert.AreEqual(0f, _bone.localPosition.x, 1e-4f, "「ポーズを戻す」で再生前のポーズに戻る");

            _driver.ReleaseTarget();
            Assert.IsTrue(_target.GetComponent<AnimatorProxy>() == null, "対象解除で付けた Proxy を外す");
            Assert.IsNull(_driver.Current);
        }

        // P5 レビュー第 1 弾 5-4 追補(b、2026-09-14) — ScenePresentationPreviewDriver 経由で
        // TimeService(HitStop 中は TimeScale=0)を渡された場合、自前の Unscaled dt(EditorApplication.update
        // 由来)にも ScaledDeltaTime を掛けてから Tick する。SceneVfxPreviewDriverTests と同じ手法
        // (private の _lastTickTime を「十分前」に書き換え、Mathf.Clamp で dt を 0.25s に確定させる)で検証する。
        [Test]
        public void HitStop_ScalesEditorTick_ToZero_PreventsShortClipFromFinishing()
        {
            var time = new TimeService();
            time.HitStop(10f, scale: 0f);

            var driver = new SceneAnimPreviewDriver(_registry, time);
            AnimationClip clip = null;
            AnimData data = null;
            try
            {
                clip = new AnimationClip { legacy = true, frameRate = 30f };
                clip.SetCurve("Bone", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 0.1f, 1f));
                data = ScriptableObject.CreateInstance<AnimData>();
                data.Clip = clip;

                var animator = _target.GetComponent<Animator>();
                var handle = driver.Play(data, animator);
                Assert.IsTrue(driver.Manager.IsPlaying(handle));

                SetLastTickSecondsAgo(driver, 10.0); // dt は Mathf.Clamp で 0.25s に確定する

                InvokePrivateVoid(driver, "EditorTick");

                Assert.IsTrue(driver.Manager.IsPlaying(handle),
                    "HitStop(TimeScale=0)中は 0.25s 分の Unscaled dt もスケールされて 0 になるため、" +
                    "0.1s の短い Clip でも再生が終わらない");
            }
            finally
            {
                driver.Dispose();
                if (data != null)
                {
                    UnityEngine.Object.DestroyImmediate(data);
                }
            }
        }

        [Test]
        public void WithoutTimeService_EditorTick_UsesRawDt_FinishesShortClip()
        {
            var clip = new AnimationClip { legacy = true, frameRate = 30f };
            clip.SetCurve("Bone", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 0.1f, 1f));
            var data = ScriptableObject.CreateInstance<AnimData>();
            data.Clip = clip;

            var animator = _target.GetComponent<Animator>();
            var handle = _driver.Play(data, animator);
            Assert.IsTrue(_driver.Manager.IsPlaying(handle));

            SetLastTickSecondsAgo(_driver, 10.0);
            InvokePrivateVoid(_driver, "EditorTick");

            Assert.IsFalse(_driver.Manager.IsPlaying(handle), "timeService 無しは従来どおり Unscaled なので 0.1s の Clip は終わる");
            UnityEngine.Object.DestroyImmediate(data);
        }

        private static void SetLastTickSecondsAgo(SceneAnimPreviewDriver driver, double secondsAgo)
        {
            var field = typeof(SceneAnimPreviewDriver).GetField("_lastTickTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "SceneAnimPreviewDriver._lastTickTime が見つかりません(実装が変わった場合はテストを追従させてください)");
            field.SetValue(driver, UnityEditor.EditorApplication.timeSinceStartup - secondsAgo);
        }

        private static void InvokePrivateVoid(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, $"{instance.GetType().Name}.{methodName} が見つかりません(実装が変わった場合はテストを追従させてください)");
            method.Invoke(instance, null);
        }

        [Test]
        public void SpawnModel_PlacesIntoActiveScene_UnderDontSaveRoot_AndDisposeRemovesIt()
        {
            var prefab = new GameObject("SceneAnimModelPrefab");
            prefab.AddComponent<Animator>();
            try
            {
                var model = ScriptableObject.CreateInstance<ModelData>();
                model.Id = 3;
                model.Prefab = prefab;

                var animator = _driver.SpawnModel(model, new Vector3(1f, 0f, 0f), Quaternion.identity);
                Assert.IsNotNull(animator);
                Assert.IsTrue(_driver.OwnsCurrent);
                var go = animator.gameObject;
                Assert.AreEqual(SceneManager.GetActiveScene(), go.scene, "開いているシーンへ配置される");
                Assert.AreEqual(HideFlags.DontSave, go.hideFlags);
                Assert.AreEqual(SceneAnimPreviewDriver.PreviewRootName, go.transform.parent.name);
                Assert.AreEqual(1f, go.transform.position.x, 1e-4f);

                var root = _driver.PreviewRoot;
                _driver.Dispose();
                Assert.IsTrue(root == null, "Dispose でまとめ用ルートごと消える");
                Assert.IsTrue(go == null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void FrameEvent_SpawnsVfxIntoScene_ViaDispatcher()
        {
            RegisterVfx(11);
            var anim = Anim(new AssetEvent { Trigger = EventTrigger.Frame, Time = 15f, Action = EventAction.PlayAsset, Target = AssetRef.From(new VfxId(11, AssetType.Vfx)) });
            var animator = _target.GetComponent<Animator>();

            _driver.Play(anim, animator);
            _driver.Tick(0.4f);
            Assert.IsFalse(_driver.Vfx.HasActive);

            _driver.Tick(0.2f);
            Assert.IsTrue(_driver.Vfx.HasActive, "15 フレーム(0.5s)で VFX がシーンに出る");
            Assert.AreEqual(1, _driver.Vfx.Manager.ActiveCount);

            // DontSave のオブジェクトは FindObjectsByType に出ないため、まとめ用ルートから辿る。
            var root = _driver.Vfx.PreviewRoot;
            Assert.AreEqual(HideFlags.DontSave, root.hideFlags);
            Assert.AreEqual(SceneManager.GetActiveScene(), root.scene, "開いているシーンに出る");
            var spawned = root.GetComponentInChildren<ParticleSystem>(true);
            Assert.IsNotNull(spawned, "VFX は SceneVfxPreviewDriver のまとめ用ルート(DontSave)の下に出る");
            Assert.AreEqual(HideFlags.DontSave, spawned.gameObject.hideFlags);
        }

        [Test]
        public void SetPaused_InvalidHandle_IsRejected()
        {
            _driver.SetPaused(Handle<AnimMarker>.Invalid, true);
            Assert.IsFalse(_driver.IsPaused, "無効 Handle では一時停止にしない(解除経路が無いまま Tick が止まらない。レビュー指摘 4)");
            Assert.IsFalse(_driver.Vfx.Paused);
        }

        [Test]
        public void Play_And_ReleaseTarget_ClearPause()
        {
            var animator = _target.GetComponent<Animator>();
            var handle = _driver.Play(Anim(), animator);
            _driver.SetPaused(handle, true);
            Assert.IsTrue(_driver.IsPaused);
            Assert.IsTrue(_driver.Vfx.Paused);
            _driver.Tick(0.5f);
            Assert.AreEqual(0f, _driver.Manager.GetNormalizedTime(handle), 1e-4f, "一時停止中は進まない");

            var again = _driver.Play(Anim(), animator);
            Assert.IsFalse(_driver.IsPaused, "Play 系の入口で一時停止を解除する");
            Assert.IsFalse(_driver.Vfx.Paused);
            _driver.Tick(0.5f);
            Assert.AreEqual(0.5f, _driver.Manager.GetNormalizedTime(again), 1e-3f);

            _driver.SetPaused(again, true);
            _driver.ReleaseTarget();
            Assert.IsFalse(_driver.IsPaused, "対象解除でも解除される");
            Assert.IsFalse(_driver.Vfx.Paused);
        }

        [Test]
        public void Tick_EditMode_TwoLayersOnSameAnimator_UpdatesAnimatorOnce()
        {
            var animator = _target.GetComponent<Animator>();
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddLayer("Base");
            controller.AddLayer("Upper");
            var clipA = Clip();
            clipA.legacy = false;
            clipA.name = "A";
            var clipB = Clip();
            clipB.legacy = false;
            clipB.name = "B";
            var idle = controller.layers[0].stateMachine.AddState("Idle");
            var stateA = controller.layers[0].stateMachine.AddState("A");
            stateA.motion = clipA;
            controller.layers[0].stateMachine.defaultState = idle;
            var stateB = controller.layers[1].stateMachine.AddState("B");
            stateB.motion = clipB;
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0f);

            var a = Anim();
            a.Clip = clipA;
            a.StateName = "A";
            a.Layer = 0;
            a.DefaultCrossFade = 0f;
            var b = Anim();
            b.Clip = clipB;
            b.StateName = "B";
            b.Layer = 1;
            b.DefaultCrossFade = 0f;

            _driver.Play(a, animator);
            _driver.Play(b, animator);
            _driver.Tick(0.25f);
            _driver.Tick(0.25f);

            // Clip A は Bone.localPosition.x を 0→1 (1 秒) で動かす。CrossFade 直後の最初の Update は遷移の適用に使われるため、
            // Update が 1 回/Tick なら 2 Tick 後は x≈0.25、インスタンス数(2)ぶん呼ばれていると x≈0.75 になる。
            var info = animator.GetCurrentAnimatorStateInfo(0);
            Assert.IsTrue(info.IsName("A"), $"state={info.shortNameHash} normalized={info.normalizedTime}");
            Assert.Greater(_bone.localPosition.x, 0.15f, $"normalized={info.normalizedTime}");
            Assert.Less(_bone.localPosition.x, 0.45f, $"同じ Animator に 2 インスタンスあっても Animator.Update は Tick ごとに 1 回(レビュー指摘 6)。normalized={info.normalizedTime}");
        }

        [Test]
        public void SetTarget_RejectsPersistentAsset()
        {
            var prefabPath = "Packages/com.ddrive.core/Tests/Editor/Temp/SceneAnimDriverTempPrefab.prefab";
            var go = new GameObject("TempPrefabSource");
            go.AddComponent<Animator>();
            try
            {
                if (!UnityEditor.AssetDatabase.IsValidFolder(TestTempFolder.Root + "/Temp"))
                {
                    TestTempFolder.CreateFolder("Temp");
                }

                var asset = UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Project 内の Prefab アセット"));
                _driver.SetTarget(asset.GetComponent<Animator>());
                Assert.IsNull(_driver.Current, "Project 内のアセットは対象にしない");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEditor.AssetDatabase.DeleteAsset(prefabPath);
            }
        }
    }
}
