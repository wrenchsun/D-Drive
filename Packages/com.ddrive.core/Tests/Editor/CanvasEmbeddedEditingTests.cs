using System.Collections.Generic;
using DDrive.Editor.CanvasTool;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Tests.Editor
{
    // [07_canvas_prefab.md] A-4 追記(2026-10-03、Canvas の埋め込み) — パス変換・Validator・埋め込み候補の検出と登録・
    // 自動収集の除外・グループ構築・選択からの持ち主の解決(UI を持たない純ロジック)。
    // 入れ子 Prefab は一時フォルダ(Assets/Tests/DDriveTemp/EmbeddedCanvas)に作って片付ける(実 GameData には触れない)。
    // 2026-10-03(レビュー PC-R-18): Runtime の EmbeddedCanvasPaths(internal)と Editor の EmbeddedPaths(internal 複製)は
    // どちらもテスト asmdef から直接は見えない(InternalsVisibleTo を置かない方針)ので、リフレクションで同じ表を両方に当てて
    // 一致を固定する。
    public class EmbeddedCanvasPathsTests
    {
        private static System.Reflection.MethodInfo RuntimeMethod(string name)
            => typeof(DDrive.Runtime.Ui.UiManager).Assembly.GetType("DDrive.Runtime.Ui.EmbeddedCanvasPaths").GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        private static System.Reflection.MethodInfo EditorMethod(string name)
            => typeof(CanvasEmbeddedEditing).Assembly.GetType("DDrive.Editor.CanvasTool.EmbeddedPaths").GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        private static string Combine(System.Reflection.MethodInfo m, string a, string b) => (string)m.Invoke(null, new object[] { a, b });

        private static (bool ok, string child) TryToChild(System.Reflection.MethodInfo m, string root, string parent)
        {
            var args = new object[] { root, parent, null };
            var ok = (bool)m.Invoke(null, args);
            return (ok, (string)args[2]);
        }

        [Test]
        public void RuntimeAndEditorCopies_ExistAndAreNotPublic()
        {
            var runtimeType = typeof(DDrive.Runtime.Ui.UiManager).Assembly.GetType("DDrive.Runtime.Ui.EmbeddedCanvasPaths");
            var editorType = typeof(CanvasEmbeddedEditing).Assembly.GetType("DDrive.Editor.CanvasTool.EmbeddedPaths");
            Assert.IsNotNull(runtimeType);
            Assert.IsNotNull(editorType);
            Assert.IsFalse(runtimeType.IsPublic, "Runtime の公開 API に残さない");
            Assert.IsFalse(editorType.IsPublic);
        }

        [TestCase("A", "B", "A/B")]
        [TestCase("", "B", "B")]
        [TestCase("A", "", "A")]
        [TestCase("A", null, "A")]
        [TestCase(null, null, "")]
        [TestCase("A/B", "C/D", "A/B/C/D")]
        public void Combine_JoinsWithSlash_AndKeepsEmptySides_InBothCopies(string a, string b, string expected)
        {
            Assert.AreEqual(expected, Combine(RuntimeMethod("Combine"), a, b));
            Assert.AreEqual(expected, Combine(EditorMethod("Combine"), a, b));
        }

        [TestCase("Option", "Option/Panel/Btn", true, "Panel/Btn")]
        [TestCase("Option", "Option", true, "")] // 埋め込みルート自身
        [TestCase("Option", "Option2/Panel", false, "")] // 前方一致の誤判定をしない
        [TestCase("Option", "Other/Panel", false, "")]
        [TestCase("", "Panel", false, "")]
        [TestCase("A/B", "A", false, "")]
        [TestCase("A/B", "A/B/C", true, "C")]
        [TestCase("A/B", "A/BC", false, "")]
        [TestCase("A", null, false, "")]
        public void TryToChildPath_ConvertsParentPathToChildPath_InBothCopies(string root, string parent, bool ok, string child)
        {
            var r = TryToChild(RuntimeMethod("TryToChildPath"), root, parent);
            var e = TryToChild(EditorMethod("TryToChildPath"), root, parent);
            Assert.AreEqual((ok, child), r);
            Assert.AreEqual(r, e, "Runtime と Editor の複製が一致している");
        }
    }

    public class CanvasEmbeddedEditingTests
    {
        // 入力中の判定: Unity 6 ではフォーカス中の要素は欄そのもの(TextField / 数値欄 / 検索欄)。内側の入力部品でも、
        // その中の文字の要素でも「入力中」と判定する。ボタンやラベルは入力欄ではない。
        [Test]
        public void IsTextInputElement_TextAndSearchFields_AreInput_ButtonsAreNot()
        {
            var text = new UnityEngine.UIElements.TextField();
            var number = new UnityEngine.UIElements.IntegerField();
            var search = new UnityEditor.UIElements.ToolbarSearchField();
            Assert.IsTrue(CanvasEmbeddedEditing.IsTextInputElement(text), "TextField そのもの");
            Assert.IsTrue(CanvasEmbeddedEditing.IsTextInputElement(number), "数値欄そのもの");
            Assert.IsTrue(CanvasEmbeddedEditing.IsTextInputElement(search), "検索欄そのもの");

            var inner = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(text, className: "unity-base-text-field__input");
            Assert.IsNotNull(inner);
            Assert.IsTrue(CanvasEmbeddedEditing.IsTextInputElement(inner), "内側の入力部品");
            var innerText = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.TextElement>(search);
            Assert.IsNotNull(innerText);
            Assert.IsTrue(CanvasEmbeddedEditing.IsTextInputElement(innerText), "検索欄の中の文字の要素");

            Assert.IsFalse(CanvasEmbeddedEditing.IsTextInputElement(new UnityEngine.UIElements.Button()), "ボタンは入力欄ではない");
            Assert.IsFalse(CanvasEmbeddedEditing.IsTextInputElement(new UnityEngine.UIElements.Label("x")));
            Assert.IsFalse(CanvasEmbeddedEditing.IsTextInputElement(new UnityEngine.UIElements.Toggle("x")));
            Assert.IsFalse(CanvasEmbeddedEditing.IsTextInputElement(null));
        }
        private const string TempName = "EmbeddedCanvas";
        private string _folder;
        private readonly List<Object> _objects = new();

        [SetUp]
        public void SetUp() => _folder = TestTempFolder.CreateFolder(TempName);

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _objects.Clear();
            AssetDatabase.DeleteAsset(_folder);
        }

        // ── フィクスチャ ──

        private static GameObject NewRect(string name, Transform parent = null, params System.Type[] components)
        {
            var types = new System.Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            System.Array.Copy(components, 0, types, 1, components.Length);
            var go = new GameObject(name, types);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        // 子 Prefab(ルート OptionPrefab / Panel(Image) / BtnX(Image))を保存して返す。
        private GameObject SaveChildPrefab(string name)
        {
            var root = NewRect(name);
            NewRect("Panel", root.transform, typeof(Image));
            NewRect("BtnX", root.transform, typeof(Image));
            var asset = PrefabUtility.SaveAsPrefabAsset(root, $"{_folder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return asset;
        }

        // 親 Prefab(Title(Image) と、入れ子の子 Prefab を rootName で配置)を保存して返す。
        private GameObject SaveParentPrefab(string name, GameObject childAsset, string childRootName)
        {
            var root = NewRect(name);
            NewRect("Title", root.transform, typeof(Image));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(childAsset, root.transform);
            instance.name = childRootName;
            var asset = PrefabUtility.SaveAsPrefabAsset(root, $"{_folder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return asset;
        }

        private CanvasData Data(ulong id, string name, GameObject prefab = null)
        {
            var data = ScriptableObject.CreateInstance<CanvasData>();
            data.Id = id;
            data.name = name;
            data.DisplayName = name;
            data.Prefab = prefab;
            _objects.Add(data);
            return data;
        }

        private static AssetId<CanvasMarker> IdOf(CanvasData d) => new(d.Id, AssetType.Canvas);

        // ── ボタンの配線(CanvasButtonWireEditing。Canvas Editor の「ボタンの配線」欄のロジック) ──

        private static ButtonWire Wire(string path, WireTrigger trigger, UiAction action, string key = null)
            => new() { ButtonPath = path, Trigger = trigger, Action = action, SignalKey = key };

        // 子(ルート直下に BtnX〔UiButton〕)と、親(ParentBtn〔UiButton〕+ 入れ子の子を OptionRoot で配置)を作る。
        private (CanvasData parent, CanvasData child) WiredParentAndChild()
        {
            var childRoot = NewRect("WireChild");
            NewRect("BtnX", childRoot.transform, typeof(Image), typeof(UiButton));
            var childPrefab = PrefabUtility.SaveAsPrefabAsset(childRoot, $"{_folder}/WireChild.prefab");
            Object.DestroyImmediate(childRoot);

            var parentRoot = NewRect("WireParent");
            NewRect("ParentBtn", parentRoot.transform, typeof(Image), typeof(UiButton));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(childPrefab, parentRoot.transform);
            instance.name = "OptionRoot";
            var parentPrefab = PrefabUtility.SaveAsPrefabAsset(parentRoot, $"{_folder}/WireParent.prefab");
            Object.DestroyImmediate(parentRoot);

            var child = Data(9101, "WireChildData", childPrefab);
            child.Buttons = new[] { Wire("BtnX", WireTrigger.Click, UiAction.SendSignal, "child/click"), Wire("BtnX", WireTrigger.LongPress, UiAction.SendSignal, "child/long") };
            var parent = Data(9102, "WireParentData", parentPrefab);
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            return (parent, child);
        }

        [Test]
        public void ButtonWires_BuildGroups_SplitsParentButtons_ParentOverrides_AndChildWires()
        {
            var (parent, child) = WiredParentAndChild();
            parent.Buttons = new[]
            {
                Wire("ParentBtn", WireTrigger.Click, UiAction.CloseSelf),
                Wire("OptionRoot/BtnX", WireTrigger.Click, UiAction.SendSignal, "parent/click"),
                Wire("Ghost", WireTrigger.Click, UiAction.None),
            };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child });

            var groups = CanvasButtonWireEditing.BuildGroups(parent, lookup);

            Assert.AreEqual(2, groups.ParentRows.Count, "親の行 = 親自身の UiButton + Prefab に無いパスを指す配線。埋め込みの配下のボタンは親の行にしない");
            Assert.AreEqual("ParentBtn", groups.ParentRows[0].ButtonPath);
            Assert.IsTrue(groups.ParentRows[0].InPrefab);
            CollectionAssert.AreEqual(new[] { 0 }, groups.ParentRows[0].WireIndices);
            Assert.AreEqual("Ghost", groups.ParentRows[1].ButtonPath);
            Assert.IsFalse(groups.ParentRows[1].InPrefab, "Prefab に無いパスの配線は、見つからない旨を出せるよう印を付ける");

            Assert.AreEqual(1, groups.Embeds.Count);
            var embed = groups.Embeds[0];
            Assert.AreSame(child, embed.Child);
            Assert.AreEqual(1, embed.OverrideRows.Count, "埋め込みの配下を指す親の配線は「親での上書き」");
            Assert.AreEqual("OptionRoot/BtnX", embed.OverrideRows[0].ButtonPath);
            CollectionAssert.AreEqual(new[] { 1 }, embed.OverrideRows[0].WireIndices);

            Assert.AreEqual(2, embed.ChildWires.Count);
            Assert.AreEqual(WireTrigger.Click, embed.ChildWires[0].Trigger);
            Assert.IsTrue(embed.ChildWires[0].OverriddenByParent, "親に同じ要素 + 同じトリガーの配線がある");
            Assert.AreEqual(WireTrigger.LongPress, embed.ChildWires[1].Trigger);
            Assert.IsFalse(embed.ChildWires[1].OverriddenByParent, "別のトリガーの配線は子の設定が使われる");
            StringAssert.Contains("child/long", embed.ChildWires[1].Summary);
        }

        [Test]
        public void ButtonWires_NoEmbeds_ListsEveryUiButton_EvenWithoutWires()
        {
            var (_, child) = WiredParentAndChild();
            child.Buttons = null;

            var groups = CanvasButtonWireEditing.BuildGroups(child, CanvasEmbeddedEditing.CanvasLookup.From(new[] { child }));

            Assert.AreEqual(0, groups.Embeds.Count);
            Assert.AreEqual(1, groups.ParentRows.Count);
            Assert.AreEqual("BtnX", groups.ParentRows[0].ButtonPath);
            Assert.AreEqual(0, groups.ParentRows[0].WireIndices.Count, "配線の無いボタンも一覧に出る(ここから足せる)");
            Assert.AreEqual(0, CanvasButtonWireEditing.BuildGroups(null, null).ParentRows.Count, "null でも例外にしない");
        }

        [Test]
        public void ButtonWires_AddAndRemove_UseFreeTrigger_AndKeepOtherRows()
        {
            var (_, child) = WiredParentAndChild();
            Assert.AreEqual(WireTrigger.DoubleClick, CanvasButtonWireEditing.FirstFreeTrigger(child, "BtnX"), "Click と LongPress は使用済み");

            var added = CanvasButtonWireEditing.AddWire(child, "BtnX");
            Assert.AreEqual(2, added);
            Assert.AreEqual(3, child.Buttons.Length);
            Assert.AreEqual("BtnX", child.Buttons[2].ButtonPath);
            Assert.AreEqual(WireTrigger.DoubleClick, child.Buttons[2].Trigger);
            Assert.AreEqual(UiAction.None, child.Buttons[2].Action);

            Assert.IsTrue(CanvasButtonWireEditing.RemoveWire(child, 0));
            Assert.AreEqual(2, child.Buttons.Length);
            Assert.AreEqual(WireTrigger.LongPress, child.Buttons[0].Trigger, "残りの行は順序も中身も変わらない");
            Assert.AreEqual("child/long", child.Buttons[0].SignalKey);
            Assert.IsFalse(CanvasButtonWireEditing.RemoveWire(child, 99), "範囲外は何もしない");
            Assert.AreEqual(-1, CanvasButtonWireEditing.AddWire(null, "BtnX"));

            var empty = Data(9103, "WireEmpty");
            Assert.AreEqual(0, CanvasButtonWireEditing.AddWire(empty, "A"), "Buttons が null でも足せる");
            Assert.AreEqual(WireTrigger.Click, empty.Buttons[0].Trigger);
        }

        [Test]
        public void ButtonWires_DescribeProblem_AndSummary()
        {
            var target = Data(9104, "WireTarget");
            var data = Data(9105, "WireProblems");
            data.Buttons = new[]
            {
                Wire("A", WireTrigger.Click, UiAction.OpenCanvas),
                Wire("A", WireTrigger.LongPress, UiAction.SendSignal),
                Wire("A", WireTrigger.Click, UiAction.CloseSelf),
                Wire("B", WireTrigger.Click, UiAction.SetOption),
                Wire("B", WireTrigger.Repeat, UiAction.SendSignal, "b/repeat"),
            };
            StringAssert.Contains("未設定", CanvasButtonWireEditing.DescribeProblem(data, 0));
            StringAssert.Contains("キー", CanvasButtonWireEditing.DescribeProblem(data, 1));
            StringAssert.Contains("重複", CanvasButtonWireEditing.DescribeProblem(data, 2));
            StringAssert.Contains("スライダー", CanvasButtonWireEditing.DescribeProblem(data, 3));
            Assert.IsNull(CanvasButtonWireEditing.DescribeProblem(data, 4));
            Assert.IsNull(CanvasButtonWireEditing.DescribeProblem(data, 99));

            data.Buttons[0].Target = new AssetRef { Type = AssetType.Canvas, Id = target.Id };
            Assert.IsNull(CanvasButtonWireEditing.DescribeProblem(data, 0));
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { target, data });
            StringAssert.Contains("WireTarget", CanvasButtonWireEditing.Summarize(data.Buttons[0], lookup));
            Assert.AreEqual("Repeat → SendSignal 'b/repeat'", CanvasButtonWireEditing.Summarize(data.Buttons[4], lookup));

            Assert.IsFalse(CanvasButtonWireEditing.IsButtonAction(UiAction.SetOption), "SetOption はスライダー専用");
            Assert.IsTrue(CanvasButtonWireEditing.UsesTarget(UiAction.OpenCanvas));
            Assert.IsTrue(CanvasButtonWireEditing.UsesSignalKey(UiAction.SendSignal));
        }

        // ── 埋め込みの有効 / 無効(2026-10-06。StartInactive と配線の 3 アクション) ──

        [Test]
        public void ButtonWires_EmbeddedActions_UseEmbeddedRootPath_ProblemAndSummary()
        {
            var (parent, _) = WiredParentAndChild();
            parent.Buttons = new[]
            {
                new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.Click, Action = UiAction.ToggleEmbedded, EmbeddedRootPath = "OptionRoot" },
                new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.LongPress, Action = UiAction.ActivateEmbedded, EmbeddedRootPath = "Ghost" },
                new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.DoubleClick, Action = UiAction.DeactivateEmbedded },
            };

            Assert.IsTrue(CanvasButtonWireEditing.UsesEmbeddedRootPath(UiAction.ActivateEmbedded));
            Assert.IsTrue(CanvasButtonWireEditing.UsesEmbeddedRootPath(UiAction.DeactivateEmbedded));
            Assert.IsTrue(CanvasButtonWireEditing.UsesEmbeddedRootPath(UiAction.ToggleEmbedded));
            Assert.IsFalse(CanvasButtonWireEditing.UsesEmbeddedRootPath(UiAction.CloseSelf));
            Assert.IsTrue(CanvasButtonWireEditing.IsButtonAction(UiAction.ToggleEmbedded));

            Assert.IsTrue(CanvasButtonWireEditing.HasEmbed(parent, "OptionRoot"));
            Assert.IsFalse(CanvasButtonWireEditing.HasEmbed(parent, "Ghost"));
            Assert.IsFalse(CanvasButtonWireEditing.HasEmbed(parent, null));
            Assert.IsFalse(CanvasButtonWireEditing.HasEmbed(null, "OptionRoot"));

            Assert.IsNull(CanvasButtonWireEditing.DescribeProblem(parent, 0), "登録済みの埋め込みを指す配線は問題なし");
            StringAssert.Contains("登録されていません", CanvasButtonWireEditing.DescribeProblem(parent, 1));
            Assert.IsNull(CanvasButtonWireEditing.DescribeProblem(parent, 2), "空 = 自分が属する埋め込み(実行時に決まる)は問題にしない");

            Assert.AreEqual("Click → ToggleEmbedded 'OptionRoot'", CanvasButtonWireEditing.Summarize(parent.Buttons[0], null));
            StringAssert.Contains("自分が属する埋め込み", CanvasButtonWireEditing.Summarize(parent.Buttons[2], null));
        }

        [Test]
        public void EmbeddedActiveValidator_WarnsUnknownEmbed_AndFirstSelectedUnderStartInactive()
        {
            var (parent, child) = WiredParentAndChild();
            var ctx = new ValidationContext(new List<AssetDataBase> { parent, child });
            List<ValidationResult> Run() => new(new CanvasEmbeddedActiveValidator().Validate(parent, ctx));

            parent.Buttons = new[]
            {
                new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.Click, Action = UiAction.ToggleEmbedded, EmbeddedRootPath = "OptionRoot" },
                new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.LongPress, Action = UiAction.DeactivateEmbedded },
            };
            parent.FirstSelected = "OptionRoot/BtnX";
            Assert.AreEqual(0, Run().Count, "登録済みの埋め込み・空の指定・有効で始まる埋め込みの配下の FirstSelected は何も出さない");

            parent.Buttons[0].EmbeddedRootPath = "Ghost";
            // EmbeddedRootPath は埋め込みのアクションのときだけ見る(他のアクションに値が残っていても何も出さない)
            parent.Buttons[1] = new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.LongPress, Action = UiAction.CloseSelf, EmbeddedRootPath = "Ghost" };
            var unknown = Run();
            Assert.AreEqual(1, unknown.Count);
            Assert.IsTrue(Has(unknown, "DD-CANVAS-WIRE-EMBED-UNKNOWN", ValidationSeverity.Warning));

            parent.Buttons = null;
            var embed = parent.EmbeddedCanvases[0];
            embed.StartInactive = true;
            parent.EmbeddedCanvases[0] = embed;
            var first = Run();
            Assert.AreEqual(1, first.Count);
            Assert.IsTrue(Has(first, "DD-CANVAS-EMBED-FIRSTSELECTED-INACTIVE", ValidationSeverity.Warning));

            parent.FirstSelected = "ParentBtn";
            Assert.AreEqual(0, Run().Count, "埋め込みの外の FirstSelected は対象外");
            Assert.AreEqual(0, new List<ValidationResult>(new CanvasEmbeddedActiveValidator().Validate(child, ctx)).Count, "埋め込みの無い CanvasData では何も出さない");

            // GG-R-09: スライダーの配線に埋め込みのアクション
            parent.Sliders = new[] { new SliderWire { ElementPath = "Sld", Action = UiAction.ToggleEmbedded } };
            var slider = Run();
            Assert.AreEqual(1, slider.Count);
            Assert.IsTrue(Has(slider, "DD-CANVAS-SLIDER-EMBED-ACTION", ValidationSeverity.Warning));
        }

        // レビュー [65] GG-R-07: 配線の対象は、直下の登録に加えて子の登録を連結した入れ子の入れ子も選べる(検査も誤って警告しない)。
        [Test]
        public void EmbeddedActions_NestedPaths_AreSelectable_AndNotWarned()
        {
            var (parent, child) = WiredParentAndChild();
            var grand = Data(9106, "Grand");
            child.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Inner", Canvas = IdOf(grand) } };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child, grand });

            var paths = new List<string>();
            CanvasButtonWireEditing.CollectEmbedPaths(parent, lookup, paths);
            CollectionAssert.AreEqual(new[] { "OptionRoot", "OptionRoot/Inner" }, paths);
            Assert.IsTrue(CanvasButtonWireEditing.HasEmbed(parent, "OptionRoot/Inner", lookup));
            Assert.IsFalse(CanvasButtonWireEditing.HasEmbed(parent, "OptionRoot/Inner"), "lookup 無しでは直下だけ");
            Assert.IsFalse(CanvasButtonWireEditing.HasEmbed(parent, "Inner", lookup), "子のルート基準の名前は親からは指せない");

            parent.Buttons = new[] { new ButtonWire { ButtonPath = "ParentBtn", Trigger = WireTrigger.Click, Action = UiAction.ToggleEmbedded, EmbeddedRootPath = "OptionRoot/Inner" } };
            Assert.IsNull(CanvasButtonWireEditing.DescribeProblem(parent, 0, lookup));
            var ctx = new ValidationContext(new List<AssetDataBase> { parent, child, grand });
            var results = new List<ValidationResult>(new CanvasEmbeddedActiveValidator().Validate(parent, ctx));
            Assert.AreEqual(0, results.Count, "入れ子の入れ子を指す配線に DD-CANVAS-WIRE-EMBED-UNKNOWN を出さない");
        }
        private static ElementFx Fx(string path) => new() { ElementPath = path };

        private static List<ValidationResult> Validate(CanvasData data, params CanvasData[] others)
        {
            var assets = new List<AssetDataBase> { data };
            assets.AddRange(others);
            var ctx = new ValidationContext(assets);
            var results = new List<ValidationResult>();
            results.AddRange(new CanvasDataValidator().Validate(data, ctx));
            results.AddRange(new CanvasEmbeddedValidator().Validate(data, ctx));
            return results;
        }

        private static bool Has(List<ValidationResult> results, string code, ValidationSeverity severity)
            => results.Exists(r => r.Code == code && r.Severity == severity);

        // ── Validator ──

        [Test]
        public void Validator_EmptyEmbeddedCanvases_ReportsNothingNew()
        {
            var prefab = SaveChildPrefab("EmptyEmbedsPrefab");
            var data = Data(10, "A", prefab);
            Assert.IsFalse(Validate(data).Exists(r => !string.IsNullOrEmpty(r.Code) && r.Code.StartsWith("DD-CANVAS-EMBED")));
        }

        [Test]
        public void Validator_RootPathMissing_Duplicate_Unset_Self_AreWarnings()
        {
            var child = SaveChildPrefab("VOption");
            var parentPrefab = SaveParentPrefab("VHud", child, "OptionRoot");
            var other = Data(20, "Other", child);
            var parent = Data(21, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[]
            {
                new EmbeddedCanvas { RootPath = "NoSuchRoot", Canvas = IdOf(other) },
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(other) },
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(other) },
                new EmbeddedCanvas { RootPath = "Title" },
                new EmbeddedCanvas { RootPath = "Title", Canvas = IdOf(parent) },
            };

            var results = Validate(parent, other);

            Assert.IsTrue(Has(results, "DD-CANVAS-EMBED-ROOT", ValidationSeverity.Warning));
            Assert.IsTrue(Has(results, "DD-CANVAS-EMBED-DUP", ValidationSeverity.Warning));
            Assert.IsTrue(Has(results, "DD-CANVAS-EMBED-UNSET", ValidationSeverity.Warning));
            Assert.IsTrue(Has(results, "DD-CANVAS-EMBED-SELF", ValidationSeverity.Warning));
            Assert.IsFalse(results.Exists(r => r.Code.StartsWith("DD-CANVAS-EMBED") && r.Severity == ValidationSeverity.Error), "新規の検査は Warning / Info のみ");
        }

        [Test]
        public void Validator_ChildCanvasNotFound_IsWarning()
        {
            var child = SaveChildPrefab("MOption");
            var parentPrefab = SaveParentPrefab("MHud", child, "OptionRoot");
            var parent = Data(31, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = new AssetId<CanvasMarker>(0x7777_0001UL, AssetType.Canvas) } };

            Assert.IsTrue(Has(Validate(parent), "DD-CANVAS-EMBED-MISSING", ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_IndirectCycle_IsWarning()
        {
            var childPrefab = SaveChildPrefab("COption");
            var parentPrefab = SaveParentPrefab("CHud", childPrefab, "OptionRoot");
            var parent = Data(41, "Hud", parentPrefab);
            var child = Data(42, "Option", childPrefab);
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            child.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Panel", Canvas = IdOf(parent) } };

            Assert.IsTrue(Has(Validate(parent, child), "DD-CANVAS-EMBED-CYCLE", ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_RootNotAnInstanceOfChildPrefab_IsWarning()
        {
            var optionPrefab = SaveChildPrefab("POption");
            var otherPrefab = SaveChildPrefab("POther");
            var parentPrefab = SaveParentPrefab("PHud", optionPrefab, "OptionRoot");
            var parent = Data(51, "Hud", parentPrefab);
            var wrongChild = Data(52, "WrongChild", otherPrefab); // OptionRoot の実体は POption のインスタンス
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(wrongChild) } };

            Assert.IsTrue(Has(Validate(parent, wrongChild), "DD-CANVAS-EMBED-PREFAB", ValidationSeverity.Warning));

            var rightChild = Data(53, "RightChild", optionPrefab);
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(rightChild) } };
            Assert.IsFalse(Has(Validate(parent, rightChild), "DD-CANVAS-EMBED-PREFAB", ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_ParentRowOnSameElement_IsInfo()
        {
            var optionPrefab = SaveChildPrefab("IOption");
            var parentPrefab = SaveParentPrefab("IHud", optionPrefab, "OptionRoot");
            var parent = Data(61, "Hud", parentPrefab);
            var child = Data(62, "Option", optionPrefab);
            child.ElementEffects = new[] { Fx("Panel") };
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            parent.ElementEffects = new[] { Fx("OptionRoot/Panel") };

            var results = Validate(parent, child);
            Assert.IsTrue(Has(results, "DD-CANVAS-EMBED-OVERRIDE", ValidationSeverity.Info));
            Assert.IsFalse(results.Exists(r => r.Code == "DD-CANVAS-EMBED-PREFAB"));
        }

        [Test]
        public void Validator_ParentButtonRow_OnlyOverridesTheSameTrigger()
        {
            var optionPrefab = SaveChildPrefab("TOption");
            var parentPrefab = SaveParentPrefab("THud", optionPrefab, "OptionRoot");
            var parent = Data(63, "Hud", parentPrefab);
            var child = Data(64, "Option", optionPrefab);
            child.Buttons = new[]
            {
                new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.Click },
                new ButtonWire { ButtonPath = "BtnX", Trigger = WireTrigger.LongPress },
            };
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            parent.Buttons = new[] { new ButtonWire { ButtonPath = "OptionRoot/BtnX", Trigger = WireTrigger.Click } };

            var infos = Validate(parent, child).FindAll(r => r.Code == "DD-CANVAS-EMBED-OVERRIDE");

            Assert.AreEqual(1, infos.Count, "Click だけが親に上書きされる(LongPress は子の設定が使われる)");
            StringAssert.Contains("Click", infos[0].Message);
        }

        [Test]
        public void Validator_OverlappingEmbedRoots_IsWarning_InRuntimeValidator()
        {
            var optionPrefab = SaveChildPrefab("NOption");
            var parentPrefab = SaveParentPrefab("NHud", optionPrefab, "OptionRoot");
            var a = Data(65, "A", optionPrefab);
            var b = Data(66, "B", optionPrefab);
            var parent = Data(67, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[]
            {
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(a) },
                new EmbeddedCanvas { RootPath = "OptionRoot/Panel", Canvas = IdOf(b) },
            };

            var results = Validate(parent, a, b);

            Assert.IsTrue(Has(results, "DD-CANVAS-EMBED-NESTED-ROOT", ValidationSeverity.Warning));
            Assert.AreEqual(1, results.FindAll(r => r.Code == "DD-CANVAS-EMBED-NESTED-ROOT").Count, "内側の登録 1 件だけを報告する");
        }

        [Test]
        public void Validator_ChildThatEmbedsTheSamePlaceAsAnotherRegistration_IsWarning()
        {
            // Hud が OptionRoot(Option)と OptionRoot/Panel(Volume)の両方を登録し、Option 自身も Panel(Volume)を埋め込んでいる。
            var optionPrefab = SaveChildPrefab("MOption2");
            var parentPrefab = SaveParentPrefab("MHud2", optionPrefab, "OptionRoot");
            var volume = Data(68, "Volume", optionPrefab);
            var option = Data(69, "Option", optionPrefab);
            option.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "Panel", Canvas = IdOf(volume) } };
            var parent = Data(90, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[]
            {
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(option) },
                new EmbeddedCanvas { RootPath = "OptionRoot/Panel", Canvas = IdOf(volume) },
            };

            var results = Validate(parent, option, volume);

            Assert.IsTrue(results.Exists(r => r.Code == "DD-CANVAS-EMBED-NESTED-ROOT" && r.Message.Contains("EmbeddedCanvases[0]")), "子が自分で埋め込んでいる場所が別の登録と重なる");
        }

        [TestCase("/OptionRoot", true)]
        [TestCase("OptionRoot/", true)]
        [TestCase("Group\\OptionRoot", true)]
        [TestCase("Group//OptionRoot", true)]
        [TestCase("./OptionRoot", true)]
        [TestCase("Group/./OptionRoot", true)]
        [TestCase("OptionRoot", false)]
        [TestCase("Group/OptionRoot", false)]
        public void Validator_RootPathForm_IsWarning(string rootPath, bool malformed)
        {
            var optionPrefab = SaveChildPrefab("FOption");
            var parentPrefab = SaveParentPrefab("FHud", optionPrefab, "OptionRoot");
            var child = Data(96, "Option", optionPrefab);
            var parent = Data(97, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = rootPath, Canvas = IdOf(child) } };

            Assert.AreEqual(malformed, Has(Validate(parent, child), "DD-CANVAS-EMBED-PATH-FORM", ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_ChildNotPreload_IsWarning_AndPreloadIsNot()
        {
            var optionPrefab = SaveChildPrefab("LOption");
            var parentPrefab = SaveParentPrefab("LHud", optionPrefab, "OptionRoot");
            var child = Data(98, "Option", optionPrefab);
            var parent = Data(99, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };

            child.Flags.Load = LoadMode.LazyLoad;
            Assert.IsTrue(Has(Validate(parent, child), "DD-CANVAS-EMBED-NOT-PRELOAD", ValidationSeverity.Warning));

            child.Flags.Load = LoadMode.Preload;
            Assert.IsFalse(Has(Validate(parent, child), "DD-CANVAS-EMBED-NOT-PRELOAD", ValidationSeverity.Warning));
        }

        [Test]
        public void Validator_NonOverlappingSiblingEmbeds_ReportNoOverlap()
        {
            var optionPrefab = SaveChildPrefab("SOption");
            var parentPrefab = SaveParentPrefab("SHud", optionPrefab, "OptionRoot");
            var a = Data(91, "A", optionPrefab);
            var b = Data(92, "B", optionPrefab);
            var parent = Data(93, "Hud", parentPrefab);
            parent.EmbeddedCanvases = new[]
            {
                new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(a) },
                new EmbeddedCanvas { RootPath = "Title", Canvas = IdOf(b) },
            };

            Assert.IsFalse(Has(Validate(parent, a, b), "DD-CANVAS-EMBED-NESTED-ROOT", ValidationSeverity.Warning));
        }

        // ── 候補の検出・登録・削除 ──

        [Test]
        public void DetectCandidates_DoesNotProposeNestedNestedPrefabInstances()
        {
            // Hud の中の OptionRoot(Option の Prefab)の、そのまた中の Inner(Volume の Prefab)。
            // Inner は Option 自身の Canvas Editor で登録するもの。Hud でも登録すると同じ要素に子の設定が重なる。
            var volumePrefab = SaveChildPrefab("QVolume");
            var optionRoot = NewRect("QOption");
            var inner = (GameObject)PrefabUtility.InstantiatePrefab(volumePrefab, optionRoot.transform);
            inner.name = "Inner";
            var optionPrefab = PrefabUtility.SaveAsPrefabAsset(optionRoot, $"{_folder}/QOption.prefab");
            Object.DestroyImmediate(optionRoot);
            var parentPrefab = SaveParentPrefab("QHud", optionPrefab, "OptionRoot");

            var optionCanvas = Data(94, "Option", optionPrefab);
            var volumeCanvas = Data(95, "Volume", volumePrefab);
            var contents = PrefabUtility.LoadPrefabContents($"{_folder}/QHud.prefab");
            try
            {
                var innerInHud = contents.transform.Find("OptionRoot/Inner");
                Assert.IsNotNull(innerInHud);
                var innerIsRoot = PrefabUtility.IsAnyPrefabInstanceRoot(innerInHud.gameObject);

                var found = CanvasEmbeddedEditing.DetectCandidates(contents, new[] { optionCanvas, volumeCanvas }, null);

                Assert.IsTrue(found.Exists(c => c.RootPath == "OptionRoot"), "外側の入れ子 Prefab は候補");
                Assert.IsFalse(found.Exists(c => c.RootPath == "OptionRoot/Inner"),
                    $"入れ子の入れ子は提案しない(Unity の IsAnyPrefabInstanceRoot = {innerIsRoot})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Test]
        public void DetectCandidates_FindsNestedPrefabInstancesMatchingACanvasPrefab()
        {
            var optionPrefab = SaveChildPrefab("DOption");
            var unrelated = SaveChildPrefab("DUnrelated");
            var parentRoot = NewRect("DHudRoot");
            _objects.Add(parentRoot);
            var optionInstance = (GameObject)PrefabUtility.InstantiatePrefab(optionPrefab, parentRoot.transform);
            optionInstance.name = "OptionRoot";
            var unrelatedInstance = (GameObject)PrefabUtility.InstantiatePrefab(unrelated, parentRoot.transform);
            unrelatedInstance.name = "Unrelated"; // どの CanvasData の Prefab でもない
            var deepHolder = NewRect("Group", parentRoot.transform);
            var deep = (GameObject)PrefabUtility.InstantiatePrefab(optionPrefab, deepHolder.transform);
            deep.name = "DeepOption";

            var optionCanvas = Data(71, "Option", optionPrefab);
            var parent = Data(72, "Hud");

            var found = CanvasEmbeddedEditing.DetectCandidates(parentRoot, new[] { optionCanvas }, parent.EmbeddedCanvases);

            Assert.AreEqual(2, found.Count);
            Assert.IsTrue(found.Exists(c => c.RootPath == "OptionRoot" && c.Canvas == optionCanvas && !c.Registered));
            Assert.IsTrue(found.Exists(c => c.RootPath == "Group/DeepOption" && c.Canvas == optionCanvas));

            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(optionCanvas) } };
            found = CanvasEmbeddedEditing.DetectCandidates(parentRoot, new[] { optionCanvas }, parent.EmbeddedCanvases);
            Assert.IsTrue(found.Find(c => c.RootPath == "OptionRoot").Registered);
            Assert.IsFalse(found.Find(c => c.RootPath == "Group/DeepOption").Registered);
        }

        [Test]
        public void Register_AddsRow_ThenIsIdempotent_AndReplacesChildOfSameRoot()
        {
            var parent = Data(81, "Hud");
            var a = Data(82, "A");
            var b = Data(83, "B");

            Assert.IsTrue(CanvasEmbeddedEditing.Register(parent, "OptionRoot", a));
            Assert.AreEqual(1, parent.EmbeddedCanvases.Length);
            Assert.AreEqual("OptionRoot", parent.EmbeddedCanvases[0].RootPath);
            Assert.AreEqual(a.Id, parent.EmbeddedCanvases[0].Canvas.Value);
            Assert.AreEqual(AssetType.Canvas, parent.EmbeddedCanvases[0].Canvas.Type);

            Assert.IsFalse(CanvasEmbeddedEditing.Register(parent, "OptionRoot", a), "同じ登録は何も書かない");
            Assert.IsTrue(CanvasEmbeddedEditing.Register(parent, "OptionRoot", b), "同じ RootPath は子だけ差し替える");
            Assert.AreEqual(1, parent.EmbeddedCanvases.Length);
            Assert.AreEqual(b.Id, parent.EmbeddedCanvases[0].Canvas.Value);

            Assert.IsFalse(CanvasEmbeddedEditing.Register(parent, "Self", parent), "自分自身は登録しない");
            Assert.IsFalse(CanvasEmbeddedEditing.Register(parent, "", a), "RootPath が空は登録しない");
        }

        [Test]
        public void AddEmpty_And_RemoveAt_EditTheArray()
        {
            var parent = Data(91, "Hud");
            CanvasEmbeddedEditing.AddEmpty(parent);
            CanvasEmbeddedEditing.AddEmpty(parent);
            Assert.AreEqual(2, parent.EmbeddedCanvases.Length);
            parent.EmbeddedCanvases[0].RootPath = "First";
            parent.EmbeddedCanvases[1].RootPath = "Second";

            Assert.IsTrue(CanvasEmbeddedEditing.RemoveAt(parent, 0));
            Assert.AreEqual(1, parent.EmbeddedCanvases.Length);
            Assert.AreEqual("Second", parent.EmbeddedCanvases[0].RootPath);
            Assert.IsFalse(CanvasEmbeddedEditing.RemoveAt(parent, 5));
        }

        // ── 自動収集 ──

        [Test]
        public void CollectMerged_SkipsRegisteredEmbeddedRoots_ButKeepsExistingRowsAndTheRootItself()
        {
            var root = NewRect("Hud");
            _objects.Add(root);
            NewRect("Title", root.transform, typeof(Image));
            var option = NewRect("OptionRoot", root.transform, typeof(Image));
            NewRect("Panel", option.transform, typeof(Image));
            var inner = NewRect("Inner", option.transform);
            NewRect("Deep", inner.transform, typeof(Image));
            NewRect("Option2", root.transform, typeof(Image)); // 接頭辞が同じだけの別要素

            var existing = new[] { Fx("OptionRoot/Panel") }; // 親での上書きとして既にある行

            var merged = CanvasElementFxCollector.CollectMerged(root, existing, new[] { "OptionRoot" });
            var paths = new List<string>();
            foreach (var m in merged)
            {
                paths.Add(m.ElementPath);
            }

            CollectionAssert.Contains(paths, "Title");
            CollectionAssert.Contains(paths, "OptionRoot", "埋め込みルート自身は親の要素として集める");
            CollectionAssert.Contains(paths, "Option2");
            CollectionAssert.Contains(paths, "OptionRoot/Panel", "既に親にある行は消えない(親での上書き)");
            CollectionAssert.DoesNotContain(paths, "OptionRoot/Inner/Deep", "登録済みの埋め込み配下は集めない");

            var unregistered = CanvasElementFxCollector.CollectMerged(root, null);
            var all = new List<string>();
            foreach (var m in unregistered)
            {
                all.Add(m.ElementPath);
            }

            CollectionAssert.Contains(all, "OptionRoot/Inner/Deep", "未登録の入れ子は従来どおり拾う");
        }

        // ── グループ構築 ──

        [Test]
        public void BuildGroups_SplitsParentRows_Overrides_AndChildSummaries()
        {
            var child = Data(101, "Option");
            child.ElementEffects = new[]
            {
                new ElementFx { ElementPath = "Panel", AppearPreset = new UiPresetRef { Preset = UiPreset.FadeIn } },
                Fx("BtnX"),
            };
            var parent = Data(102, "Hud");
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            parent.ElementEffects = new[] { Fx("Title"), Fx("OptionRoot/Panel"), Fx("OptionRoot") };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child });

            var groups = CanvasEmbeddedEditing.BuildGroups(parent, lookup);

            CollectionAssert.AreEqual(new[] { 0, 2 }, groups.ParentRows, "親の要素 = Title と 埋め込みルート自身(配下ではない)");
            Assert.AreEqual(1, groups.Embeds.Count);
            var g = groups.Embeds[0];
            Assert.AreSame(child, g.Child);
            CollectionAssert.AreEqual(new[] { 1 }, g.OverrideRows, "親での上書き = 埋め込み配下を指す親の行");
            Assert.AreEqual(2, g.ChildRows.Count);
            Assert.IsTrue(g.ChildRows[0].OverriddenByParent, "Panel は親の行がある");
            StringAssert.Contains("Appear: FadeIn", g.ChildRows[0].Summary);
            Assert.IsFalse(g.ChildRows[1].OverriddenByParent);
        }

        [Test]
        public void BuildGroups_Filter_AppliesToParentOverrideAndChildRows()
        {
            var child = Data(111, "Option");
            child.ElementEffects = new[] { Fx("Panel"), Fx("BtnX") };
            var parent = Data(112, "Hud");
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            parent.ElementEffects = new[] { Fx("Title"), Fx("TitleSub") };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child });

            var groups = CanvasEmbeddedEditing.BuildGroups(parent, lookup, "btn");

            Assert.AreEqual(0, groups.ParentRows.Count);
            Assert.AreEqual(1, groups.Embeds[0].ChildRows.Count);
            Assert.AreEqual("BtnX", groups.Embeds[0].ChildRows[0].ElementPath);
        }

        [Test]
        public void BuildGroups_UnresolvedChild_KeepsRowsInParent()
        {
            var parent = Data(121, "Hud");
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = new AssetId<CanvasMarker>(0x9999UL, AssetType.Canvas) } };
            parent.ElementEffects = new[] { Fx("OptionRoot/Panel") };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent });

            var groups = CanvasEmbeddedEditing.BuildGroups(parent, lookup);

            CollectionAssert.AreEqual(new[] { 0 }, groups.ParentRows);
            Assert.IsNull(groups.Embeds[0].Child);
        }

        [Test]
        public void BuildGroups_NoEmbeds_AllRowsAreParentRows()
        {
            var parent = Data(131, "Plain");
            parent.ElementEffects = new[] { Fx("A"), Fx("B") };
            var groups = CanvasEmbeddedEditing.BuildGroups(parent, CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent }));
            CollectionAssert.AreEqual(new[] { 0, 1 }, groups.ParentRows);
            Assert.AreEqual(0, groups.Embeds.Count);
        }

        // ── 持ち主の解決・パス変換 ──

        [Test]
        public void ResolveOwner_ChildElement_ReturnsChildWithChildPath_AndAncestors()
        {
            var child = Data(141, "Option");
            var parent = Data(142, "Hud");
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child });

            var owner = CanvasEmbeddedEditing.ResolveOwner(parent, "OptionRoot/Panel/Btn", lookup);

            Assert.AreSame(child, owner.Data);
            Assert.AreEqual("Panel/Btn", owner.Path);
            Assert.AreEqual(1, owner.Ancestors.Count);
            Assert.AreSame(parent, owner.Ancestors[0].Data);
            Assert.AreEqual("OptionRoot", owner.Ancestors[0].RootPath);
        }

        [Test]
        public void ResolveOwner_ParentElement_AndEmbedRootItself_BelongToTheParent()
        {
            var child = Data(151, "Option");
            var parent = Data(152, "Hud");
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "OptionRoot", Canvas = IdOf(child) } };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child });

            var title = CanvasEmbeddedEditing.ResolveOwner(parent, "Title", lookup);
            Assert.AreSame(parent, title.Data);
            Assert.AreEqual("Title", title.Path);
            Assert.AreEqual(0, title.Ancestors.Count);

            var root = CanvasEmbeddedEditing.ResolveOwner(parent, "OptionRoot", lookup);
            Assert.AreSame(parent, root.Data, "埋め込みルート自身は親の要素(子の行からは指せない)");
            Assert.AreEqual("OptionRoot", root.Path);

            var sibling = CanvasEmbeddedEditing.ResolveOwner(parent, "OptionRoot2/Panel", lookup);
            Assert.AreSame(parent, sibling.Data);
        }

        [Test]
        public void ResolveOwner_NestedAndCyclic()
        {
            var grand = Data(161, "Grand");
            var child = Data(162, "Child");
            var parent = Data(163, "Parent");
            parent.EmbeddedCanvases = new[] { new EmbeddedCanvas { RootPath = "A", Canvas = IdOf(child) } };
            child.EmbeddedCanvases = new[]
            {
                new EmbeddedCanvas { RootPath = "B", Canvas = IdOf(grand) },
                new EmbeddedCanvas { RootPath = "Loop", Canvas = IdOf(parent) }, // 循環: 無視される
            };
            var lookup = CanvasEmbeddedEditing.CanvasLookup.From(new[] { parent, child, grand });

            var deep = CanvasEmbeddedEditing.ResolveOwner(parent, "A/B/X/Y", lookup);
            Assert.AreSame(grand, deep.Data);
            Assert.AreEqual("X/Y", deep.Path);
            Assert.AreEqual(2, deep.Ancestors.Count);

            var loop = CanvasEmbeddedEditing.ResolveOwner(parent, "A/Loop/Z", lookup);
            Assert.AreSame(child, loop.Data, "循環する子は無視して手前の持ち主に倒す");
            Assert.AreEqual("Loop/Z", loop.Path);
        }

        [Test]
        public void ToAncestorPath_ConvertsChildPathIntoEachAncestorsFrame()
        {
            var a = Data(171, "A");
            var b = Data(172, "B");
            var links = new List<CanvasEmbeddedEditing.Link>
            {
                new(a, "X"),   // a のルートから見て b のルートは X
                new(b, "Y"),   // b のルートから見て対象のルートは Y
            };

            Assert.AreEqual("Y/p", CanvasEmbeddedEditing.ToAncestorPath(links, 1, "p"));
            Assert.AreEqual("X/Y/p", CanvasEmbeddedEditing.ToAncestorPath(links, 0, "p"));
            Assert.AreEqual("p", CanvasEmbeddedEditing.ToAncestorPath(links, 2, "p"), "index = 件数なら対象自身(変換なし)");
            Assert.AreEqual("X/Y", CanvasEmbeddedEditing.ToAncestorPath(links, 0, string.Empty));
        }

        // ── 選択した Transform の属する Canvas のルート ──

        [Test]
        public void FindCanvasRoot_PreviewInstanceUnderUiRoot_ReturnsTheCanvasRoot_NotTheSceneRoot()
        {
            var uiRoot = new GameObject(UiManager.RootName);
            _objects.Add(uiRoot);
            var layer = NewRect("HUD", uiRoot.transform);
            var canvasRoot = NewRect("HudCanvas(Clone)", layer.transform);
            var panel = NewRect("Panel", canvasRoot.transform);
            var btn = NewRect("Btn", panel.transform);

            var found = CanvasEmbeddedEditing.FindCanvasRoot(btn.transform);
            Assert.AreSame(canvasRoot.transform, found);
            Assert.AreEqual("Panel/Btn", TransformPath.GetRelative(found, btn.transform), "CanvasData のルート基準のパスになる");
            Assert.AreEqual("HUD/HudCanvas(Clone)/Panel/Btn", TransformPath.GetRelative(btn.transform.root, btn.transform), "従来の root 基準だと UI Root からのパスになってしまう");

            Assert.AreSame(canvasRoot.transform, CanvasEmbeddedEditing.FindCanvasRoot(canvasRoot.transform));
            Assert.IsNull(CanvasEmbeddedEditing.FindCanvasRoot(layer.transform), "レイヤー自身は Canvas の一部ではない");
        }

        [Test]
        public void FindCanvasRoot_PlainSceneObject_ReturnsNull_SoCallersFallBackToRoot()
        {
            var root = NewRect("Plain");
            _objects.Add(root);
            var child = NewRect("Child", root.transform);
            Assert.IsNull(CanvasEmbeddedEditing.FindCanvasRoot(child.transform));
            Assert.IsNull(CanvasEmbeddedEditing.FindCanvasRoot(null));
        }

        [Test]
        public void FindCanvasRoot_PrefabInstanceOfCanvasPrefab_ReturnsTheInstanceRoot()
        {
            var optionPrefab = SaveChildPrefab("FOption");
            var holder = NewRect("SceneHolder");
            _objects.Add(holder);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(optionPrefab, holder.transform);
            var panel = instance.transform.Find("Panel");

            Assert.AreSame(instance.transform, CanvasEmbeddedEditing.FindCanvasRoot(panel, optionPrefab));
            Assert.IsNull(CanvasEmbeddedEditing.FindCanvasRoot(panel, null));
        }
    }
}
