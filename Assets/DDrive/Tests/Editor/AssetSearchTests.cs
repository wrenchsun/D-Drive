using DDrive.Editor;
using DDrive.Foundation.Data;
using DDrive.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            var filter = "t:" + nameof(AssetDataBase);

            var all = AssetSearch.FindAssets(filter);
            var gameData = AssetSearch.FindAssets(filter, new[] { "Assets/GameData" });
            var again = AssetSearch.FindAssets(filter, null);

            Assert.AreEqual(misses + 2, AssetSearch.MissCount, "null / 空は既定ルートと同じキー");
            CollectionAssert.AreEqual(all, again, "null 指定は既定ルートと同じ結果");
            // フォルダを絞った結果は絞らない結果の部分集合(別エントリとしてキャッシュされている)。
            CollectionAssert.IsSubsetOf(gameData, all);
            Assert.LessOrEqual(gameData.Length, all.Length);
        }

        // ImportWatcher(OnPostprocessAllAssets)が自動で無効化するので、作成直後に手で Invalidate しなくても見つかる(2026-09-11 レビュー対応)。
        [Test]
        public void CreatedAsset_IsFound_WithoutManualInvalidate()
        {
            const string parent = "Assets/DDrive/Tests/Editor";
            const string folderName = "TempAssetSearch";
            const string folder = parent + "/" + folderName;
            const string assetPath = folder + "/SearchProbe.asset";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }

            try
            {
                var filter = "t:" + nameof(AssetDataBase);
                AssetSearch.FindAssets(filter, new[] { folder }); // 空の状態をキャッシュさせる

                var data = ScriptableObject.CreateInstance<SeData>();
                data.Id = 987654321;
                AssetDatabase.CreateAsset(data, assetPath);

                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                CollectionAssert.Contains(AssetSearch.FindAssets(filter, new[] { folder }), guid,
                    "作成直後でも(手動 Invalidate 無しで)見つかる");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                AssetSearch.Invalidate();
            }
        }
    }
}
