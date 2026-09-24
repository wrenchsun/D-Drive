using System.Collections.Generic;
using DDrive.Editor.Preload;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [11_tasks.md] M-1b(2026-09-25) — ScenePreloadCodeReferenceScanner.CountReferences は
    // ScenePreloadList が生成 ID 定数の直接呼び出し(コード参照)を拾うための判定ロジック。
    // 実ファイル IO を持たない純関数なので、文字列だけで境界条件を検証する。
    public class ScenePreloadCodeReferenceScannerTests
    {
        [Test]
        public void ExactMatch_IsCounted()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string> { "var h = Audio.PlaySe(SEID.PlayerSlash);" });

            Assert.AreEqual(1, counts["SEID.PlayerSlash"]);
        }

        [Test]
        public void NoOccurrence_IsNotInResult()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string> { "var h = Audio.PlaySe(SEID.PlayerJump);" });

            Assert.IsFalse(counts.ContainsKey("SEID.PlayerSlash"));
        }

        [Test]
        public void MultipleOccurrences_AcrossMultipleFiles_AreSummed()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string>
                {
                    "SEID.PlayerSlash; SEID.PlayerSlash;",
                    "Audio.PlaySe(SEID.PlayerSlash);",
                });

            Assert.AreEqual(3, counts["SEID.PlayerSlash"]);
        }

        // 単語境界チェック: "SEID.PlayerSlash" は "SEID.PlayerSlashHeavy" のプレフィックスとして
        // 部分一致してしまうため、境界チェックで除外されるはず。
        [Test]
        public void SuffixCollision_IsNotCounted()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string> { "Audio.PlaySe(SEID.PlayerSlashHeavy);" });

            Assert.IsFalse(counts.ContainsKey("SEID.PlayerSlash"));
        }

        // 逆方向の境界チェック: "XSEID.PlayerSlash" のようにクラス名の前が識別子文字だと誤検出になるため除外する。
        [Test]
        public void PrefixCollision_IsNotCounted()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string> { "var x = XSEID.PlayerSlash;" });

            Assert.IsFalse(counts.ContainsKey("SEID.PlayerSlash"));
        }

        // 完全修飾名(前に '.' が来る)は識別子文字ではないので、正当な参照として許容する。
        [Test]
        public void FullyQualifiedReference_IsCounted()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string> { "DDrive.Generated.SEID.PlayerSlash" });

            Assert.AreEqual(1, counts["SEID.PlayerSlash"]);
        }

        // コメント/文字列リテラル内かどうかは区別しない(誤検知よりも見逃しを避ける安全側の方針)。
        [Test]
        public void OccurrenceInsideCommentOrStringLiteral_IsStillCounted()
        {
            var counts = ScenePreloadCodeReferenceScanner.CountReferences(
                new List<string> { "SEID.PlayerSlash" },
                new List<string> { "// TODO: SEID.PlayerSlash を後で差し替える" });

            Assert.AreEqual(1, counts["SEID.PlayerSlash"]);
        }

        [Test]
        public void EmptyOrNullInputs_ReturnEmptyResult_WithoutThrowing()
        {
            Assert.AreEqual(0, ScenePreloadCodeReferenceScanner.CountReferences(null, null).Count);
            Assert.AreEqual(0, ScenePreloadCodeReferenceScanner.CountReferences(new List<string>(), new List<string>()).Count);
        }
    }
}
