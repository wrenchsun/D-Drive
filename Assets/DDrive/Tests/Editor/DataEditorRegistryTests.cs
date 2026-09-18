using System;
using System.Collections.Generic;
using DDrive.Editor.Anchor;
using DDrive.Editor.Anim;
using DDrive.Editor.Audio;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Inspector;
using DDrive.Editor.Model;
using DDrive.Editor.Presentation;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Cutscene;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
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
        // 2026-09-14(5-4): PresentationEditorWindow を実装したため PresentationData を Exempt から外した。
        // 2026-09-18(6-10a): CutsceneData の編集 UI は Unity 標準の Timeline ウィンドウ([26_timeline.md] §3
        // 「編集 UI は Unity 標準の Timeline ウィンドウを使う。独自のタイムライン UI は作らない」)であり、
        // D-Drive 独自の EditorWindow は持たない。
        // 2026-09-18(6-10d): Inspector 導線(標準 Timeline ウィンドウを開く・確認用シーンを開く・
        // バインド検査・Play Mode 中の再生)は `CutsceneDataEditor`(`Editor/Cutscene/CutsceneDataEditor.cs`)
        // で実装したが、これは [DataEditor] 属性付きの EditorWindow ではないため本テストの対象表には乗らない。
        // Exempt は上記の理由により変更なし。
        private static readonly HashSet<string> Exempt = new() { nameof(CutsceneData) };

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
            AssertWindow(typeof(PresentationData), typeof(PresentationEditorWindow));
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
