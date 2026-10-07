using System;
using System.Collections.Generic;
using DDrive.Foundation.Validation;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §4.1 / §5.3 MCP-2(2026-10-07) — 直近の Validation 実行の「要約」を Editor セッション内に覚えておく。
    //
    // ddrive_status は 1 回で状態が揃うことが価値なので、全 Validator を走らせる重い処理は呼ばない。
    // 代わりに ddrive_validate(MCP-5)や CI.RunValidation の呼び出し側が Record で件数を書き込み、
    // ddrive_status はここから読むだけにする。保存先は SessionState(ドメインリロードをまたぎ、Editor 終了で消える)。
    public static class McpValidationCache
    {
        private const string SessionKey = "DDrive.Mcp.ValidationSummary";

        // 件数を数えて保存する。Severity ごとに Error / Warning / Info へ振り分ける。
        public static JObject Record(IReadOnlyList<ValidationReport> reports)
        {
            var errors = 0;
            var warnings = 0;
            var infos = 0;
            if (reports != null)
            {
                for (var i = 0; i < reports.Count; i++)
                {
                    switch (reports[i].Result.Severity)
                    {
                        case ValidationSeverity.Error:
                            errors++;
                            break;
                        case ValidationSeverity.Warning:
                            warnings++;
                            break;
                        default:
                            infos++;
                            break;
                    }
                }
            }

            var summary = new JObject
            {
                ["errors"] = errors,
                ["warnings"] = warnings,
                ["infos"] = infos,
                ["at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            };
            try
            {
                SessionState.SetString(SessionKey, summary.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception)
            {
                // SessionState が使えない状況では保存しない(次回 TryGet が false になるだけ)。
            }

            return summary;
        }

        // 保存済みの要約を返す。無ければ / 壊れていれば false(例外にしない)。
        public static bool TryGet(out JObject summary)
        {
            summary = null;
            try
            {
                var raw = SessionState.GetString(SessionKey, string.Empty);
                if (string.IsNullOrEmpty(raw))
                {
                    return false;
                }

                summary = McpJson.Parse(raw);
                return true;
            }
            catch (Exception)
            {
                summary = null;
                return false;
            }
        }

        public static void Clear()
        {
            try
            {
                SessionState.EraseString(SessionKey);
            }
            catch (Exception)
            {
                // 消せなくても致命ではない。
            }
        }
    }
}
