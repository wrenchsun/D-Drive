using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DDrive.Editor.Anim;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Loader;
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
        public void Play_OnSceneAnimator_MovesPose_AndStopRestoresIt()
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
            Assert.AreEqual(0f, _bone.localPosition.x, 1e-4f, "停止で元のポーズに戻る");
            Assert.IsFalse(_driver.Manager.IsPlaying(handle));

            _driver.ReleaseTarget();
            Assert.IsTrue(_target.GetComponent<AnimatorProxy>() == null, "対象解除で付けた Proxy を外す");
            Assert.IsNull(_driver.Current);
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
        public void SetTarget_RejectsPersistentAsset()
        {
            var prefabPath = "Assets/DDrive/Tests/Editor/Temp/SceneAnimDriverTempPrefab.prefab";
            var go = new GameObject("TempPrefabSource");
            go.AddComponent<Animator>();
            try
            {
                if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/DDrive/Tests/Editor/Temp"))
                {
                    UnityEditor.AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
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
