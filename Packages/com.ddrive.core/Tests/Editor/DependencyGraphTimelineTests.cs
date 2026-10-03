using System.Collections.Generic;
using System.Linq;
using DDrive.Editor;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Dependencies;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Cutscene.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Tests.Editor
{
    // [51_tdrive_integration.md] §4.8(FC-7) — 依存グラフが Timeline(.playable)のクリップ / マーカー内の
    // AssetId 参照まで届くこと。実 Assets/GameData は触らず、TestTempFolder 配下の一時アセットだけを使う。
    public class DependencyGraphTimelineTests
    {
        private const string TestRoot = TestTempFolder.Root + "/TempDepsTimeline";

        private readonly List<string> _trackedPaths = new();

        [SetUp]
        public void SetUp()
        {
            DependencyGraphPostprocessor.Suppress = true;
            DependencyGraphService.ResetInMemoryCacheForTests();
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                TestTempFolder.CreateFolder("TempDepsTimeline");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_trackedPaths.Count > 0)
            {
                DependencyGraphService.UpdatePaths(null, _trackedPaths);
                _trackedPaths.Clear();
            }

            DependencyGraphService.ResetInMemoryCacheForTests();
            DependencyGraphPostprocessor.Suppress = false;

            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AddressablesSync.RemoveEntriesUnder(TestRoot);
                AssetDatabase.DeleteAsset(TestRoot);
                using (DDrive.Editor.Versioning.VersionStampSuppression.Scope()) { AssetDatabase.SaveAssets(); }
            }
        }

        private static void SetId(Object target, string field, AssetType type, ulong id)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            prop.FindPropertyRelative("value").ulongValue = id;
            prop.FindPropertyRelative("type").enumValueIndex = (int)type;
            so.ApplyModifiedProperties();
        }

        // SE クリップ 1 つを持つ .playable を作る。
        private string CreateSeTimeline(string name, ulong seId)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var path = $"{TestRoot}/{name}.playable";
            AssetDatabase.CreateAsset(timeline, path);
            _trackedPaths.Add(path);

            var track = timeline.CreateTrack<CutsceneSeTrack>(null, "Se");
            var clip = track.CreateClip<CutsceneSeClip>();
            SetId(clip.asset, "SeId", AssetType.Se, seId);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return path;
        }

        [Test]
        public void UpdatePaths_PlayableWithSeClip_IsFoundByFindUsages()
        {
            var path = CreateSeTimeline("ZzTest7Se", 818181UL);

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            var usages = DependencyGraphService.FindUsages(AssetType.Se, 818181UL);
            Assert.IsTrue(usages.Any(u => u.SourcePath == path
                && u.ComponentType == nameof(CutsceneSeClip)
                && u.PropertyPath == "SeId"
                && u.ObjectPath.StartsWith("Se/")),
                "Timeline クリップ内の AssetId が使用箇所に出るはず");
            Assert.IsTrue(DependencyGraphService.FindReferencesIn(path).Any(r => r.TargetType == AssetType.Se && r.TargetId == 818181UL));
        }

        [Test]
        public void UpdatePaths_PlayableWithShakeMarker_IsFoundByFindUsages()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var path = $"{TestRoot}/ZzTest7Marker.playable";
            AssetDatabase.CreateAsset(timeline, path);
            _trackedPaths.Add(path);

            var track = timeline.CreateTrack<CutsceneShakeTrack>(null, "Shake");
            var marker = track.CreateMarker<CutsceneShakeNotification>(1.0);
            SetId(marker, "ShakeId", AssetType.Shake, 727272UL);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            var usages = DependencyGraphService.FindUsages(AssetType.Shake, 727272UL);
            Assert.IsTrue(usages.Any(u => u.SourcePath == path && u.ComponentType == nameof(CutsceneShakeNotification)),
                "マーカー内の AssetId も使用箇所に出るはず");
        }

        [Test]
        public void UpdatePaths_PlayableWithPresentationClip_IsFoundByFindUsages()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var path = $"{TestRoot}/ZzTest7Pres.playable";
            AssetDatabase.CreateAsset(timeline, path);
            _trackedPaths.Add(path);

            var track = timeline.CreateTrack<CutscenePresentationTrack>(null, "Pres");
            var clip = track.CreateClip<CutscenePresentationClip>();
            SetId(clip.asset, "PresentationId", AssetType.Presentation, 616161UL);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            DependencyGraphService.UpdatePaths(new[] { path }, null);

            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Presentation, 616161UL)
                .Any(u => u.SourcePath == path && u.ComponentType == nameof(CutscenePresentationClip)));
        }

        [Test]
        public void UpdatePaths_PlayableChangedAndDeleted_UpdatesIndex()
        {
            var path = CreateSeTimeline("ZzTest7Change", 515151UL);
            DependencyGraphService.UpdatePaths(new[] { path }, null);
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, 515151UL).Any(u => u.SourcePath == path));

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            var clip = timeline.GetOutputTracks().First().GetClips().First();
            SetId(clip.asset, "SeId", AssetType.Se, 515152UL);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            DependencyGraphService.UpdatePaths(new[] { path }, null);

            Assert.IsFalse(DependencyGraphService.FindUsages(AssetType.Se, 515151UL).Any(u => u.SourcePath == path));
            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, 515152UL).Any(u => u.SourcePath == path));

            AssetDatabase.DeleteAsset(path);
            DependencyGraphService.UpdatePaths(null, new[] { path });
            Assert.IsFalse(DependencyGraphService.FindUsages(AssetType.Se, 515152UL).Any(u => u.SourcePath == path));
        }

        [Test]
        public void FindUnusedIds_SeReferencedOnlyFromTimeline_IsNotUnused()
        {
            var se = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest7 Se Used", "Category", "ZzTest7SeUsed", gameDataRoot: TestRoot);
            var sePath = AssetDatabase.GetAssetPath(se);
            var other = AssetCreationService.Create(typeof(SeData), AssetType.Se, "ZzTest7 Se Unused", "Category", "ZzTest7SeUnused", gameDataRoot: TestRoot);
            var otherPath = AssetDatabase.GetAssetPath(other);
            _trackedPaths.Add(sePath);
            _trackedPaths.Add(otherPath);

            var timelinePath = CreateSeTimeline("ZzTest7Unused", se.Id);
            AssetSearch.Invalidate();

            DependencyGraphService.UpdatePaths(new[] { sePath, otherPath, timelinePath }, null);

            var unused = DependencyGraphService.FindUnusedIds();
            Assert.IsFalse(unused.Any(u => u.AssetPath == sePath), "Cutscene からだけ参照されている SE は使用中のはず");
            Assert.IsTrue(unused.Any(u => u.AssetPath == otherPath), "参照の無い SE は従来どおり未使用のはず(逆方向の変化なし)");
        }

        [Test]
        public void FindCutscenePathsUsing_ReturnsCutsceneDataThatReferencesTimeline()
        {
            var timelinePath = CreateSeTimeline("ZzTest7Owner", 414141UL);
            var cutscene = AssetCreationService.Create(typeof(CutsceneData), AssetType.Cutscene, "ZzTest7 Cut", "Category", "ZzTest7Cut", gameDataRoot: TestRoot);
            var cutscenePath = AssetDatabase.GetAssetPath(cutscene);
            _trackedPaths.Add(cutscenePath);
            ((CutsceneData)cutscene).Timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            EditorUtility.SetDirty(cutscene);
            AssetDatabase.SaveAssets();
            AssetSearch.Invalidate();

            var owners = DependencyGraphService.FindCutscenePathsUsing(timelinePath);
            CollectionAssert.Contains(owners, cutscenePath);
            Assert.IsEmpty(DependencyGraphService.FindCutscenePathsUsing($"{TestRoot}/NoSuch.playable"));
        }

        [Test]
        public void ClassifyPath_Playable_IsTimeline()
        {
            Assert.AreEqual(ReferenceFileKind.Timeline, ClassifiedReference.ClassifyPath("Assets/X/Cut.playable"));
        }

        [Test]
        public void EnsureLoaded_WithOutdatedCacheVersion_BackfillsPlayables()
        {
            var path = CreateSeTimeline("ZzTest7Backfill", 313131UL);
            AssetSearch.Invalidate();

            // 旧版のキャッシュ(.playable を知らない)が残っている状態を再現する。
            DependencyGraphService.MarkCacheOutdatedForTests();
            DependencyGraphService.ResetInMemoryCacheForTests();

            Assert.IsTrue(DependencyGraphService.FindUsages(AssetType.Se, 313131UL).Any(u => u.SourcePath == path),
                "古いキャッシュでも .playable だけは自動で補完されるはず(全体の再構築は要らない)");
        }
    }
}
