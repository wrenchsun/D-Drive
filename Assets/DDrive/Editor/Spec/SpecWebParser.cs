using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Manual;
using DDrive.Foundation.Identity;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §3.1/§3.2/§5.1 — Web API(GAS)の JSON 応答を SpecAssetRow/SpecTuningRow/
    // SpecTuningTableRow に変換する。5-13 の SpecSheetParser(CSV)を置き換える。
    // AssetDatabase に依存しない純ロジック(JSON 文字列を直接注入してテストする、W-9 の要件)。
    // 「衝突」(種別+識別子の重複・識別子が PascalCase でない・未知の種別)の検出は
    // SpecSheetParser と同じ考え方を踏襲する(Web 側の assets.create/update でも同種の検証を
    // 行っているが、D-Drive 側は取得した JSON を信用せず自前でも検証する。CLAUDE.md §0-4)。
    public static class SpecWebParser
    {
        // assets.list の応答({ items: [...] , ok:true, status:200 })。
        // humanAppUrl を渡すと、各行の SpecLink を「人向け SPA の URL + ?page=order&id=<種別::識別子>」
        // で組み立てる(docs/32 §6/§10.8: 5-14 の SpecUrl を Web アプリのアセット詳細ページの URL に
        // 変える。2026-09-14: PR #50(O-13)で Web 側が実装した `?page=order&id=...` ディープリンクに
        // 合わせた。旧 `#/assets/<id>` ハッシュ形式は Web の SPA が location.hash に依存しないため
        // 機能しなかった)。
        // 省略/null なら SpecLink は空のままにする(SpecDiffService/SpecSyncService は
        // 「シート側が空なら既存の SpecUrl を消さない」ため、安全側に倒れる)。
        public static SpecParseResult<SpecAssetRow> ParseAssets(string json, string humanAppUrl = null)
        {
            var result = new SpecParseResult<SpecAssetRow>();
            if (!TryParseEnvelope(json, result.Issues, out var root))
            {
                return result;
            }

            var items = root["items"] as JArray;
            if (items == null)
            {
                result.Issues.Add(new SpecIssue(0, "assets.list の応答に items(配列)がありません。"));
                return result;
            }

            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < items.Count; i++)
            {
                var rowNumber = i + 1;
                if (items[i] is not JObject item)
                {
                    result.Issues.Add(new SpecIssue(rowNumber, "items の要素がオブジェクトではありません。"));
                    continue;
                }

                if (item.Value<bool?>("archived") == true)
                {
                    continue; // 論理削除済み(Web §9-要判断: archived フラグ)は同期対象外
                }

                var typeCell = (string)item["assetType"] ?? string.Empty;
                var identifier = (string)item["identifier"] ?? string.Empty;

                if (string.IsNullOrEmpty(typeCell) || string.IsNullOrEmpty(identifier))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, "種別・識別子は必須です(空欄)。"));
                    continue;
                }

                if (!Enum.TryParse<AssetType>(typeCell, ignoreCase: true, out var assetType)
                    || assetType == AssetType.None
                    || !Enum.IsDefined(typeof(AssetType), assetType))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"種別 '{typeCell}' が AssetType に見つかりません。"));
                    continue;
                }

                if (!AssetNamingService.IsValidIdentifier(identifier))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"識別子 '{identifier}' は PascalCase(先頭大文字・英数字のみ)にしてください。"));
                    continue;
                }

                var key = assetType + "::" + identifier;
                if (seenKeys.TryGetValue(key, out var firstRowNumber))
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"種別+識別子が {firstRowNumber} 行目と重複しています。"));
                    continue;
                }

                seenKeys[key] = rowNumber;

                var id = (string)item["id"] ?? key;
                result.Rows.Add(new SpecAssetRow
                {
                    RowNumber = rowNumber,
                    Type = assetType,
                    Category = (string)item["category"] ?? string.Empty,
                    Identifier = identifier,
                    DisplayName = (string)item["displayName"] ?? string.Empty,
                    // 状態は O-1 以降 3 値(発注済 / 納品済 / インポート済)。
                    // (Tools/SpecWeb/src/Assets.js の SPEC_WEB_ASSET_STATUSES が正。旧 4 値
                    //  (未着手/仮/本番/保留)は GAS 側の specWebNormalizeLegacyOrderItem_ が
                    //  読み込み時に 3 値へ変換して返すため、D-Drive 側は変換しない)。
                    Status = (string)item["status"] ?? string.Empty,
                    Assignee = ReadContractor(item),
                    SpecLink = BuildSpecLink(humanAppUrl, id),
                    Note = ReadReferenceMd(item),
                });
            }

            return result;
        }

        // tuningScalarList の応答({ items: { "<key>": {...} }, ok:true, status:200 })。
        public static SpecParseResult<SpecTuningRow> ParseTuningScalars(string json)
        {
            var result = new SpecParseResult<SpecTuningRow>();
            if (!TryParseEnvelope(json, result.Issues, out var root))
            {
                return result;
            }

            var items = root["items"] as JObject;
            if (items == null)
            {
                result.Issues.Add(new SpecIssue(0, "tuningScalarList の応答に items(オブジェクト)がありません。"));
                return result;
            }

            var rowNumber = 0;
            foreach (var property in items.Properties())
            {
                rowNumber++;
                var key = property.Name;
                if (property.Value is not JObject entry)
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"調整値 '{key}' の値がオブジェクトではありません。"));
                    continue;
                }

                if ((string)entry["kind"] != "scalar")
                {
                    continue; // テーブル型はここでは扱わない(ParseTuningTables)
                }

                var valueType = (string)entry["valueType"] ?? string.Empty;
                var enumOptions = ToStringArray(entry["enumOptions"] as JArray);

                result.Rows.Add(new SpecTuningRow
                {
                    RowNumber = rowNumber,
                    Key = key,
                    RawValue = ValueToRawString(entry["value"]),
                    RawType = valueType,
                    RawMin = ValueToRawString(entry["min"]),
                    RawMax = ValueToRawString(entry["max"]),
                    Unit = (string)entry["unit"] ?? string.Empty,
                    Description = (string)entry["description"] ?? string.Empty,
                    RawEnumOptions = enumOptions,
                });
            }

            return result;
        }

        // tuningTableList の応答({ items: { "<key>": {...columns,rows,locked} }, ok:true, status:200 })。
        // 列・行の変換は SpecSyncService.ApplyTuningTable(TuningTableEntry への変換の 1 か所)に任せ、
        // ここでは JObject をそのまま運ぶだけにする(SpecTuningTableRow のコメント参照)。
        public static SpecParseResult<SpecTuningTableRow> ParseTuningTables(string json)
        {
            var result = new SpecParseResult<SpecTuningTableRow>();
            if (!TryParseEnvelope(json, result.Issues, out var root))
            {
                return result;
            }

            var items = root["items"] as JObject;
            if (items == null)
            {
                result.Issues.Add(new SpecIssue(0, "tuningTableList の応答に items(オブジェクト)がありません。"));
                return result;
            }

            var rowNumber = 0;
            foreach (var property in items.Properties())
            {
                rowNumber++;
                var key = property.Name;
                if (property.Value is not JObject entry)
                {
                    result.Issues.Add(new SpecIssue(rowNumber, $"調整値テーブル '{key}' の値がオブジェクトではありません。"));
                    continue;
                }

                result.Rows.Add(new SpecTuningTableRow
                {
                    RowNumber = rowNumber,
                    Key = key,
                    Raw = entry,
                });
            }

            return result;
        }

        private static bool TryParseEnvelope(string json, List<SpecIssue> issues, out JObject root)
        {
            root = null;
            if (string.IsNullOrEmpty(json))
            {
                issues.Add(new SpecIssue(0, "応答が空です。"));
                return false;
            }

            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (Exception e)
            {
                issues.Add(new SpecIssue(0, $"応答の JSON を解釈できません: {e.Message}"));
                return false;
            }

            if (token is not JObject obj)
            {
                issues.Add(new SpecIssue(0, "応答が JSON オブジェクトではありません。"));
                return false;
            }

            if (obj.Value<bool?>("ok") == false)
            {
                var error = (string)obj["error"] ?? "不明なエラー";
                var status = obj.Value<int?>("status") ?? 0;
                issues.Add(new SpecIssue(0, $"Web API がエラーを返しました(status={status}): {error}"));
                return false;
            }

            root = obj;
            return true;
        }

        // Web 側(Tools/SpecWeb/src/Code.js の resolveInitialScreen_、html/OrderLinkLogic.html の
        // buildOrderUrl)と同じ `?page=order&id=<種別::識別子>` 形式(assetId は assets.list の
        // "id" フィールド=種別::識別子。§10 参照)。末尾スラッシュの扱いは ManualUrlBuilder.BuildWebUrl と
        // 揃えるため、共通の AppendQuery(トリムしない)に寄せる。
        private static string BuildSpecLink(string humanAppUrl, string assetId)
        {
            if (string.IsNullOrEmpty(humanAppUrl) || string.IsNullOrEmpty(assetId))
            {
                return string.Empty;
            }

            return ManualUrlBuilder.AppendQuery(humanAppUrl, "page=order&id=" + Uri.EscapeDataString(assetId));
        }

        // ── 発注スキーマ(O-1)のフィールド名。旧名は移行前のデータのためだけに残す ──
        // [32] §10.2.1 — 旧 `assignee`(担当)は `orderer`(発注者)/`contractor`(受注者)に分割され、
        // 「実際に作る人」= `contractor` が D-Drive の Assignee に対応する。旧 `note` は
        // `referenceMd`(Markdown)に改称された。
        // 2026-09-17([41] P1-7): D-Drive 側が旧名 `assignee` / `note` のままだったため、O-1 以降に
        // 作られた発注(旧キーを持たない)では常に空文字として読まれ、「担当 / 備考が変わった」と
        // 誤判定 → 適用で既存の Assignee / Description を空で上書きしていた。
        // 旧名へのフォールバックを残すのは、GAS 側の物理移行(migrateLegacyOrdersToNewSchema)を
        // まだ実行していないデータが残っている可能性があるため(Migration.js の
        // specWebNormalizeLegacyOrderItem_ は読み込み時に新名で返すが、D-Drive 側は取得した JSON を
        // 信用せず自前でも同じ規則を持つ。CLAUDE.md §0-4)。
        private static string ReadContractor(JObject item)
        {
            // GAS の specWebNormalizeLegacyOrderItem_ と同じ規則: contractor が空なら assignee を使う。
            var contractor = ReadString(item, "contractor");
            return contractor.Length > 0 ? contractor : ReadString(item, "assignee");
        }

        private static string ReadReferenceMd(JObject item)
        {
            // GAS は「referenceMd のキーが無いときだけ note を使う」が、D-Drive 側は
            // 「空(キー無し / null / 空文字)なら旧 note を見る」に緩めてある(既存値を空で
            // 潰さない方向に倒す。SpecDiffService/SpecSyncService の「空は変更なし」と同じ向き)。
            var token = item["referenceMd"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return ReadString(item, "note");
            }

            var value = (string)token ?? string.Empty;
            return value.Length > 0 ? value : ReadString(item, "note");
        }

        private static string ReadString(JObject item, string key) => (string)item[key] ?? string.Empty;

        private static string[] ToStringArray(JArray array)
        {
            if (array == null || array.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                result[i] = (string)array[i] ?? string.Empty;
            }

            return result;
        }

        // JToken(数値/文字列/真偽値/null)を SpecTuningRow の Raw* フィールド(文字列)に変換する。
        // 既存の SpecSyncService.TryBuildEntry は CultureInfo.InvariantCulture で float.TryParse する前提のため、
        // 数値は不変カルチャで文字列化する。
        private static string ValueToRawString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            switch (token.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer:
                    return token.ToObject<double>().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case JTokenType.Boolean:
                    return token.ToObject<bool>() ? "true" : "false";
                default:
                    return token.ToString();
            }
        }
    }
}
