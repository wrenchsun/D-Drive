using System;
using System.Collections.Generic;
using DDrive.Editor.Settings;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §3 / §4.3 / §5.2 MCP-1(2026-10-07) — ddrive_* ツール共通のガード。
    //
    // ツールはアダプタに徹する(ロジックは持たない)ので、「Play Mode 中の書き込み拒否」「書き込み設定が OFF なら拒否」
    // 「読み取り専用欄の拒否」「返り値の切り詰め」「例外を {error:{code,msg}} の 1 行に畳む」をここに集める。
    // 例外でツールを落とさない(CLAUDE.md §0-4): ガードは McpToolError を投げ、Run が畳んで通常の返り値として返す。
    //
    // エラーコード: play_mode / write_disabled / read_only_field / invalid_params / exception。
    // (truncated はエラーではなく、返り値に付く `truncated:true` の印。Truncate を使うツールが付ける)
    public static class McpGuard
    {
        public const int DefaultMaxChars = 4000;

        // MCP-13(2026-10-07): max_chars の上限。isuzu v4.3.0 から、返り値が [McpTool] の MaxResultSizeChars を超えると
        // 切り詰めずに isError になる。AI が max_chars を際限なく上げても isError にならないよう、ここで丸め、
        // max_chars を受けるツールの MaxResultSizeChars を同じ値にする(= McpTool 属性側は定数 MaxMaxChars を参照)。
        public const int MaxMaxChars = 16000;

        // max_chars の正規化。0 以下 = 既定値、MaxMaxChars 超 = MaxMaxChars。
        public static int ClampMaxChars(int maxChars)
        {
            if (maxChars <= 0)
            {
                return DefaultMaxChars;
            }

            return maxChars > MaxMaxChars ? MaxMaxChars : maxChars;
        }

        public const string CodePlayMode = "play_mode";
        public const string CodeWriteDisabled = "write_disabled";
        public const string CodeReadOnlyField = "read_only_field";
        public const string CodeInvalidParams = "invalid_params";
        public const string CodeException = "exception";

        private const int ErrorMessageMaxChars = 200;

        // AssetDataBase(Foundation/Data/AssetDataBase.cs)の SerializedProperty パスそのもの(m_ 等の接頭辞は付かない)。
        // 書き込みツールは必ずこの一覧で拒否する(ID の付け替え・版の巻き戻し等を AI に許さない)。
        private static readonly string[] ReadOnlyFieldNames =
        {
            "Id", "SchemaVersion", "ImportSourceGuid", "Version", "UpdatedAt", "Icon",
        };

        public static IReadOnlyList<string> ReadOnlyFields => ReadOnlyFieldNames;

        // 読み取りツールは Play Mode 中でも許可する(書き込みだけ拒否)。
        public static bool IsPlaying => EditorApplication.isPlayingOrWillChangePlaymode;

        public static void EnsureNotPlaying()
        {
            if (IsPlaying)
            {
                throw new McpToolError(CodePlayMode, "Play Mode 中は書き込みできません。停止してから実行してください");
            }
        }

        public static void EnsureWriteAllowed()
        {
            if (!DDriveProjectSettings.instance.McpAllowWrite)
            {
                throw new McpToolError(
                    CodeWriteDisabled,
                    "MCP の書き込みツールは無効です(Project Settings > D-Drive > MCP で有効にできます)");
            }
        }

        // 書き込みツール共通の入口。設定 → Play Mode の順に検査する。
        public static void EnsureCanWrite()
        {
            EnsureWriteAllowed();
            EnsureNotPlaying();
        }

        // `Id` / `Tags.Array.data[0]` / `Events[0].Name` のような SerializedProperty パスの先頭の欄名で判定する。
        public static bool IsReadOnlyField(string serializedPath)
        {
            if (string.IsNullOrEmpty(serializedPath))
            {
                return false;
            }

            var end = serializedPath.IndexOfAny(new[] { '.', '[' });
            var head = end < 0 ? serializedPath : serializedPath.Substring(0, end);
            for (var i = 0; i < ReadOnlyFieldNames.Length; i++)
            {
                if (string.Equals(head, ReadOnlyFieldNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static void EnsureFieldWritable(string serializedPath)
        {
            if (IsReadOnlyField(serializedPath))
            {
                throw new McpToolError(CodeReadOnlyField, $"'{serializedPath}' は読み取り専用です");
            }
        }

        // maxChars 以下なら無加工。超えたら先頭 maxChars 文字に切り、truncated=true を返す(黙って切らない。呼び出し側が印を付ける)。
        // maxChars <= 0 は既定値(4000)、MaxMaxChars(16000)超は MaxMaxChars として扱う。
        public static (string text, bool truncated) Truncate(string json, int maxChars = DefaultMaxChars)
        {
            maxChars = ClampMaxChars(maxChars);

            if (json == null)
            {
                return (string.Empty, false);
            }

            return json.Length <= maxChars ? (json, false) : (json.Substring(0, maxChars), true);
        }

        // ツール本体を包む。McpToolError は {error:{code,msg}} に、その他の例外は code=exception に畳む(スタックトレースは返さない)。
        public static JObject Run(Func<JObject> body)
        {
            try
            {
                return body() ?? new JObject();
            }
            catch (McpToolError error)
            {
                return ErrorResult(error.Code, error.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(CodeException, ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static JObject ErrorResult(string code, string message)
        {
            return new JObject
            {
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["msg"] = Cap(message),
                },
            };
        }

        private static string Cap(string message)
        {
            message ??= string.Empty;
            return message.Length <= ErrorMessageMaxChars ? message : message.Substring(0, ErrorMessageMaxChars);
        }
    }

    // ガードやツール本体が「想定内の拒否」を表すための例外。McpGuard.Run が {error:{code,msg}} に畳む。
    public sealed class McpToolError : Exception
    {
        public string Code { get; }

        public McpToolError(string code, string message)
            : base(message)
        {
            Code = code;
        }
    }
}
