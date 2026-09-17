using System.Linq;
using DDrive.Editor.Creation;
using DDrive.Editor.Import;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Model;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] U-17 / [09_editor_tools.md] §1.2 — Project 右クリックの「Data を作成」の対応表。
    // アセットを実際に作るテストは AssetCreationServiceTests / ImportRuleServiceTests が既に持っているため、
    // ここでは「対応表が ImportRule のハンドラから正しく組み上がるか」と「カテゴリ推測」の純粋ロジックだけを見る
    // (実 GameData・カタログ・Addressables には一切触れない)。
    public sealed class SourceDataCreationTests
    {
        [SetUp]
        public void SetUp() => SourceDataCreation.Invalidate();

        [Test]
        public void Options_CoverEveryImportRuleHandler()
        {
            foreach (var handler in ImportRuleService.Handlers)
            {
                var option = SourceDataCreation.Find(handler.DataType);
                Assert.IsNotNull(option, $"{handler.DataType.Name} の選択肢が ImportRule のハンドラから作られていません");
                Assert.AreEqual(handler.Target, option.Target);
                CollectionAssert.AreEquivalent(handler.Extensions, option.Extensions);
            }
        }

        [Test]
        public void Options_HaveNoDuplicateDataTypes()
        {
            var duplicates = SourceDataCreation.Options
                .GroupBy(o => o.DataType)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.Name)
                .ToArray();

            CollectionAssert.IsEmpty(duplicates, "同じ Data 型の選択肢が重複しています");
        }

        // メニュー(AssetContextMenu)が宣言している 12 種別が全て引けること。
        [TestCase(typeof(SeData))]
        [TestCase(typeof(BgmData))]
        [TestCase(typeof(DDrive.Runtime.Material.TextureData))]
        [TestCase(typeof(DDrive.Runtime.Material.MaterialData))]
        [TestCase(typeof(ModelData))]
        [TestCase(typeof(AnimData))]
        [TestCase(typeof(Anim2DData))]
        [TestCase(typeof(PrefabData))]
        [TestCase(typeof(CanvasData))]
        [TestCase(typeof(VfxData))]
        [TestCase(typeof(ButtonSkinData))]
        [TestCase(typeof(SliderSkinData))]
        public void Find_ReturnsOptionForMenuDeclaredTypes(System.Type dataType)
        {
            var option = SourceDataCreation.Find(dataType);
            Assert.IsNotNull(option, $"{dataType.Name} の選択肢がありません([MenuItem] だけ増えて対応表に無い状態)");
            Assert.AreNotEqual(AssetType.None, option.Target);
            Assert.IsTrue(option.Extensions != null && option.Extensions.Length > 0);
            Assert.IsTrue(option.Configure != null || option.CreateOverride != null,
                $"{dataType.Name} は元アセットの割り当ても専用生成処理も持っていません");
        }

        // .mat は専用経路(UnityMaterialMigrator)を通る。
        [Test]
        public void MaterialOption_UsesDedicatedCreateOverride()
        {
            var option = SourceDataCreation.Find(typeof(DDrive.Runtime.Material.MaterialData));
            Assert.IsNotNull(option.CreateOverride);
            CollectionAssert.Contains(option.Extensions, ".mat");
        }

        // SourceAssets/<種別>/<カテゴリ...>/ は ImportRule と同じ「種別フォルダから先」。
        [TestCase("Assets/SourceAssets/Se/Player/Attack/Slash.wav", "Player/Attack")]
        [TestCase("Assets/SourceAssets/Se/Slash.wav", "")]
        [TestCase("Assets/SourceAssets/Texture/UI/Btn.png", "UI")]
        // それ以外は直上のフォルダ名 1 つ。
        [TestCase("Assets/Art/UI/Btn.png", "UI")]
        [TestCase("Assets/Btn.png", "")]
        public void ResolveCategory_FollowsFolderConvention(string assetPath, string expected)
        {
            Assert.AreEqual(expected, SourceDataCreation.ResolveCategory(assetPath));
        }
    }
}
