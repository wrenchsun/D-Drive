using DDrive.Editor;
using DDrive.Foundation.Data;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §9 — AssetSearch は同じ検索をプロジェクト変更までキャッシュし、Invalidate で捨てる(2026-09-11)。
    public class AssetSearchTests
    {
        [SetUp]
        public void SetUp() => AssetSearch.Invalidate();

        [Test]
        public void SameQuery_HitsCache_UntilInvalidated()
        {
            var filter = "t:" + nameof(AssetDataBase);
            var misses = AssetSearch.MissCount;

            var first = AssetSearch.FindAssets(filter);
            var second = AssetSearch.FindAssets(filter);

            Assert.AreEqual(misses + 1, AssetSearch.MissCount, "2 回目はキャッシュ");
            CollectionAssert.AreEqual(first, second);
            Assert.AreNotSame(first, second, "複製を返す(呼び出し側の書き換えでキャッシュを壊さない)");

            AssetSearch.Invalidate();
            AssetSearch.FindAssets(filter);
            Assert.AreEqual(misses + 2, AssetSearch.MissCount, "Invalidate 後は再検索");
        }

        [Test]
        public void DifferentFolders_AreSeparateEntries()
        {
            var misses = AssetSearch.MissCount;

            AssetSearch.FindAssets("t:" + nameof(AssetDataBase));
            AssetSearch.FindAssets("t:" + nameof(AssetDataBase), new[] { "Assets/GameData" });
            AssetSearch.FindAssets("t:" + nameof(AssetDataBase), null);

            Assert.AreEqual(misses + 2, AssetSearch.MissCount, "null / 空は既定ルートと同じキー");
        }
    }
}
