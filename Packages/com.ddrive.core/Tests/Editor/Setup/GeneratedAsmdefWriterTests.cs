using DDrive.Editor.Codegen;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Setup
{
    // [42_distribution.md] §2.3-7/§7 A-8/§6 P-6(2026-09-20) — `DDrive.Generated.asmdef` の出力内容と
    // ガード条件(emit=false・Assets/ 配下以外)を、実ファイルへ一切書き込まずに固定する
    // (このチケットの指示「開発リポジトリの状態を壊さない」に沿い、実際に asmdef を書き出す経路
    // 〔Assets 配下への書き込み〕はここではテストしない。書き込み前のガードで return するケースだけを
    // 確認する)。
    public class GeneratedAsmdefWriterTests
    {
        [Test]
        public void BuildAsmdefJson_ReferencesFoundationAndRuntime()
        {
            var json = GeneratedAsmdefWriter.BuildAsmdefJson();

            StringAssert.Contains("\"name\": \"DDrive.Generated\"", json);
            StringAssert.Contains("\"DDrive.Foundation\"", json);
            StringAssert.Contains("\"DDrive.Runtime\"", json);
        }

        [Test]
        public void EnsureAsmdef_EmitFalse_NoOp()
        {
            var result = GeneratedAsmdefWriter.EnsureAsmdef("Assets/Generated", emit: false);
            Assert.IsFalse(result);
        }

        [Test]
        public void EnsureAsmdef_NullOrEmptyFolder_NoOp()
        {
            Assert.IsFalse(GeneratedAsmdefWriter.EnsureAsmdef(null, emit: true));
            Assert.IsFalse(GeneratedAsmdefWriter.EnsureAsmdef(string.Empty, emit: true));
        }

        [Test]
        public void EnsureAsmdef_FolderOutsideAssets_NoOp()
        {
            // Packages 配下のテスト用一時フォルダ等は対象外(実フォルダが存在しなくても、
            // Assets/ 前置きの判定で書き込み前に return するため安全)。
            // [47_review_p_tickets_2026-09-20.md] P1-3(2026-09-20) — テストの一時アセットは
            // `TestTempFolder`(Assets 配下)へ寄せたが、このテストは意図的に「Assets 配下ではない
            // パス」を検証するためのものなので、ここだけは Packages 配下の非 Assets パスのままにする。
            var result = GeneratedAsmdefWriter.EnsureAsmdef("Packages/com.ddrive.core/Tests/Editor/TempGameData", emit: true);
            Assert.IsFalse(result);
        }
    }
}
