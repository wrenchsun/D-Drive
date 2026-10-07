using System.IO;
using System.Text.RegularExpressions;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Compat
{
    // [42_distribution.md] §5.11(P-3、2026-09-20) — 互換性スナップショット(ゴールデン)の更新導線。
    //
    // 「意図した変更のときだけ同 PR で更新する」(§5.11 冒頭)ため、更新は人がこのメニューを押すか、
    // 環境変数 DDRIVE_UPDATE_COMPAT_SNAPSHOTS=1 でテストを再実行したときにしか起きない
    // (通常のテスト実行では書き込まず比較のみ)。
    //
    // ここで更新するのは「純粋なリフレクション/SerializedObject 走査」から作れるもの(公開 API・
    // シリアライズ enum・シリアライズ形式レイアウト・ネットメッセージ・Editor 契約)のみ。
    // 一時フィクスチャに依存するもの(Tuning コード生成・ContentHash・KnownPrefixes 由来の定数名例・
    // Validator の重さ)は各テストファイル側で同じ環境変数を見て自己更新する
    // (`CompatGoldenAssert.AssertMatches`、Tests/Editor/Compat/CompatGoldenAssert.cs)。
    public static class CompatSnapshotMenu
    {
        [MenuItem(DDriveMenu.Compat + "スナップショットを更新")]
        public static void UpdateAll()
        {
            WriteIfChanged(CompatSnapshotPaths.PublicApiFoundation, PublicApiSnapshotBuilder.Build("DDrive.Foundation"));
            WriteIfChanged(CompatSnapshotPaths.PublicApiRuntime, PublicApiSnapshotBuilder.Build("DDrive.Runtime"));
            WriteIfChanged(CompatSnapshotPaths.SerializedLayout, SerializedLayoutSnapshotBuilder.Build());
            WriteIfChanged(CompatSnapshotPaths.Enums, SerializedEnumSnapshotBuilder.Build());
            WriteIfChanged(CompatSnapshotPaths.NetMessages, NetMessageSnapshotBuilder.Build());
            WriteIfChanged(CompatSnapshotPaths.EditorContract, EditorContractSnapshotBuilder.Build());

            // [1002_ddrive_mcp.md] §7 MCP-9: ddrive_* ツールの契約。DDrive.Editor.Mcp(isuzu 版 Unity MCP が有るときだけコンパイルされる)
            // のビルダーを型名で呼ぶ(DDrive.Editor から DDrive.Editor.Mcp への参照を作らない)。無ければ何もしない。
            var mcpTools = BuildMcpToolsSnapshot();
            if (mcpTools != null)
            {
                WriteIfChanged(CompatSnapshotPaths.McpTools, mcpTools);
            }
            else
            {
                Debug.Log("[DDrive][Compat] DDrive.Editor.Mcp が無いため mcp-tools.txt は更新しません(Unity MCP 未導入)。");
            }

            AssetDatabase.Refresh();
            Debug.Log("[DDrive][Compat] スナップショットを更新しました(" + CompatSnapshotPaths.Root + ")。" +
                "差分が意図したもの(MINOR=追加のみ / MAJOR=削除・変更、docs/42 §5.12)か確認し、" +
                "CHANGELOG.md の [Unreleased] 互換性節に追記してください。");
        }

        // null = Mcp アセンブリが無い。
        internal static string BuildMcpToolsSnapshot()
        {
            var type = System.Type.GetType("DDrive.Editor.Mcp.McpToolsSnapshotBuilder, DDrive.Editor.Mcp");
            var method = type?.GetMethod("Build", System.Type.EmptyTypes);
            return method?.Invoke(null, null) as string;
        }

        private static void WriteIfChanged(string path, string content)
        {
            var normalized = Normalize(content);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, normalized);
        }

        // LF 統一。Windows チェックアウト(CRLF)でも常に LF で書き出す([10_workflow.md] の慣習に合わせる)。
        internal static string Normalize(string content) => Regex.Replace(content ?? string.Empty, "\r\n?", "\n");
    }
}
