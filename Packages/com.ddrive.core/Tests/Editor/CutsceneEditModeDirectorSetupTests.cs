using DDrive.Editor.Cutscene;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using DDrive.Runtime.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.4 / docs/45 P1-5(2026-09-20) — CutsceneEditModeDirectorSetup のプレビュー用
    // Director が (a) 確認用シーンに保存されない (b) Play Mode で CutsceneManager を経由せず勝手に
    // 再生しない (c) 押し直しても前回の SpawnModel を溜めないことを確認する。
    public class CutsceneEditModeDirectorSetupTests
    {
        private const string GameDataRoot = "Packages/com.ddrive.core/Tests/Editor/TempCutsceneEditModeGameData";

        private GameObject _prefab;
        private CutsceneData _data;
        private TimelineAsset _timeline;

        [TearDown]
        public void TearDown()
        {
            // docs/45 P1-5 — 確認用シーンではなく現在のテストシーンに作られた DontSave の Director/Model を
            // 破棄する(残しても保存はされないが、後続テストへ持ち越さないため)。
            CutsceneEditModeDirectorSetup.TearDown();
            CutsceneEditModePreviewProvider.TearDownForTests();

            if (_prefab != null)
            {
                Object.DestroyImmediate(_prefab);
            }

            if (_timeline != null)
            {
                Object.DestroyImmediate(_timeline);
            }

            if (_data != null)
            {
                Object.DestroyImmediate(_data);
            }

            if (AssetDatabase.IsValidFolder(GameDataRoot))
            {
                AssetDatabase.DeleteAsset(GameDataRoot);
            }
        }

        private ModelData CreateModelData(ulong id)
        {
            var folder = $"{GameDataRoot}/Model";
            EnsureFolder(folder);

            // EditorAnchorRegistry.Build() が ID で解決できるように、実アセットとして保存する
            // (SceneAnimPreviewDriverTests と違い、CutsceneEditModeDirectorSetup は
            // ModelsManager.Spawn(ModelId, Transform) 経由で Registry.ResolveOrPlaceholder を使うため)。
            _prefab = new GameObject("CutsceneEditModeDirectorSetupTestPrefab");

            var model = ScriptableObject.CreateInstance<ModelData>();
            model.Id = id;
            model.Prefab = _prefab;
            AssetDatabase.CreateAsset(model, $"{folder}/MODEL_EditModeDirectorSetupTest_{id}.asset");
            return model;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private CutsceneData CreateCutsceneWithSpawnModelBinding(ModelData model)
        {
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            _timeline.fixedDuration = 1.0;
            _timeline.CreateTrack<AnimationTrack>(null, "Hero");

            _data = ScriptableObject.CreateInstance<CutsceneData>();
            _data.Timeline = _timeline;
            _data.Origin = CutsceneOrigin.World; // アクター探索を避ける。
            _data.Bindings = new[]
            {
                new CutsceneBinding
                {
                    TrackName = "Hero",
                    Target = CutsceneBindTarget.SpawnModel,
                    Model = new AssetId<ModelMarker>(model.Id, AssetType.Model),
                },
            };

            return _data;
        }

        [Test]
        public void EnsureDirector_CreatesDontSaveDirector_WithPlayOnAwakeDisabled()
        {
            var model = CreateModelData(900000000001UL);
            var data = CreateCutsceneWithSpawnModelBinding(model);

            var director = CutsceneEditModeDirectorSetup.EnsureDirector(data);

            Assert.IsNotNull(director);
            Assert.IsTrue((director.gameObject.hideFlags & HideFlags.DontSave) != 0,
                "確認用シーンに保存されてはならない([26_timeline.md] §4.4、docs/45 P1-5)");
            Assert.IsFalse(director.playOnAwake,
                "Play Mode で CutsceneManager を経由せず勝手に再生してはならない(docs/45 P1-5)");
        }

        [Test]
        public void EnsureDirector_CalledTwice_DoesNotAccumulateSpawnedModels()
        {
            var model = CreateModelData(900000000002UL);
            var data = CreateCutsceneWithSpawnModelBinding(model);

            CutsceneEditModeDirectorSetup.EnsureDirector(data);
            var director = CutsceneEditModeDirectorSetup.EnsureDirector(data);

            Assert.AreEqual(1, director.gameObject.transform.childCount,
                "押し直すたびに前回の SpawnModel を返却してから作り直すこと([26_timeline.md] §4.4、docs/45 P1-5)");
        }
    }
}
