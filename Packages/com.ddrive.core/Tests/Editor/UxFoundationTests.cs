using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 §2(UX-1): 新デザインの共通基盤(Editor/Ux)の規約と、題材の VfxEditor が共通基盤だけで組めていることの点検。
    // Ux の型は internal(InternalsVisibleTo を置かない方針)なので、型の中身はリフレクションで見る。
    public class UxFoundationTests
    {
        private static string PackageRoot =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDrive.Editor.Menu.DDriveMenu).Assembly).resolvedPath;

        private static string UxFolder => Path.Combine(PackageRoot, "Editor", "Ux");

        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;

        private static string StripLineComment(string line)
        {
            var i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line.Substring(0, i) : line;
        }

        // U-27 の機械点検に足した検査: Editor/Ux 配下の C# に style の代入(インラインスタイル)が無い。見た目は Theme/DDriveUx.uss に書く。
        [Test]
        public void UxSources_HaveNoInlineStyleAssignments()
        {
            var assign = new Regex(@"(?<![A-Za-z_])style\.[A-Za-z]+\s*=(?!=)");
            var initializer = new Regex(@"\bstyle\s*=\s*\{");
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(UxFolder, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var code = StripLineComment(lines[i]);
                    if (assign.IsMatch(code) || initializer.IsMatch(code))
                    {
                        offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + lines[i].Trim());
                    }
                }
            }

            Assert.IsEmpty(offenders, "Editor/Ux の C# にインラインスタイルがあります(USS のクラスで切り替える):\n" + string.Join("\n", offenders));
        }

        // C# が付ける USS クラス名(完全なリテラルだけ)が、テーマ(DDriveUx.uss)に定義されている(綴り違いで見た目が当たらない事故の検出)。
        [Test]
        public void UxClassNames_UsedInCode_AreDefinedInTheme()
        {
            var uss = File.ReadAllText(Path.Combine(UxFolder, "Theme", "DDriveUx.uss"));
            var call = new Regex("(?:AddToClassList|EnableInClassList|RemoveFromClassList)\\(\"(ux-[A-Za-z0-9_-]+)\"\\s*[,)]");
            var files = new List<string>(Directory.GetFiles(UxFolder, "*.cs", SearchOption.AllDirectories));
            files.AddRange(new[]
            {
                Path.Combine(PackageRoot, "Editor", "Vfx", "VfxEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Vfx", "VfxEditorRows.cs"),
                Path.Combine(PackageRoot, "Editor", "Audio", "SeEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Audio", "BgmEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Audio", "AudioEditorParts.cs"),
                Path.Combine(PackageRoot, "Editor", "Audio", "AudioWaveformView.cs"),
                Path.Combine(PackageRoot, "Editor", "Audio", "AudioListenerPad.cs"),
                Path.Combine(PackageRoot, "Editor", "Canvas", "CanvasEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Canvas", "CanvasEditor.ElementFx.cs"),
                Path.Combine(PackageRoot, "Editor", "Canvas", "CanvasEditor.Wires.cs"),
                Path.Combine(PackageRoot, "Editor", "Canvas", "CanvasEditor.Embedded.cs"),
                Path.Combine(PackageRoot, "Editor", "Canvas", "CanvasEditor.Navigation.cs"),
                Path.Combine(PackageRoot, "Editor", "Canvas", "CanvasSectionView.cs"),
                Path.Combine(PackageRoot, "Editor", "Presentation", "PresentationEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Presentation", "PresentationEditor.Tracks.cs"),
                Path.Combine(PackageRoot, "Editor", "Presentation", "PresentationEditor.Timeline.cs"),
                Path.Combine(PackageRoot, "Editor", "Presentation", "PresentationEditor.Preview.cs"),
                Path.Combine(PackageRoot, "Editor", "Presentation", "PresentationEditor.SceneAnchors.cs"),
                Path.Combine(PackageRoot, "Editor", "Presentation", "PresentationEditorRows.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditor.Scene.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditor.Events.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditor.Source.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditor.Body.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditor.Blend.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim", "AnimEditorRows.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim2D", "Anim2DEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim2D", "Anim2DEditor.Lists.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim2D", "Anim2DEditor.Preview.cs"),
                Path.Combine(PackageRoot, "Editor", "Anim2D", "Anim2DCreateWizard.cs"),
                Path.Combine(PackageRoot, "Editor", "Material", "MaterialEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Material", "MaterialEditor.Rows.cs"),
                Path.Combine(PackageRoot, "Editor", "Material", "MaterialEditor.Preview.cs"),
                Path.Combine(PackageRoot, "Editor", "Material", "TextureEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Model", "ModelEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Model", "ModelEditor.Slots.cs"),
                Path.Combine(PackageRoot, "Editor", "Model", "ModelEditor.Preview.cs"),
                Path.Combine(PackageRoot, "Editor", "Prefab", "PrefabEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Camera", "ShakeEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Camera", "HapticsEditor.cs"),
                Path.Combine(PackageRoot, "Editor", "Camera", "CameraFxGraphView.cs"),
                Path.Combine(PackageRoot, "Editor", "Camera", "CameraFxEditorParts.cs"),
            });
            // 見た目を持たない目印のクラス(USS に規則が無くてよい)。
            var markers = new HashSet<string> { "ux-root", "ux-dark", "ux-light", "ux-knobs", "ux-panel" };
            var missing = new SortedSet<string>();
            foreach (var file in files)
            {
                foreach (Match m in call.Matches(File.ReadAllText(file)))
                {
                    var name = m.Groups[1].Value;
                    if (markers.Contains(name))
                    {
                        continue;
                    }

                    if (!Regex.IsMatch(uss, "\\." + Regex.Escape(name) + "(?![A-Za-z0-9_-])"))
                    {
                        missing.Add(name + "  (" + Path.GetFileName(file) + ")");
                    }
                }
            }

            Assert.IsEmpty(missing, "USS に定義が無いクラス:\n" + string.Join("\n", missing));
        }

        private static IReadOnlyList<object> GuideEntries(out Type guideType)
        {
            guideType = EditorAssembly.GetType("DDrive.Editor.Vfx.VfxFieldGuide");
            Assert.IsNotNull(guideType, "VfxFieldGuide が見つかりません");
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var entries = (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance);
            var list = new List<object>();
            foreach (var e in entries)
            {
                list.Add(e);
            }

            return list;
        }

        private static string FieldOf(object entry) => (string)entry.GetType().GetField("Field").GetValue(entry);

        // 欄の表(FieldGuide)は VfxData の編集できる全フィールドを持つ(編集不可は Id / Version / Author / UpdatedAt と HideInInspector だけ)。
        [Test]
        public void VfxFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var entries = GuideEntries(out _);
            var inGuide = new HashSet<string>();
            foreach (var e in entries)
            {
                inGuide.Add(FieldOf(e));
            }

            var data = ScriptableObject.CreateInstance<VfxData>();
            try
            {
                var so = new SerializedObject(data);
                var readOnly = new HashSet<string> { "Id", "Version", "Author", "UpdatedAt", "m_Script" };
                var missing = new List<string>();
                var it = so.GetIterator();
                var enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (!readOnly.Contains(it.name) && !inGuide.Contains(it.name))
                    {
                        missing.Add(it.name);
                    }
                }

                Assert.IsEmpty(missing, "VfxFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が VfxData にありません");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // 管理情報カードの既定: DisplayName と Category だけが「よく使う」、残りは「詳細」。
        [Test]
        public void FieldGuide_AdminCard_ShowsOnlyDisplayNameAndCategoryByDefault()
        {
            var entries = GuideEntries(out var guideType);
            var adminName = (string)guideType.BaseType.GetField("AdminPurpose", BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
            var common = new List<string>();
            foreach (var e in entries)
            {
                var purpose = (string)e.GetType().GetField("Purpose").GetValue(e);
                var tier = Convert.ToInt32(e.GetType().GetField("Tier").GetValue(e));
                if (purpose == adminName && tier <= 1)
                {
                    common.Add(FieldOf(e));
                }
            }

            CollectionAssert.AreEquivalent(new[] { "DisplayName", "Category" }, common);
        }

        [Test]
        public void ValidationResult_Field_DefaultsToNull_AndWithFieldKeepsEverythingElse()
        {
            var plain = ValidationResult.Warning("x", null, "VFX-TEST");
            Assert.IsNull(plain.Field);

            var fixCalled = false;
            var bound = ValidationResult.Error("msg", () => fixCalled = true, "CODE").WithField("Prefab");
            Assert.AreEqual("Prefab", bound.Field);
            Assert.AreEqual(ValidationSeverity.Error, bound.Severity);
            Assert.AreEqual("msg", bound.Message);
            Assert.AreEqual("CODE", bound.Code);
            bound.FixAction();
            Assert.IsTrue(fixCalled);
            Assert.IsNull(ValidationResult.Error("msg").Field, "WithField は元の結果を変えない(値型のコピー)");
        }

        [Test]
        public void VfxDataValidator_PrefabMissing_IsBoundToThePrefabField()
        {
            var data = ScriptableObject.CreateInstance<VfxData>();
            try
            {
                var results = new List<ValidationResult>(new VfxDataValidator().Validate(data, new ValidationContext(new List<DDrive.Foundation.Data.AssetDataBase>())));
                var prefab = results.Find(r => r.Severity == ValidationSeverity.Error);
                Assert.AreEqual("Prefab", prefab.Field);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // 題材の VfxEditor が、共通基盤の部品だけで E と同じ画面(ヒーロー・目的別カード・固定バー)を組む。
        [Test]
        public void VfxEditor_BuildsHeroCardsAndFooter_FromTheCommonFoundation()
        {
            var type = EditorAssembly.GetType("DDrive.Editor.Vfx.VfxEditor");
            Assert.IsNotNull(type);
            var data = ScriptableObject.CreateInstance<VfxData>();
            data.name = "UxSmoke";
            EditorWindow window = null;
            try
            {
                window = (EditorWindow)ScriptableObject.CreateInstance(type);
                var baseType = type.BaseType;
                baseType.GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, data);
                baseType.GetMethod("CreateGUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                var root = window.rootVisualElement;
                Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
                Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
                Assert.IsNotNull(root.Q(className: "ux-seg"), "セグメント");
                // 目的別カード 5 + 複数同時再生 + 管理情報 + その他の検証(隠れている)= 8
                var cards = root.Query(className: "ux-card").ToList();
                Assert.AreEqual(8, cards.Count, "カード数(出す / 出る場所 / 長さ / 見た目 / 連携 / 複数同時再生 / 管理情報 / その他の検証)");
                Assert.IsTrue(root.ClassListContains("ux-root"));
                // 空カードの文言は USS クラスで切り替える(インラインスタイルではない)
                Assert.IsNotNull(root.Q(className: "ux-card__empty"));
            }
            finally
            {
                if (window != null)
                {
                    UnityEngine.Object.DestroyImmediate(window);
                }

                UnityEngine.Object.DestroyImmediate(data);
            }
        }
    }
}
