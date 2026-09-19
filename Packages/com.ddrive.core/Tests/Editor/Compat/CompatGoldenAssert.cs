using System;
using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.Compat;
using NUnit.Framework;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.11(P-3、2026-09-20) — 互換性スナップショットの比較・更新を 1 箇所に集約する。
    //
    // 更新手段は 2 通り(チケット本文の指示どおり):
    //   (1) 純粋なリフレクション由来のゴールデンは `Tools > D-Drive > Compat > スナップショットを更新`
    //       (`CompatSnapshotMenu`)。
    //   (2) 一時フィクスチャに依存するゴールデン(Tuning コード生成・ContentHash 等、テストの中でしか
    //       再現できない入力を使うもの)は、環境変数 DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1 を設定して
    //       このテストを再実行すると、比較の代わりにゴールデンファイルへ書き込む。
    //
    // どちらの経路でも「意図した変更のときだけ」しか更新が起きない(通常のテスト実行は比較のみ)。
    public static class CompatGoldenAssert
    {
        private const string EnvUpdateFlag = "DDRIVE_UPDATE_COMPAT_SNAPSHOTS";

        public static void AssertMatches(string goldenPath, string actual, string compatibilityHint)
        {
            var normalizedActual = Normalize(actual);

            if (IsUpdateRequested())
            {
                WriteGolden(goldenPath, normalizedActual);
                Assert.Pass($"[DDrive][Compat] {EnvUpdateFlag}=1 のためゴールデンを更新しました: {goldenPath}");
                return;
            }

            if (!File.Exists(goldenPath))
            {
                Assert.Fail(BuildMissingMessage(goldenPath, compatibilityHint));
                return;
            }

            var expected = Normalize(File.ReadAllText(goldenPath));
            Assert.AreEqual(expected, normalizedActual, BuildMismatchMessage(goldenPath, compatibilityHint));
        }

        private static bool IsUpdateRequested()
        {
            var value = Environment.GetEnvironmentVariable(EnvUpdateFlag);
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteGolden(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, content);
        }

        private static string BuildMissingMessage(string path, string hint) =>
            $"互換性ゴールデンが見つかりません: {path}\n{hint}\n" +
            $"意図した追加なら環境変数 {EnvUpdateFlag}=1 を設定してこのテストを再実行するか、" +
            "Tools > D-Drive > Compat > スナップショットを更新 を実行してコミットしてください。";

        private static string BuildMismatchMessage(string path, string hint) =>
            $"互換性スナップショットが一致しません: {path}\n{hint}\n" +
            $"意図した変更なら環境変数 {EnvUpdateFlag}=1 を設定してこのテストを再実行するか、" +
            "Tools > D-Drive > Compat > スナップショットを更新 を実行してゴールデンを更新し、" +
            "同じ PR で CHANGELOG.md の [Unreleased] 互換性節に追記してください。";

        internal static string Normalize(string content) => Regex.Replace(content ?? string.Empty, "\r\n?", "\n");
    }
}
