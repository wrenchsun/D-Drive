using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DDrive.Editor.Inspector;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // docs/1010 UX-9 / UX-14 / D-10: 新 ModelEditor(Editor/Model)と PrefabEditor(Editor/Prefab)。Ux の型は internal なので、型の中身はリフレクションで見る。
    // 実運用規模のサンプル(ModelSample = 「ユニティちゃん」: スロット 24 行・BlendShape 付き・Default Animation あり / PrefabSample = 「爆発する樽」)を
    // 一時フォルダに作って確かめる(実データは触らない)。
    public class ModelEditorUxTests
    {
        private const string TmpFolder = "Assets/__ModelEditorUxTestTmp";
        private static Assembly EditorAssembly => typeof(DDrive.Editor.Menu.DDriveMenu).Assembly;
        private static Type ModelType => EditorAssembly.GetType("DDrive.Editor.Model.ModelEditor", true);
        private static Type PrefabType => EditorAssembly.GetType("DDrive.Editor.PrefabTool.PrefabEditor", true);
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private ModelData _model;
        private PrefabData _prefabData;
        private EditorWindow _window;
        private Type _windowType;

        [SetUp]
        public void SetUp() => UxTestWindows.CloseAll();

        [TearDown]
        public void TearDown()
        {
            UxTestWindows.CloseAll();
            _window = null;
            _model = null;
            _prefabData = null;
            UxTestWindows.DeleteFolder(TmpFolder);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => UxTestWindows.Release();

        private object BuildModelSample()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.Model.ModelSample", true);
            return sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
        }

        private object BuildPrefabSample()
        {
            var sample = EditorAssembly.GetType("DDrive.Editor.PrefabTool.PrefabSample", true);
            return sample.GetMethod("Build", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { TmpFolder });
        }

        private void OpenWindow(Type type, UnityEngine.Object target)
        {
            RequiresGraphicsGuard.SkipIfNoGraphicsDevice();
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

        private object BuildSampleAndModelWindow()
        {
            var result = BuildModelSample();
            _model = (ModelData)result.GetType().GetField("Model").GetValue(result);
            OpenWindow(ModelType, _model);
            return result;
        }

        private object Field(string name) => _windowType.GetField(name, Inst).GetValue(_window);

        private object Call(string method, params object[] args) => _windowType.GetMethod(method, Inst).Invoke(_window, args);

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

        private static void AssertGuideCoversEveryField<T>(string guideTypeName, string requiredField) where T : ScriptableObject
        {
            var inGuide = new HashSet<string>(GuideFields(guideTypeName, out var tiers));
            var data = ScriptableObject.CreateInstance<T>();
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

                Assert.IsEmpty(missing, guideTypeName + " に無い欄: " + string.Join(", ", missing));
                foreach (var field in inGuide)
                {
                    if (!field.StartsWith("(", StringComparison.Ordinal))
                    {
                        Assert.IsNotNull(so.FindProperty(field), "表の欄 " + field + " が " + typeof(T).Name + " にありません");
                    }
                }

                Assert.AreEqual(0, tiers[requiredField], requiredField + " は必須");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        // ── 欄の表 ──

        [Test]
        public void ModelFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            AssertGuideCoversEveryField<ModelData>("DDrive.Editor.Model.ModelFieldGuide", "Prefab");
        }

        [Test]
        public void PrefabFieldGuide_CoversEveryEditableField_AndOnlyExistingFields()
        {
            AssertGuideCoversEveryField<PrefabData>("DDrive.Editor.PrefabTool.PrefabFieldGuide", "Prefab");
        }

        // ── 登録表([DataEditor]) ──

        [Test]
        public void Registry_NewEditorsFirst_OldOnesLabelledAndLast()
        {
            foreach (var (dataType, newType, oldName) in new[]
            {
                (typeof(ModelData), ModelType, "ModelEditorWindow"),
                (typeof(PrefabData), PrefabType, "PrefabEditorWindow"),
            })
            {
                var entries = DataEditorRegistry.GetEntries(dataType);
                Assert.AreEqual(newType, entries[0].WindowType, dataType.Name + " の先頭は新エディター");
                Assert.AreEqual(0, entries[0].Order);
                var old = entries[entries.Count - 1];
                Assert.AreEqual(oldName, old.WindowType.Name);
                Assert.AreEqual(100, old.Order);
                StringAssert.StartsWith("(旧)", old.Label);
            }
        }

        // ── Model: 実運用規模のサンプルで開く ──

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_BuildsHeroCardsAndFooter_WithPlayback()
        {
            var result = BuildSampleAndModelWindow();
            var root = _window.rootVisualElement;
            Assert.GreaterOrEqual(_model.Slots.Length, 8, "スロット 8 以上");
            Assert.IsTrue(_model.DefaultAnimation.IsValid, "Default Animation あり");
            Assert.IsNotNull(_model.Avatar, "Avatar あり");
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNotNull(root.Q(className: "ux-bar"), "固定バー");
            Assert.IsNotNull(root.Q(className: "ux-bar__speed"), "Model は ▶(DefaultAnimation)があるので速度を出す");
            // 見た目 / スロット / アニメ / 描画 / 確認する / 管理情報 / その他の検証(カタログ未登録のサンプルなので出る)
            Assert.AreEqual(7, root.Query(className: "ux-card").ToList().Count, "カード数");
            Assert.AreEqual(24, (int)result.GetType().GetField("SlotCount").GetValue(result));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_AllFoldoutsAreClosedByDefault()
        {
            BuildSampleAndModelWindow();
            var root = _window.rootVisualElement;
            ModelType.GetMethod("CollapseFoldouts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
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
        [Category("RequiresGraphics")]
        public void ModelSample_SlotList_IsOneRowPerSlot_WithCountsChipsAndListView()
        {
            BuildSampleAndModelWindow();
            var root = _window.rootVisualElement;
            var list = root.Q(className: "ux-itemlist");
            Assert.IsNotNull(list, "スロットの一覧");
            var count = list.Q<Label>(className: "ux-itemlist__count");
            Assert.AreEqual("24 スロット中 割当済み 21", count.text);
            var chips = list.Query<Button>(className: "ux-filter").ToList().ConvertAll(b => b.text);
            CollectionAssert.AreEqual(new[] { "すべて", "割当済み", "未割当", "Skinned", "Mesh", "ほか", "見つからない" }, chips);
            Assert.IsNotNull(list.Q<ListView>(), "24 行は 20 を超えるので ListView");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_SlotRows_FlagUnassignedAndMissingRenderer()
        {
            BuildSampleAndModelWindow();
            var list = _window.rootVisualElement.Q(className: "ux-itemlist");
            // 「未割当」で絞ると 3 行、「見つからない」で絞ると 1 行。
            var buttons = list.Query<Button>(className: "ux-filter").ToList();
            Click(buttons.Find(b => b.text == "未割当"));
            StringAssert.Contains("(表示 3)", list.Q<Label>(className: "ux-itemlist__count").text);
            Click(buttons.Find(b => b.text == "すべて"));
            Click(buttons.Find(b => b.text == "見つからない"));
            StringAssert.Contains("(表示 1)", list.Q<Label>(className: "ux-itemlist__count").text);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_SetSlotMaterial_WritesOneSlot_AndUndoRestores()
        {
            var result = BuildSampleAndModelWindow();
            var materials = (MaterialData[])result.GetType().GetField("Materials").GetValue(result);
            var before = _model.Slots[5].Material;
            Assert.IsFalse(before.IsValid, "サンプルのスロット 5 は未割当");
            var slotCountBefore = _model.Slots.Length;

            Undo.IncrementCurrentGroup();
            Call("SetSlotMaterial", 5, materials[3]);
            Assert.AreEqual(materials[3].Id, _model.Slots[5].Material.Value);
            Assert.AreEqual(AssetType.Material, _model.Slots[5].Material.Type);
            Assert.AreEqual(slotCountBefore, _model.Slots.Length);

            Undo.PerformUndo();
            Assert.IsFalse(_model.Slots[5].Material.IsValid, "Undo で未割当に戻る");

            // null = 未割当に戻す
            Call("SetSlotMaterial", 0, null);
            Assert.IsFalse(_model.Slots[0].Material.IsValid);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_OpenSlotMaterial_OpensNewMaterialEditor()
        {
            BuildSampleAndModelWindow();
            Call("RebuildSlots"); // Material の表を作る
            Call("OpenSlotMaterial", 0);
            var matType = EditorAssembly.GetType("DDrive.Editor.Materials.MaterialEditor", true);
            var windows = Resources.FindObjectsOfTypeAll(matType);
            Assert.AreEqual(1, windows.Length, "新 MaterialEditor が開く");
            var target = matType.BaseType.GetField("_target", Inst).GetValue(windows[0]) as MaterialData;
            Assert.IsNotNull(target);
            Assert.AreEqual(_model.Slots[0].Material.Value, target.Id, "スロット 0 の Material が対象");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_OpenDefaultAnimation_OpensNewAnimEditor()
        {
            BuildSampleAndModelWindow();
            Call("OpenDefaultAnimation");
            var animType = EditorAssembly.GetType("DDrive.Editor.Anim.AnimEditor", true);
            Assert.AreEqual(1, Resources.FindObjectsOfTypeAll(animType).Length, "新 AnimEditor が開く");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_InfoAndAvatarTexts_DescribePrefabAndHumanoid()
        {
            BuildSampleAndModelWindow();
            var info = (string)Call("InfoText");
            StringAssert.Contains("Renderer", info);
            StringAssert.Contains("BlendShape", info);
            StringAssert.Contains("IModelInstanceListener", info);
            var avatar = (string)Call("AvatarText");
            StringAssert.Contains("Humanoid", avatar);
            StringAssert.Contains("使えます", avatar);
            Assert.IsFalse((bool)Call("CanCopyAnimatorAvatar"), "Data に Humanoid の Avatar があるので入れ直しは不要");

            _model.Avatar = null;
            var after = (string)Call("AvatarText");
            StringAssert.Contains("使えません", after);
            Assert.IsTrue((bool)Call("CanCopyAnimatorAvatar"), "Animator 側の Avatar を入れられる");
            Call("CopyAnimatorAvatar");
            Assert.IsNotNull(_model.Avatar, "Animator の Avatar が入る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_PlaceParallelAndRemove_UsesRealModelsManager()
        {
            var result = BuildSampleAndModelWindow();
            var alt = (ModelData)result.GetType().GetField("Alt").GetValue(result);
            Call("PlaceMain", false);
            var scene = Field("_scene");
            Assert.IsTrue((bool)scene.GetType().GetProperty("OwnsCurrent").GetValue(scene), "対象が配置される");

            var parallelModels = (ModelData[])Field("_parallelModels");
            parallelModels[0] = alt;
            var field = new UnityEditor.UIElements.ObjectField { objectType = typeof(ModelData), value = alt };
            Call("ToggleParallel", 0, field);
            Assert.IsTrue((bool)Call("IsParallelPlaced", 0), "1 体目が並ぶ");
            Call("ToggleParallel", 0, field);
            Assert.IsFalse((bool)Call("IsParallelPlaced", 0), "もう一度で撤去");

            Call("ToggleParallel", 1, field);
            Call("RemoveMain");
            Assert.IsFalse((bool)scene.GetType().GetProperty("OwnsCurrent").GetValue(scene), "撤去で対象も消える");
            Assert.IsFalse((bool)Call("IsParallelPlaced", 1), "並べた分も消える");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_PlayWithoutDefaultAnimation_SaysSo_AndDoesNotRepeat()
        {
            BuildSampleAndModelWindow();
            var so = new SerializedObject(_model);
            so.FindProperty("DefaultAnimation").FindPropertyRelative("value").ulongValue = 0UL;
            so.ApplyModifiedProperties();
            Call("PlayDefaultAnimation", false);
            StringAssert.Contains("未設定", (string)Field("_note"));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_PlayWithDefaultAnimation_PlacesTheModel()
        {
            BuildSampleAndModelWindow();
            Call("PlayDefaultAnimation", false);
            var scene = Field("_scene");
            Assert.IsTrue((bool)scene.GetType().GetProperty("OwnsCurrent").GetValue(scene), "▶ で配置される");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelSample_ValidationShowsOnTheSlotsRow_AndInTheChip()
        {
            BuildSampleAndModelWindow();
            var root = _window.rootVisualElement;
            // Slots[23] の RendererPath が Prefab に無い(Error)と、未割当 3 つ(Warning)が Slots の行の下に出る。
            var issues = root.Query(className: "ux-issues").ToList();
            Assert.IsTrue(issues.Exists(i => i.childCount > 0), "インラインの検証結果が出る");
        }

        [Test]
        [Category("RequiresGraphics")]
        public void ModelEditor_EmptyState_DoesNotThrow()
        {
            OpenWindow(ModelType, null);
            Assert.IsNotNull(_window.rootVisualElement.Q(className: "ux-bar") ?? _window.rootVisualElement);
            Assert.DoesNotThrow(() => Call("RemoveMain"));
        }

        [Test]
        public void OldModelEditorWindow_StillOpens_WithArchiveTitle()
        {
            var result = BuildModelSample();
            _model = (ModelData)result.GetType().GetField("Model").GetValue(result);
            DDrive.Editor.Model.ModelEditorWindow.Open(_model);
            var old = EditorWindow.GetWindow<DDrive.Editor.Model.ModelEditorWindow>();
            try
            {
                StringAssert.StartsWith("(旧)", old.titleContent.text);
            }
            finally
            {
                old.Close();
            }
        }

        // ── Prefab ──

        private void BuildSampleAndPrefabWindow()
        {
            var result = BuildPrefabSample();
            _prefabData = (PrefabData)result.GetType().GetField("Prefab").GetValue(result);
            OpenWindow(PrefabType, _prefabData);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void PrefabSample_BuildsHeroCardsAndFooter_WithoutPlayback()
        {
            BuildSampleAndPrefabWindow();
            var root = _window.rootVisualElement;
            Assert.IsNotNull(root.Q(className: "ux-hero"), "ヒーロー");
            Assert.IsNull(root.Q(className: "ux-bar__speed"), "Prefab は再生が無いので速度を出さない");
            // 見た目 / ゲームでの扱い / 確認する / 管理情報 / その他の検証(カタログ未登録のサンプルなので出る)
            Assert.AreEqual(5, root.Query(className: "ux-card").ToList().Count, "カード数");
            Assert.AreEqual(4, _prefabData.GameplayTags.Length);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void PrefabSample_AllFoldoutsAreClosedByDefault()
        {
            BuildSampleAndPrefabWindow();
            var root = _window.rootVisualElement;
            PrefabType.GetMethod("CollapseFoldouts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
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
        [Category("RequiresGraphics")]
        public void PrefabSample_PlaceAndRemovePreview_UsesRealPrefabsManager()
        {
            BuildSampleAndPrefabWindow();
            Call("PlacePreview", DDrive.Editor.Preview.PreviewPlaceMode.CurrentScene);
            var root = Field("_previewRoot") as GameObject;
            Assert.IsNotNull(root, "プレビューのルートができる");
            var manager = Field("_manager");
            var spawned = manager.GetType().GetMethod("GetGameObject").Invoke(manager, new[] { Field("_previewHandle") }) as GameObject;
            Assert.IsNotNull(spawned, "実 PrefabsManager が Spawn した実体がある");
            StringAssert.Contains("配置中", (string)Call("StatusText"));
            Call("RemovePreview");
            Assert.IsNull(Field("_previewRoot") as GameObject);
        }

        [Test]
        [Category("RequiresGraphics")]
        public void PrefabSample_ValidationWarnsDuplicateTag_InlineOnTheTagsRow()
        {
            BuildSampleAndPrefabWindow();
            var root = _window.rootVisualElement;
            var issueTexts = new List<string>();
            foreach (var box in root.Query(className: "ux-issues").ToList())
            {
                foreach (var l in box.Query<Label>().ToList())
                {
                    issueTexts.Add(l.text);
                }
            }

            Assert.IsTrue(issueTexts.Exists(t => t.Contains("重複")), "重複タグの警告がインラインで出る: " + string.Join(" | ", issueTexts));
        }

        [Test]
        [Category("RequiresGraphics")]
        public void PrefabEditor_EmptyState_DoesNotThrow()
        {
            OpenWindow(PrefabType, null);
            Assert.DoesNotThrow(() => Call("RemovePreview"));
        }

        [Test]
        public void OldPrefabEditorWindow_StillOpens_WithArchiveTitle()
        {
            var result = BuildPrefabSample();
            _prefabData = (PrefabData)result.GetType().GetField("Prefab").GetValue(result);
            DDrive.Editor.PrefabTool.PrefabEditorWindow.Open(_prefabData);
            var old = EditorWindow.GetWindow<DDrive.Editor.PrefabTool.PrefabEditorWindow>();
            try
            {
                StringAssert.StartsWith("(旧)", old.titleContent.text);
            }
            finally
            {
                old.Close();
            }
        }

        private static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }
    }
}
