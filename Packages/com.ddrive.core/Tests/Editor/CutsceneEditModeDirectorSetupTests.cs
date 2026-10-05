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
        private const string GameDataRoot = TestTempFolder.Root + "/TempCutsceneEditModeGameData";

        private GameObject _prefab;
        private CutsceneData _data;
        private TimelineAsset _timeline;

        // 前のテストや、同じ Editor での手動のプレビュー(「▶ Timeline ウィンドウで開く」)が残した静的な Manager 群と
        // プレビュー用 Director を、テストの前に捨てる。残っていると、その Registry にはこのテストが作る ModelData が
        // 入っておらず、SpawnModel が解決できない(全件実行の 1 回目だけ落ちることがあった)。
        [SetUp]
        public void SetUp()
        {
            CutsceneEditModeDirectorSetup.TearDown();
            CutsceneEditModePreviewProvider.TearDownForTests();
        }

        private static readonly System.Reflection.MethodInfo OnEditorUpdate = typeof(CutsceneEditModePreviewProvider)
            .GetMethod("OnEditorUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Edit Mode のプレビューがカメラへ書くのは、Timeline ウィンドウがそのプレビュー用 Director を開いている間だけ。
        // 開かれなくなったとき・Director が片付いたとき・プレビュー全体の後始末のときは、書き込む前の姿勢へ戻す
        // (Director は Timeline ウィンドウを閉じても残るので、戻さないとカメラを手で動かせず、保存すると姿勢が残る)。
        [Test]
        public void EditPreview_Camera_IsWrittenOnlyWhileInspected_AndRestoredOnRelease()
        {
            Assert.IsNotNull(OnEditorUpdate, "CutsceneEditModePreviewProvider.OnEditorUpdate(private static)が見つかりません。改名された場合はこの MethodInfo を直してください");
            GameObject createdCamera = null;
            var cam = Camera.main;
            if (cam == null)
            {
                createdCamera = new GameObject("EditPreviewTestCamera", typeof(Camera)) { tag = "MainCamera" };
                cam = createdCamera.GetComponent<Camera>();
            }

            var originalPos = new Vector3(1f, 2f, 3f);
            var originalRot = Quaternion.Euler(10f, 20f, 0f);
            var savedPos = cam.transform.position;
            var savedRot = cam.transform.rotation;
            var savedFov = cam.fieldOfView;
            cam.transform.SetPositionAndRotation(originalPos, originalRot);
            cam.fieldOfView = 50f;

            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _data = ScriptableObject.CreateInstance<CutsceneData>();
            _data.Timeline = _timeline;
            try
            {
                var director = CutsceneEditModeDirectorSetup.EnsureDirector(_data);
                CutsceneEditModePreviewProvider.PrepareContext(director.gameObject);
                var holder = director.GetComponent<CutsceneCameraStateHolder>();
                Assert.IsNotNull(holder);
                holder.HasData = true;
                holder.LocalPos = new Vector3(5f, 6f, 7f);
                holder.LocalRot = Quaternion.identity;
                holder.Fov = 33f;
                var written = director.transform.TransformPoint(holder.LocalPos);

                var inspected = true;
                CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = _ => inspected;

                OnEditorUpdate.Invoke(null, null);
                Assert.Less(Vector3.Distance(written, cam.transform.position), 1e-4f, "Timeline ウィンドウで開いている間はカメラへ書く");
                Assert.AreEqual(33f, cam.fieldOfView, 1e-4f);

                inspected = false; // Timeline ウィンドウを閉じた・別の Director を開いた
                OnEditorUpdate.Invoke(null, null);
                Assert.Less(Vector3.Distance(originalPos, cam.transform.position), 1e-4f, "開かれなくなったら、書き込む前の姿勢へ戻す");
                Assert.Less(Quaternion.Angle(originalRot, cam.transform.rotation), 1e-3f);
                Assert.AreEqual(50f, cam.fieldOfView, 1e-4f);

                // 閉じた後は、カメラを手で動かしても書き戻さない。
                var moved = new Vector3(-4f, 0f, 9f);
                cam.transform.position = moved;
                OnEditorUpdate.Invoke(null, null);
                Assert.Less(Vector3.Distance(moved, cam.transform.position), 1e-4f, "開かれていない間はカメラに触らない");

                inspected = true;
                OnEditorUpdate.Invoke(null, null);
                Assert.Less(Vector3.Distance(written, cam.transform.position), 1e-4f);
                CutsceneEditModeDirectorSetup.TearDown(); // Director が片付いた
                OnEditorUpdate.Invoke(null, null);
                Assert.Less(Vector3.Distance(moved, cam.transform.position), 1e-4f, "Director が無くなったら、書き込む前(手で動かした後)の姿勢へ戻す");

                director = CutsceneEditModeDirectorSetup.EnsureDirector(_data);
                CutsceneEditModePreviewProvider.PrepareContext(director.gameObject);
                holder = director.GetComponent<CutsceneCameraStateHolder>();
                holder.HasData = true;
                holder.LocalPos = new Vector3(5f, 6f, 7f);
                OnEditorUpdate.Invoke(null, null);
                Assert.Less(Vector3.Distance(written, cam.transform.position), 1e-4f);
                CutsceneEditModePreviewProvider.TearDownForTests(); // シーン切替・Play Mode 突入・再コンパイルと同じ後始末
                Assert.Less(Vector3.Distance(moved, cam.transform.position), 1e-4f, "プレビューの後始末でも、書き込む前の姿勢へ戻す");
            }
            finally
            {
                CutsceneEditModePreviewProvider.IsInspectedOverrideForTests = null;
                if (createdCamera != null)
                {
                    Object.DestroyImmediate(createdCamera);
                }
                else
                {
                    cam.transform.SetPositionAndRotation(savedPos, savedRot);
                    cam.fieldOfView = savedFov;
                }
            }
        }
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

        // ── FC-1([51_tdrive_integration.md] §4.2) — SameAsTrack(Edit Mode も Play と同じ 2 パス) ──

        [Test]
        public void EnsureDirector_SameAsTrack_ReferencingSpawnModel_SpawnsOnce_AndSharesBinding()
        {
            var model = CreateModelData(900000000003UL);
            var data = CreateCutsceneWithSpawnModelBinding(model);
            _timeline.CreateTrack<AnimationTrack>(null, "Hero_Ext");
            // 参照元(Hero_Ext)を参照先(Hero)より前に置いても解決できる。
            data.Bindings = new[]
            {
                new CutsceneBinding { TrackName = "Hero_Ext", Target = CutsceneBindTarget.SameAsTrack, SourceTrackName = "Hero" },
                data.Bindings[0],
            };

            var director = CutsceneEditModeDirectorSetup.EnsureDirector(data);

            Assert.AreEqual(1, director.gameObject.transform.childCount, "SameAsTrack は Spawn しない(モデルは 1 体)");
            var hero = director.GetGenericBinding(FindTrack("Hero"));
            Assert.IsNotNull(hero);
            Assert.AreSame(hero, director.GetGenericBinding(FindTrack("Hero_Ext")));

            // 押し直しても溜まらない(返却は参照先の 1 件だけ)。
            director = CutsceneEditModeDirectorSetup.EnsureDirector(data);
            Assert.AreEqual(1, director.gameObject.transform.childCount);
        }

        [Test]
        public void EnsureDirector_SameAsTrack_Unresolvable_BindsNull_WithoutThrowing()
        {
            var model = CreateModelData(900000000004UL);
            var data = CreateCutsceneWithSpawnModelBinding(model);
            _timeline.CreateTrack<AnimationTrack>(null, "A");
            _timeline.CreateTrack<AnimationTrack>(null, "B");
            data.Bindings = new[]
            {
                data.Bindings[0],
                new CutsceneBinding { TrackName = "A", Target = CutsceneBindTarget.SameAsTrack, SourceTrackName = "B" },
                new CutsceneBinding { TrackName = "B", Target = CutsceneBindTarget.SameAsTrack, SourceTrackName = "A" },
            };

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("トラック 'A' が未解決です"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("トラック 'B' が未解決です"));
            var director = CutsceneEditModeDirectorSetup.EnsureDirector(data);

            Assert.IsNull(director.GetGenericBinding(FindTrack("A")));
            Assert.IsNull(director.GetGenericBinding(FindTrack("B")));
            Assert.IsNotNull(director.GetGenericBinding(FindTrack("Hero")));
        }

        private TrackAsset FindTrack(string name)
        {
            foreach (var track in _timeline.GetOutputTracks())
            {
                if (track.name == name)
                {
                    return track;
                }
            }

            Assert.Fail($"トラック {name} が無い");
            return null;
        }
    }
}
