using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Inspector;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-8 / D-10: 新 MaterialEditor / TextureEditor(Editor/Material)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(MaterialSample = 「敵キャラ トゥーン」: 固有 27 項目・Passes / Keywords・常時アニメ)を一時フォルダに作って確かめる(実データは触らない)。
    public class MaterialEditorUxTests
    {
        private const string TmpFolder = "Assets/__MaterialEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type MatType => EditorAssembly.GetType("DDrive.Editor.Materials.MaterialEditor", true);
        private static Type TexType => EditorAssembly.GetType("DDrive.Editor.Materials.TextureEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private MaterialData _data;
        private TextureData _tex;
        private EditorWindow _window;
        private Type _windowType;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                try
                {
                    if (_windowType == MatType)
                    {
                        MatType.GetMethod("RemovePreview", Inst).Invoke(_window, null);
                    }
                }
                catch (Exception)
                {
                    // 後始末。失敗してもテストの結果にはしない。
                }

                if (_data != null)
                {
                    EditorUtility.ClearDirty(_data);
                }

                if (_tex != null)
                {
                    EditorUtility.ClearDirty(_tex);
                }

                UnityEngine.Object.DestroyImmediate(_window);
                _window = null;
            }

            if (AssetDatabase.IsValidFolder(TmpFolder))
            {
                AssetDatabase.DeleteAsset(TmpFolder);
            }
        }

        private object BuildSample()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Materials.MaterialSample", true);
            return sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
        }

        private void OpenWindow(Type type, UnityEngine.Object target)
        {
            _windowType = type;
            _window = (EditorWindow)ScriptableObject.CreateInstance(type);
            var baseType = type.BaseType;
            baseType.GetField("_target", Inst).SetValue(_window, target);
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                baseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }
        }

        private void BuildSampleAndMaterialWindow()
        {
            var result = BuildSample();
            _data = (MaterialData)result.GetType().GetField("Material").GetValue(result);
            OpenWindow(MatType, _data);
        }

        private object Field(string name) => _windowType.GetField(name, Inst).GetValue(_window);

        private object Call(string method, params object[] args) => _windowType.GetMethod(method, Inst).Invoke(_window, args);

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private static IEnumerable<string> GuideFields(string guideTypeName, out Dictionary<string, int> tiers)
        {
            var guideType = EditorAssembly.GetType(guideTypeName, true);
            var instance = guideType.GetField("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var list = new List<string>();
            tiers = new Dictionary<string, int>();
            foreach (var e in (IEnumerable)guideType.BaseType.GetProperty("Entries").GetValue(instance))
            {
                var field = (string)e.GetType().GetField("Field").GetValue(e);
                list.Add(field);
                tiers[field] = (int)e.GetType().GetField("Tier").GetValue(e);
            }

            return list;
        }

        // ── 欄の表 ──

        [Test]
        public void MaterialFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var inGuide = new HashSet<string>(GuideFields("DDrive.Editor.Materials.MaterialFieldGuide", out var tiers));
            var data = ScriptableObject.CreateInstance<MaterialData>();
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
                    // Common は MaterialCommon 構造体。1 項目 = 1 行にするため "Common.Xxx" で表に載っている(下で全項目を確かめる)。
                    if (it.name == "Common")
                    {
                        continue;
                    }

                    if (!readOnly.Contains(it.name) && !inGuide.Contains(it.name))
                    {
                        missing.Add(it.name);
                    }
                }

                var common = so.FindProperty("Common");
                var child = common.Copy();
                var end = common.GetEndProperty();
                var inner = true;
                while (child.NextVisible(inner) && !SerializedProperty.EqualContents(child, end))
                {
                    inner = false;
                    if (!inGuide.Contains("Common." + child.name))
                    {
                        missing.Add("Common." + child.name);
                    }
                }

                Assert.IsEmpty(missing, "MaterialFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が MaterialData にありません");
                    }
                }

                Assert.AreEqual(0, tiers["Shader"], "Shader は必須");
                Assert.AreNotEqual(0, tiers["Specific"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void TextureFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            var inGuide = new HashSet<string>(GuideFields("DDrive.Editor.Materials.TextureFieldGuide", out var tiers));
            var data = ScriptableObject.CreateInstance<TextureData>();
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

                Assert.IsEmpty(missing, "TextureFieldGuide に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が TextureData にありません");
                    }
                }

                Assert.AreEqual(0, tiers["Texture"], "Texture は必須");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // ── 分類(つまみの語彙) ──

        [Test]
        public void TuneSource_PrefixOf_TakesTheLeadingWord()
        {
            var t = EditorAssembly.GetType("DDrive.Editor.Materials.MaterialTuneSource", true);
            var m = t.GetMethod("PrefixOf", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.AreEqual("Toon", m.Invoke(null, new object[] { "_ToonShadeColor" }));
            Assert.AreEqual("Toon", m.Invoke(null, new object[] { "_Toon_Shade" }));
            Assert.AreEqual("Outline", m.Invoke(null, new object[] { "_OutlineWidth" }));
            Assert.AreEqual("Rim", m.Invoke(null, new object[] { "_RimPower" }));
            Assert.AreEqual("Shade", m.Invoke(null, new object[] { "_Shade1st" }));
            Assert.AreEqual("Noise", m.Invoke(null, new object[] { "_NoiseScale" }));
            Assert.AreEqual("A", m.Invoke(null, new object[] { "_A" }));
        }

        [Test]
        public void TuneSource_Categories_SplitCommonRenderStatePrefixesAndOthers()
        {
            var result = BuildSample();
            var data = (MaterialData)result.GetType().GetField("Material").GetValue(result);
            var t = EditorAssembly.GetType("DDrive.Editor.Materials.MaterialTuneSource", true);
            var src = t.GetConstructors()[0].Invoke(new object[] { data });
            t.GetMethod("Signature").Invoke(src, null);
            var names = new List<string>((IEnumerable<string>)t.GetProperty("CategoryNames").GetValue(src));
            CollectionAssert.AreEqual(new[] { "共通チャンネル", "描画ステート", "Toon", "Outline", "Rim", "Glow", "Wind", "その他" }, names);

            int Cat(string property)
            {
                for (var i = 0; i < data.Specific.Length; i++)
                {
                    if (data.Specific[i].Property == property)
                    {
                        return (int)t.GetMethod("InferCategory").Invoke(src, new object[] { i });
                    }
                }

                return -1;
            }

            Assert.AreEqual("共通チャンネル", names[Cat("_Smoothness")], "Common を上書きする紛れ込みは「共通チャンネル」");
            Assert.AreEqual("描画ステート", names[Cat("_Cull")]);
            Assert.AreEqual("Toon", names[Cat("_ToonRampMap")]);
            Assert.AreEqual("Outline", names[Cat("_OutlineWidth")]);
            Assert.AreEqual("その他", names[Cat("_NoiseScale")], "1 件しかない接頭辞は「その他」");
        }

        [Test]
        public void TuneSource_PlanAdd_OffersOnlyRealShaderProperties_NotYetRegistered()
        {
            var result = BuildSample();
            var data = (MaterialData)result.GetType().GetField("Material").GetValue(result);
            var t = EditorAssembly.GetType("DDrive.Editor.Materials.MaterialTuneSource", true);
            var src = t.GetConstructors()[0].Invoke(new object[] { data });
            var plan = t.GetMethod("PlanAdd").Invoke(src, new object[] { 0 }); // 色: 全部登録済み
            var message = (string)plan.GetType().GetField("Message").GetValue(plan);
            StringAssert.Contains("すべて追加済み", message);

            // 1 件外すと、その名前だけが候補に出る。
            var list = new List<ShaderParam>(data.Specific);
            var idx = list.FindIndex(p => p.Property == "_ToonShadeColor");
            list.RemoveAt(idx);
            data.Specific = list.ToArray();
            plan = t.GetMethod("PlanAdd").Invoke(src, new object[] { 0 });
            var cands = (IList)plan.GetType().GetField("Candidates").GetValue(plan);
            Assert.AreEqual(1, cands.Count);
            Assert.AreEqual("_ToonShadeColor", cands[0].GetType().GetField("Name").GetValue(cands[0]));
        }

        // ── 実運用規模のサンプルで開く ──

        [Test]
        public void Sample_BuildsHeroCardsAndFooter_WithoutPlayback()
        {
            BuildSampleAndMaterialWindow();
            var root = _window.rootVisualElement;
            Assert.AreEqual(27, _data.Specific.Length, "固有 20 項目以上");
            Assert.IsTrue(_data.DisabledPasses.Length > 0 && _data.EnabledKeywords.Length > 0, "Pass / Keyword あり");
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNull(root.Q(className: "ux-bar__speed"), "Material は再生が無いので速度を出さない");
            // 見た目 / 質感 / 固有 / 描画 / 動き / パス / 確認する / 管理情報 / その他の検証(カタログ未登録のサンプルなので出る)
            Assert.AreEqual(9, root.Query(className: "ux-card").ToList().Count, "カード数");
        }

        [Test]
        public void Sample_AllFoldoutsAreClosedByDefault()
        {
            BuildSampleAndMaterialWindow();
            var root = _window.rootVisualElement;
            MatType.GetMethod("CollapseFoldouts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
            var open = new List<string>();
            foreach (var f in root.Query<Foldout>().ToList())
            {
                if (f.value)
                {
                    open.Add(f.text);
                }
            }

            Assert.IsEmpty(open, "既定で開いている Foldout: " + string.Join(", ", open));
        }

        [Test]
        public void Sample_SpecificKnobs_FilterChipsWithCounts_AndListViewAbove20()
        {
            BuildSampleAndMaterialWindow();
            var root = _window.rootVisualElement;
            var knobs = (VisualElement)Field("_knobs");
            var chips = knobs.Query<Button>(className: "ux-filter").ToList();
            var texts = chips.ConvertAll(b => b.text);
            CollectionAssert.Contains(texts, "すべて 27");
            CollectionAssert.Contains(texts, "共通チャンネル 1");
            CollectionAssert.Contains(texts, "描画ステート 1");
            CollectionAssert.Contains(texts, "Toon 8");
            CollectionAssert.Contains(texts, "Outline 5");
            CollectionAssert.Contains(texts, "Rim 4");
            CollectionAssert.Contains(texts, "Glow 3");
            CollectionAssert.Contains(texts, "Wind 2");
            CollectionAssert.Contains(texts, "その他 3");
            Assert.AreEqual(1, knobs.Query<ListView>(className: "ux-itemlist__lv").ToList().Count, "20 行を超えたら ListView(仮想化)");

            // 分類で絞ると 20 行以下になり、行がそのまま並ぶ(ListView は外れる)。
            Click(chips.Find(b => b.text == "Outline 5"));
            Assert.AreEqual(0, knobs.Query<ListView>(className: "ux-itemlist__lv").ToList().Count, "5 行ならそのまま並べる");
            var shown = knobs.Query(className: "ux-knob").Where(k => !k.ClassListContains("ux-hidden")).ToList();
            Assert.AreEqual(5, shown.Count, "Outline の 5 行だけ");
            Assert.IsNotNull(root.Q(className: "ux-hero"));
        }

        [Test]
        public void Sample_SpecificKnob_OneRowChange_WritesData_AndUndoRestores()
        {
            BuildSampleAndMaterialWindow();
            var knobs = (VisualElement)Field("_knobs");
            Click(knobs.Query<Button>(className: "ux-filter").ToList().Find(b => b.text == "Outline 5"));
            VisualElement row = null;
            foreach (var k in knobs.Query(className: "ux-knob").ToList())
            {
                var name = k.Q<Label>(className: "ux-knob__name");
                if (name != null && name.text == "_OutlineWidth")
                {
                    row = k;
                }
            }

            Assert.IsNotNull(row, "_OutlineWidth の行");
            var slider = row.Q<Slider>();
            Assert.IsNotNull(slider, "Range のつまみはスライダー");
            Assert.AreEqual(0f, slider.lowValue, 1e-4f, "シェーダーの Range の下限");
            Assert.AreEqual(0.1f, slider.highValue, 1e-4f, "シェーダーの Range の上限");

            int Index() => Array.FindIndex(_data.Specific, p => p.Property == "_OutlineWidth");
            var before = _data.Specific[Index()].Value.FloatValue;
            Undo.IncrementCurrentGroup();
            slider.value = 0.07f;
            Assert.AreEqual(0.07f, _data.Specific[Index()].Value.FloatValue, 1e-4f, "つまみ 1 行の変更がデータに入る");
            Assert.IsTrue(row.ClassListContains("ux-knob--mod"), "変更の色バー");
            Undo.PerformUndo();
            Assert.AreEqual(before, _data.Specific[Index()].Value.FloatValue, 1e-4f, "Undo で戻る");
        }

        [Test]
        public void Sample_PassKeyword_ToggleAndAdd_WriteData_AndUndoRestores()
        {
            BuildSampleAndMaterialWindow();
            var view = (VisualElement)Field("_passKeyword");
            Assert.IsNotNull(view);
            var toggles = view.Query<Toggle>(className: "ux-pk__toggle").ToList();
            Assert.AreEqual(4, toggles.Count, "シェーダーの LightMode 4 つ(Forward / ShadowCaster / DepthOnly / SRPDefaultUnlit)");
            var shadow = toggles.Find(t => string.Equals(t.text, "ShadowCaster", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(shadow);
            Assert.IsTrue(shadow.value, "ShadowCaster は無効(チェック済み)");

            var depth = toggles.Find(t => t.text == "DepthOnly");
            Undo.IncrementCurrentGroup();
            depth.value = true;
            CollectionAssert.Contains(new List<string>(_data.DisabledPasses), "DepthOnly", "チェックで DisabledPasses に入る");
            Undo.PerformUndo();
            CollectionAssert.DoesNotContain(new List<string>(_data.DisabledPasses), "DepthOnly", "Undo で戻る");

            Undo.IncrementCurrentGroup();
            view.GetType().GetMethod("AddKeyword", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { "_TOON_RAMP" });
            CollectionAssert.Contains(new List<string>(_data.EnabledKeywords), "_TOON_RAMP");
            Undo.PerformUndo();
            CollectionAssert.DoesNotContain(new List<string>(_data.EnabledKeywords), "_TOON_RAMP");
        }

        [Test]
        public void Sample_SyncSpecific_AddsMissingProperties_AndReportsConflicts()
        {
            BuildSampleAndMaterialWindow();
            var list = new List<ShaderParam>(_data.Specific);
            list.RemoveAll(p => p.Property == "_RimPower");
            _data.Specific = list.ToArray();
            Call("SyncSpecificFromShader");
            Assert.IsTrue(Array.Exists(_data.Specific, p => p.Property == "_RimPower"), "シェーダーから固有を同期で足りない物が戻る");
            var label = (Label)Field("_specificLabel");
            StringAssert.Contains("_Smoothness, _Cull", label.text, "紛れ込み(_Smoothness / _Cull)の報告");
            var btn = (Button)Field("_removeConflictsButton");
            Assert.IsFalse(btn.ClassListContains("ux-hidden"), "紛れ込みがあるときだけ出る「共通チャンネルの重複を削除」");
            Call("RemoveSpecificConflicts");
            Assert.IsFalse(Array.Exists(_data.Specific, p => p.Property == "_Cull" || p.Property == "_Smoothness"));
            Assert.IsTrue(btn.ClassListContains("ux-hidden"));
        }

        [Test]
        public void Sample_Validation_AttachesToSpecificRow()
        {
            BuildSampleAndMaterialWindow();
            var root = _window.rootVisualElement;
            // Specific の紛れ込みの警告は「固有」カードの Specific の行の下に出る(その他の検証には出ない)。
            var issues = root.Query(className: "ux-issue").ToList();
            Assert.IsTrue(issues.Exists(i => i.Query<Label>().ToList().Exists(l => l.text.Contains("共通チャンネル名です"))), "Specific の警告がインラインに出る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void Sample_Thumbnail_Renders_AndScenePreviewPlacesAndRemoves()
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
            BuildSampleAndMaterialWindow();
            Call("RenderThumbnail");
            var image = (Image)Field("_thumbnailImage");
            Assert.IsNotNull(image.image, "サムネイルが描かれる(実 MaterialManager の共有 Material)");

            Call("PlacePreview");
            Assert.IsNotNull(Field("_primaryPreview"), "球がシーンに置かれる");
            Assert.IsNotNull(GameObject.Find((string)MatType.GetField("PreviewRootName", BindingFlags.Public | BindingFlags.Static).GetRawConstantValue()));
            Call("RemovePreview");
            Assert.IsNull(Field("_primaryPreview"));
        }

        [Test]
        public void Sample_ModelShape_NeedsAModelData_BeforePlacing()
        {
            BuildSampleAndMaterialWindow();
            MatType.GetField("_shape", Inst).SetValue(_window, DDrive.Editor.Materials.MaterialPreviewShape.Model);
            Call("PlacePreview");
            Assert.IsNull(Field("_primaryPreview"), "ModelData が無いと置かない");
            var status = (Label)Field("_sceneStatus");
            StringAssert.Contains("ModelData", status.text);
        }

        [Test]
        public void EmptyState_ShowsPickerForMaterialData()
        {
            _windowType = MatType;
            _window = (EditorWindow)ScriptableObject.CreateInstance(MatType);
            _window.Show();
            if (_window.rootVisualElement.childCount == 0)
            {
                MatType.BaseType.GetMethod("CreateGUI", Inst).Invoke(_window, null);
            }

            Assert.IsNotNull(_window.rootVisualElement.Q(className: "ux-empty"), "空状態");
        }

        // ── Texture Editor ──

        [Test]
        public void TextureEditor_BuildsImageCardAndInfoRow()
        {
            var result = BuildSample();
            _tex = (TextureData)result.GetType().GetField("Texture").GetValue(result);
            OpenWindow(TexType, _tex);
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"));
            // 画像 / 管理情報 / その他の検証(カタログ未登録)
            var titles = root.Query<Label>(className: "ux-card__title").ToList().ConvertAll(l => l.text);
            CollectionAssert.Contains(titles, "画像");
            CollectionAssert.Contains(titles, "管理情報");
            var infoImage = root.Q<Image>(className: "ux-tex-image");
            Assert.IsNotNull(infoImage);
            Assert.AreSame(_tex.Texture, infoImage.image, "画像のプレビュー");
            var notes = root.Query<Label>(className: "ux-note").ToList();
            Assert.IsTrue(notes.Exists(l => l.text.Contains("64×64")), "画像の大きさ");
            Assert.IsTrue(notes.Exists(l => l.text.Contains("Texture Type")), "Importer の現状");
        }

        // ── 登録 ──

        [Test]
        public void DataEditorRegistry_NewEditorsComeFirst_OldAreMarked()
        {
            var mat = DataEditorRegistry.GetEntries(typeof(MaterialData));
            Assert.AreSame(MatType, mat[0].WindowType, "MaterialData の第 1 候補は新 MaterialEditor");
            Assert.AreEqual("Material Editor で開く", mat[0].Label);
            var oldMat = new List<DataEditorRegistry.Entry>(mat).Find(e => e.WindowType == typeof(DDrive.Editor.Materials.MaterialEditorWindow));
            Assert.IsNotNull(oldMat);
            Assert.AreEqual("(旧) Material Editor で開く", oldMat.Label);
            Assert.AreEqual(100, oldMat.Order);

            var tex = DataEditorRegistry.GetEntries(typeof(TextureData));
            Assert.AreSame(TexType, tex[0].WindowType, "TextureData の第 1 候補は新 TextureEditor");
            Assert.AreEqual("(旧) Material Editor で開く", tex[tex.Count - 1].Label);
        }
    }
}
