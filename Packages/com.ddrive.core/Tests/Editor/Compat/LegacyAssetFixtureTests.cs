using System.IO;
using DDrive.Editor.Compat;
using DDrive.Foundation.Data;
using NUnit.Framework;
using UnityEditor;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.1 / §5.11-2(P-3、2026-09-20) — 旧版フィクスチャ(現在の各種別 Data を
    // 1 つずつ Unity Editor 経由で作成し、Tests/Editor/Compat/Fixtures/v1_0_0/ にテキストとしてコミット
    // したもの)が、将来のシリアライズ形式変更後も既知の値を保ったまま読めることを固定する。
    //
    // フィクスチャの作り方(再生成する場合、`Assets.CreateAsset` は Unity Editor 経由の原則どおり
    // Editor から呼ぶこと。`.asset` をテキストで手編集しない):
    //   `AssetDatabase.CreateInstance(型)` → `Id`/`DisplayName`/`Category` に本テストが期待する値
    //   (下記 ExpectedDisplayName/ExpectedCategory)を設定 → `AssetDatabase.CreateAsset(instance,
    //   FixturesRoot + "/" + 型名 + ".asset")`。新しい AssetType を追加したら、対応する具象 Data 型の
    //   フィクスチャも同じ手順で追加すること(`AllConcreteDataTypes_HaveFixture` が検出する)。
    public class LegacyAssetFixtureTests
    {
        public const string FixturesRoot = "Packages/com.ddrive.core/Tests/Editor/Compat/Fixtures/v1_0_0";
        private const string ExpectedCategory = "CompatFixture";

        [Test]
        public void AllConcreteDataTypes_HaveFixture()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var type in SerializedLayoutSnapshotBuilder.ConcreteDataTypes())
            {
                var path = $"{FixturesRoot}/{type.Name}.asset";
                if (!File.Exists(path))
                {
                    missing.Add(type.Name);
                }
            }

            Assert.IsEmpty(missing,
                "新しい AssetType を追加したら、対応する Data 型の旧版フィクスチャ(Tests/Editor/Compat/Fixtures/v1_0_0/<型名>.asset)も " +
                "Unity Editor 経由(AssetDatabase.CreateAsset)で追加してください: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryFixture_LoadsWithKnownValues()
        {
            Assert.IsTrue(Directory.Exists(FixturesRoot), $"{FixturesRoot} が見つかりません。");

            var checkedCount = 0;
            foreach (var path in Directory.GetFiles(FixturesRoot, "*.asset"))
            {
                var normalized = path.Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(normalized);
                Assert.IsNotNull(asset, $"{normalized} が読み込めません(フィールド削除・型変更で壊れていないか確認)。");

                var expectedDisplayName = "CompatFixture_" + Path.GetFileNameWithoutExtension(normalized);
                Assert.AreEqual(expectedDisplayName, asset.DisplayName, $"{normalized}: DisplayName が既知の値と異なります。");
                Assert.AreEqual(ExpectedCategory, asset.Category, $"{normalized}: Category が既知の値と異なります。");
                Assert.AreNotEqual(0UL, asset.Id, $"{normalized}: Id が既知の値(非 0)と異なります。");
                checkedCount++;
            }

            Assert.Greater(checkedCount, 0, "フィクスチャが 1 件も見つかりませんでした。");
        }
    }
}
