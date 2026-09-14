using System;
using System.Collections.Generic;
using DDrive.Editor.Anchor;
using DDrive.Editor.Anim;
using DDrive.Editor.Audio;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Inspector;
using DDrive.Editor.Model;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Model;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §8 — Data アセットの Inspector 最上部「エディターで開く」の対応表。
    // 専用エディタを持つ Data 種別が [DataEditor] を付け忘れていないことを機械的に検出する。
    public class DataEditorRegistryTests
    {
        // 専用エディタを持たない Data 種別はここに明示する(理由をコメントで残す)。
        // PresentationData: 専用エディタ(マルチトラック UI + 統合プレビュー)は 5-4(PresentationEditor)で
        // 実装予定([08_presentation.md] §4)。5-1 時点では Inspector から Tracks を直接編集する。
        // CameraShakeData / HapticsData: 専用エディタ(CameraFxEditorWindow = ShakeEditor / HapticsEditor)は
        // 5-2c で実装済み([16_camera_haptics.md] §C-2)。
        private static readonly HashSet<string> Exempt = new() { "PresentationData" };

        private sealed class DerivedVfxData : VfxData
        {
        }

        [Test]
        public void EveryConcreteDataType_HasEditor()
        {
            var missing = new List<string>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<AssetDataBase>())
            {
                var assembly = type.Assembly.GetName().Name;
                if (type.IsAbstract || !assembly.StartsWith("DDrive.", StringComparison.Ordinal) || assembly.StartsWith("DDrive.Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!DataEditorRegistry.HasEditor(type) && !Exempt.Contains(type.Name))
                {
                    missing.Add(type.Name);
                }
            }

            Assert.IsEmpty(missing, "専用エディタの EditorWindow に [DataEditor(typeof(XxxData), \"…で開く\")] を付けてください: " + string.Join(", ", missing));
        }

        [Test]
        public void KnownPairs_ResolveToTheirWindows()
        {
            AssertWindow(typeof(SeData), typeof(AudioEditorWindow));
            AssertWindow(typeof(BgmData), typeof(AudioEditorWindow));
            AssertWindow(typeof(VfxData), typeof(VfxEditorWindow));
            AssertWindow(typeof(ModelData), typeof(ModelEditorWindow));
            AssertWindow(typeof(AnimData), typeof(AnimEditorWindow));
            AssertWindow(typeof(AnchorData), typeof(AnchorEditorWindow));
            AssertWindow(typeof(AnchorGroupData), typeof(AnchorGroupEditorWindow));
            AssertWindow(typeof(CameraShakeData), typeof(CameraFxEditorWindow));
            AssertWindow(typeof(HapticsData), typeof(CameraFxEditorWindow));
        }

        [Test]
        public void DerivedDataType_InheritsBaseTypeEditor()
        {
            AssertWindow(typeof(DerivedVfxData), typeof(VfxEditorWindow));
        }

        [Test]
        public void Entries_HaveLabels()
        {
            foreach (var dataType in DataEditorRegistry.RegisteredDataTypes)
            {
                foreach (var entry in DataEditorRegistry.GetEntries(dataType))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Label), $"{entry.WindowType.Name} の [DataEditor] にラベルがありません");
                }
            }
        }

        private static void AssertWindow(Type dataType, Type windowType)
        {
            foreach (var entry in DataEditorRegistry.GetEntries(dataType))
            {
                if (entry.WindowType == windowType)
                {
                    return;
                }
            }

            Assert.Fail($"{dataType.Name} → {windowType.Name} が登録されていません");
        }
    }
}
