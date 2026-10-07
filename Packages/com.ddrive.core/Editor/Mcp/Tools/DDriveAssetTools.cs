using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Validation;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Core.Attributes;

namespace DDrive.Editor.Mcp.Tools
{
    // [1002_ddrive_mcp.md] §4.2 / §4.5 / §5.2 / §5.3 MCP-3(2026-10-07) — ddrive_asset_list / get / create / set。
    // ツールはアダプタに徹する。作成は AssetCreationService.Create(カタログ + Addressables 登録まで 1 回で行う)、
    // 欄の読み書きは SerializedFieldIo、欄の表は FieldTables、検査は DataValidationRunner を呼ぶだけ。
    //  - ID は JSON では 10 進文字列(入力は 10 進 / 0x 16 進の文字列か JSON の整数)
    //  - set は「全欄を検証してから書く」: SerializedObject に全部書き込めたときだけ ApplyModifiedProperties する
    //  - 書き込みは必ず ChangeNote の先頭に "[mcp] " を付ける(Q-7)。Version / UpdatedAt は VersionStampProcessor が通常どおり付ける
    public static class DDriveAssetTools
    {
        public const string McpNotePrefix = "[mcp] ";
        public const string UndoName = "D-Drive MCP: Data の変更";

        private const string TypeArgText = "種別名(Se,Bgm,Vfx,Anim,Anim2D,Material,Texture,Canvas,Prefab,Presentation,Shake,Haptics,UiTween,Model,Anchor,AnchorGroup,ControlSkin,Cutscene)";

        // ── ddrive_asset_list ──

        [McpTool(
            "ddrive_asset_list",
            "種別ごとの Data 一覧。fields で返す欄を選ぶ(既定 id,name,category)",
            Idempotency = McpIdempotency.Safe,
            Group = "authoring")]
        public static JObject List(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("category", "カテゴリで絞る(完全一致 or 配下)")]
            string category = null,
            [McpArg("query", "表示名・アセット名の部分一致(大小無視)")]
            string query = null,
            [McpArg("fields", "カンマ区切り(id,name,category,path と欄名)。* で全欄")]
            string fields = null,
            [McpArg("cursor", "続き(返り値の next をそのまま渡す)")]
            string cursor = null,
            [McpArg("limit", "最大件数(既定 50、最大 200)")]
            int limit = 0,
            [McpArg("max_chars", "返り値の最大文字数(既定 4000)")]
            int max_chars = 0)
        {
            return McpGuard.Run(() =>
            {
                var entry = FieldTables.RequireType(type);
                var assets = FindAll(entry);
                var matched = new List<AssetDataBase>();
                foreach (var asset in assets)
                {
                    if (MatchesCategory(asset, category) && MatchesQuery(asset, query))
                    {
                        matched.Add(asset);
                    }
                }

                var columns = ParseListColumns(fields);
                var head = new JObject { ["total"] = matched.Count };
                return ItemPaging.Fit(head, matched, cursor, limit, max_chars, asset => MapListRow(asset, columns));
            });
        }

        private static bool MatchesCategory(AssetDataBase asset, string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return true;
            }

            var wanted = category.Trim().TrimEnd('/');
            var actual = asset.Category ?? string.Empty;
            return string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase)
                || actual.StartsWith(wanted + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesQuery(AssetDataBase asset, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var q = query.Trim();
            return Contains(asset.DisplayName, q) || Contains(asset.name, q);
        }

        private static bool Contains(string text, string part)
            => !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        private sealed class ListColumns
        {
            public bool All;
            public List<string> Names = new List<string>();
        }

        private static ListColumns ParseListColumns(string fields)
        {
            var columns = new ListColumns();
            if (string.IsNullOrWhiteSpace(fields))
            {
                columns.Names.AddRange(new[] { "id", "name", "category" });
                return columns;
            }

            foreach (var raw in fields.Split(','))
            {
                var name = raw.Trim();
                if (name.Length == 0 || columns.Names.Contains(name))
                {
                    continue;
                }

                if (name == "*")
                {
                    columns.All = true;
                    continue;
                }

                columns.Names.Add(name);
            }

            if (columns.Names.Count == 0 && !columns.All)
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, "fields が空です");
            }

            return columns;
        }

        private static JObject MapListRow(AssetDataBase asset, ListColumns columns)
        {
            var row = new JObject();
            SerializedObject so = null;
            var names = columns.Names;
            if (columns.All)
            {
                so = new SerializedObject(asset);
                names = new List<string>(new[] { "id", "name", "category" });
                names.AddRange(SerializedFieldIo.TopLevelNames(so));
            }

            foreach (var name in names)
            {
                switch (name)
                {
                    case "id":
                        row["id"] = McpJson.FormatId(asset.Id);
                        break;
                    case "name":
                        row["name"] = DisplayOf(asset);
                        break;
                    case "category":
                        row["category"] = asset.Category ?? string.Empty;
                        break;
                    case "path":
                        row["path"] = AssetDatabase.GetAssetPath(asset);
                        break;
                    default:
                        so ??= new SerializedObject(asset);
                        row[name] = SerializedFieldIo.Read(so, name);
                        break;
                }
            }

            return row;
        }

        // ── ddrive_asset_get ──

        [McpTool(
            "ddrive_asset_get",
            "Data 1 件の欄の値と検査件数を返す。fields 省略=主要欄、*=全欄",
            Idempotency = McpIdempotency.Safe,
            Group = "authoring")]
        public static JObject Get(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("id", "ID(10 進文字列か 0x 16 進)。id か path のどちらか")]
            string id = null,
            [McpArg("path", "アセットパス(Assets/...)")]
            string path = null,
            [McpArg("fields", "カンマ区切りの欄名。* で全欄")]
            string fields = null,
            [McpArg("max_chars", "返り値の最大文字数(既定 4000)")]
            int max_chars = 0)
        {
            return McpGuard.Run(() =>
            {
                var entry = FieldTables.RequireType(type);
                var asset = Locate(entry, id, path);
                var so = new SerializedObject(asset);

                List<string> names;
                if (string.IsNullOrWhiteSpace(fields))
                {
                    names = new List<string>(FieldTables.DefaultFields(entry.Type, asset.GetType()));
                }
                else if (fields.Trim() == "*")
                {
                    names = SerializedFieldIo.TopLevelNames(so);
                }
                else
                {
                    names = SplitNames(fields);
                }

                var values = new JObject();
                var unknown = new List<string>();
                foreach (var name in names)
                {
                    var prop = so.FindProperty(name);
                    if (prop == null)
                    {
                        unknown.Add(name);
                        continue;
                    }

                    values[name] = SerializedFieldIo.Read(prop);
                }

                if (unknown.Count > 0)
                {
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"{asset.GetType().Name} に欄 {string.Join(",", unknown)} がありません");
                }

                var result = new JObject
                {
                    ["id"] = McpJson.FormatId(asset.Id),
                    ["name"] = DisplayOf(asset),
                    ["path"] = AssetDatabase.GetAssetPath(asset),
                    ["fields"] = values,
                    ["validation"] = ValidationCounts(asset),
                };

                return FitFields(result, values, max_chars);
            });
        }

        private const int OmittedReserve = 190;
        private const int OmittedNamesMaxChars = 150;

        // fields が max_chars に収まるまで、末尾の欄を丸ごと落とす(JSON を途中で切らない)。
        // 落とした欄名は omitted(カンマ区切り、150 文字まで)に出す。落とした先は fields に狭めて読み直せる。
        private static JObject FitFields(JObject result, JObject values, int maxChars)
        {
            if (maxChars <= 0)
            {
                maxChars = McpGuard.DefaultMaxChars;
            }

            var omitted = new List<string>();
            while (values.Count > 0 && McpJson.Compact(result).Length + OmittedReserve > maxChars)
            {
                JProperty tail = null;
                foreach (var prop in values.Properties())
                {
                    tail = prop;
                }

                omitted.Insert(0, tail.Name);
                values.Remove(tail.Name);
            }

            if (omitted.Count > 0)
            {
                var names = string.Join(",", omitted);
                result["truncated"] = true;
                result["omitted"] = names.Length <= OmittedNamesMaxChars ? names : names.Substring(0, OmittedNamesMaxChars) + "…";
            }

            return result;
        }

        // ── ddrive_asset_create ──

        [McpTool(
            "ddrive_asset_create",
            "Data を新規作成(カタログ+Addressables 登録・検査まで 1 回)。preview=true で作らず確認",
            Group = "authoring")]
        public static JObject Create(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("name", "表示名(DisplayName)", Required = true)]
            string name,
            [McpArg("category", "カテゴリ(例 Player/Attack。フォルダ階層になる)")]
            string category = null,
            [McpArg("identifier", "PascalCase の識別子。省略は name から生成")]
            string identifier = null,
            [McpArg("data_class", "ControlSkin のみ: ButtonSkinData / SliderSkinData")]
            string data_class = null,
            [McpArg("fields", "作成時に設定する欄 {欄名: 値}")]
            JObject fields = null,
            [McpArg("preview", "true なら何も作らず wouldCreate(パスと識別子)だけ返す")]
            bool preview = false)
        {
            return CreateIn(AssetCreationService.DefaultGameDataRoot, type, name, category, identifier, data_class, fields, preview);
        }

        // gameDataRoot を指定できる本体(テストが一時フォルダで実作成を検証するため。MCP には出さない)。
        public static JObject CreateIn(
            string gameDataRoot, string type, string name, string category, string identifier,
            string dataClass, JObject fields, bool preview)
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();

                var entry = FieldTables.RequireType(type);
                var dataType = FieldTables.ResolveDataClass(entry, dataClass);
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, "name が空です");
                }

                category = string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim();
                string ident;
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    ident = AssetNamingService.ToIdentifier(name, entry.Type.ToString());
                }
                else
                {
                    ident = identifier.Trim();
                }

                if (!AssetNamingService.IsValidIdentifier(ident))
                {
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"identifier '{ident}' は PascalCase の英数字(先頭大文字)で指定してください");
                }

                var root = AssetCreationService.ResolveGameDataRoot(gameDataRoot);
                var folder = $"{root}/{AssetNamingService.GetTargetFolder(entry.Type, category)}";
                var path = $"{folder}/{AssetNamingService.BuildFileName(entry.Type, category, ident)}.asset";
                if (AssetDatabase.LoadMainAssetAtPath(path) != null || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)))
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"同じ識別子のアセットが既にあります: {path}");
                }

                // 作る前に、fields が全部書けることを使い捨てのインスタンスで確かめる(書けないなら何も作らない)。
                if (fields != null && fields.Count > 0)
                {
                    var probe = ScriptableObject.CreateInstance(dataType);
                    try
                    {
                        ApplyFields(new SerializedObject(probe), fields);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(probe);
                    }
                }

                if (preview)
                {
                    return McpJson.Obj(("wouldCreate", path), ("identifier", ident));
                }

                var created = AssetCreationService.Create(
                    dataType, entry.Type, name.Trim(), category, ident,
                    configure: asset =>
                    {
                        var so = new SerializedObject(asset);
                        if (fields != null && fields.Count > 0)
                        {
                            ApplyFields(so, fields);
                        }

                        var note = so.FindProperty("ChangeNote");
                        note.stringValue = fields != null && fields["ChangeNote"] != null
                            ? WithMcpPrefix(note.stringValue)
                            : McpNotePrefix + "作成";
                        so.ApplyModifiedPropertiesWithoutUndo();
                    },
                    gameDataRoot: gameDataRoot);

                if (created == null)
                {
                    throw new McpToolError(McpGuard.CodeException, "作成に失敗しました(コンソールのエラーを確認してください)");
                }

                // Create がカタログ + Addressables(同じ address)の登録とディスクへの保存まで済ませる。ここでは二重に登録しない。
                var addressable = AddressablesSync.IsRegistered(created);
                return McpJson.Obj(
                    ("id", McpJson.FormatId(created.Id)),
                    ("path", AssetDatabase.GetAssetPath(created)),
                    ("addressable", McpJson.Keep(addressable)),
                    ("validation", ValidationCounts(created)));
            });
        }

        // ── ddrive_asset_set ──

        [McpTool(
            "ddrive_asset_set",
            "Data の欄を書き換える(Undo 可)。読み取り専用欄は拒否。preview=true で差分だけ",
            UndoGroup = UndoName,
            Group = "authoring")]
        public static JObject Set(
            [McpArg("type", TypeArgText, Required = true)]
            string type,
            [McpArg("id", "ID(10 進文字列か 0x 16 進)", Required = true)]
            string id,
            [McpArg("fields", "{欄名: 値}。入れ子は Common.Color のドット区切り。配列は全体置換", Required = true)]
            JObject fields,
            [McpArg("preview", "true なら何も書かず wouldChange(from/to)だけ返す")]
            bool preview = false)
        {
            return McpGuard.Run(() =>
            {
                McpGuard.EnsureCanWrite();

                var entry = FieldTables.RequireType(type);
                if (fields == null || fields.Count == 0)
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, "fields が空です({欄名: 値} を渡してください)");
                }

                var asset = Locate(entry, id, null);
                var so = new SerializedObject(asset);

                // 1) 全欄の名前を先に検査する(読み取り専用・書式)。1 つでも不正なら何も書かない。
                foreach (var kv in fields)
                {
                    CheckFieldName(kv.Key);
                    if (so.FindProperty(kv.Key) == null)
                    {
                        throw new McpToolError(
                            McpGuard.CodeInvalidParams,
                            $"{asset.GetType().Name} に欄 '{kv.Key}' がありません");
                    }
                }

                // 2) SerializedObject に全部書く(ここで型が合わなければ例外 = まだ Apply していないので何も変わらない)。
                var before = new JObject();
                foreach (var kv in fields)
                {
                    before[kv.Key] = SerializedFieldIo.Read(so, kv.Key);
                }

                foreach (var kv in fields)
                {
                    SerializedFieldIo.Write(so.FindProperty(kv.Key), kv.Value);
                }

                var changedNames = new List<string>();
                foreach (var kv in fields)
                {
                    if (!JToken.DeepEquals(before[kv.Key], SerializedFieldIo.Read(so, kv.Key)))
                    {
                        changedNames.Add(kv.Key);
                    }
                }

                // 3) 変わる欄があるときだけ ChangeNote に "[mcp] " を付ける(変化なしの呼び出しで版を進めない)。
                if (changedNames.Count > 0)
                {
                    var note = so.FindProperty("ChangeNote");
                    var current = note.stringValue;
                    note.stringValue = string.IsNullOrEmpty(current) && fields["ChangeNote"] == null
                        ? McpNotePrefix + "変更: " + string.Join(", ", changedNames)
                        : WithMcpPrefix(current);
                }

                var changed = new JArray();
                foreach (var name in changedNames)
                {
                    changed.Add(new JObject
                    {
                        ["field"] = name,
                        ["from"] = SerializedFieldIo.Describe(before[name]),
                        ["to"] = SerializedFieldIo.Describe(SerializedFieldIo.Read(so, name)),
                    });
                }

                if (preview)
                {
                    return McpJson.Obj(("wouldChange", McpJson.Keep(changed)));
                }

                if (changedNames.Count > 0)
                {
                    Undo.RecordObject(asset, UndoName);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(asset);
                    DDriveAssetSave.SaveDirty(asset);
                }

                return McpJson.Obj(
                    ("changed", McpJson.Keep(changed)),
                    ("validation", ValidationCounts(asset)));
            });
        }

        // ── 共通 ──

        // ChangeNote の先頭に "[mcp] " を付ける。既に付いていれば何もしない。
        public static string WithMcpPrefix(string note)
        {
            note ??= string.Empty;
            return note.StartsWith(McpNotePrefix, StringComparison.Ordinal) ? note : McpNotePrefix + note;
        }

        // 欄名の検査: 読み取り専用欄は read_only_field、要素単位(Tags[0] / Tags.Array.data[0])は invalid_params。
        public static void CheckFieldName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, "欄名が空です");
            }

            McpGuard.EnsureFieldWritable(name);
            if (name.IndexOf('[') >= 0 || name.Contains(".Array"))
            {
                throw new McpToolError(
                    McpGuard.CodeInvalidParams,
                    $"'{name}' は要素単位の更新に対応していません(配列は欄名で全体を置き換えてください)");
            }
        }

        // 全欄の名前を検査してから SerializedObject に書く(Apply はしない)。create の probe / configure 共用。
        private static void ApplyFields(SerializedObject so, JObject fields)
        {
            foreach (var kv in fields)
            {
                CheckFieldName(kv.Key);
                if (so.FindProperty(kv.Key) == null)
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"欄 '{kv.Key}' がありません");
                }
            }

            foreach (var kv in fields)
            {
                SerializedFieldIo.Write(so.FindProperty(kv.Key), kv.Value);
            }
        }

        private static List<string> SplitNames(string fields)
        {
            var names = new List<string>();
            foreach (var raw in fields.Split(','))
            {
                var name = raw.Trim();
                if (name.Length > 0 && !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, "fields が空です");
            }

            return names;
        }

        public static string DisplayOf(AssetDataBase asset)
            => string.IsNullOrEmpty(asset.DisplayName) ? asset.name : asset.DisplayName;

        public static JObject ValidationCounts(AssetDataBase asset)
        {
            var errors = 0;
            var warnings = 0;
            foreach (var result in DataValidationRunner.Run(asset, false))
            {
                if (result.Severity == ValidationSeverity.Error)
                {
                    errors++;
                }
                else if (result.Severity == ValidationSeverity.Warning)
                {
                    warnings++;
                }
            }

            return new JObject { ["errors"] = errors, ["warnings"] = warnings };
        }

        // 種別の Data を全部(パス順)。Anim2D は AnimData の派生なので、t:AnimData が Anim2D も拾う → 種別で絞り直す。
        public static List<AssetDataBase> FindAll(FieldTables.Entry entry)
        {
            var result = new List<AssetDataBase>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cls in entry.DataClasses)
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + cls.Name))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(path))
                    {
                        continue;
                    }

                    var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                    if (asset != null && ValidationSummary.ResolveType(asset) == entry.Type)
                    {
                        result.Add(asset);
                    }
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            return result;
        }

        public static AssetDataBase Locate(FieldTables.Entry entry, string id, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path.Trim());
                if (asset == null)
                {
                    throw new McpToolError(McpGuard.CodeInvalidParams, $"path '{path}' に Data がありません");
                }

                if (ValidationSummary.ResolveType(asset) != entry.Type)
                {
                    throw new McpToolError(
                        McpGuard.CodeInvalidParams,
                        $"path '{path}' は {ValidationSummary.ResolveType(asset)} です({entry.Type} ではありません)");
                }

                return asset;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, "id か path が必要です");
            }

            if (!McpJson.TryParseId(id, out var parsed))
            {
                throw new McpToolError(McpGuard.CodeInvalidParams, $"id '{id}' が不正です(10 進または 0x 16 進)");
            }

            foreach (var asset in FindAll(entry))
            {
                if (asset.Id == parsed)
                {
                    return asset;
                }
            }

            throw new McpToolError(McpGuard.CodeInvalidParams, $"{entry.Type} に id {id} の Data がありません");
        }
    }
}
