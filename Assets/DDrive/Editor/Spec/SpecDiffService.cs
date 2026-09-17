using System;
using System.Collections.Generic;
using DDrive.Editor.Inspectors;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEditor;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.3 — 仕様書「アセット」タブの行と、既存の Data アセットを
    // 「種別+識別子」(SpecIdentifierCodec でファイル名から逆算)で結び付けて差分を出す。
    public sealed class SpecAssetChange
    {
        public SpecAssetRow Row;
        public AssetDataBase ExistingAsset; // 新規のときは null
        public readonly List<string> ChangedFields = new();
    }

    public sealed class SpecArchiveCandidate
    {
        public AssetDataBase Asset;
        public AssetType Type;
        public string Identifier;
    }

    public sealed class SpecDiffResult
    {
        public readonly List<SpecAssetChange> New = new();
        public readonly List<SpecAssetChange> Changed = new();
        public readonly List<SpecArchiveCandidate> Archived = new();
        public readonly List<SpecIssue> Conflicts = new();
    }

    public static class SpecDiffService
    {
        public static SpecDiffResult ComputeDiff(SpecParseResult<SpecAssetRow> parsed)
        {
            var result = new SpecDiffResult();
            result.Conflicts.AddRange(parsed.Issues);

            var existingByKey = BuildExistingIndex();
            var matchedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in parsed.Rows)
            {
                if (existingByKey.TryGetValue(row.Key, out var asset))
                {
                    matchedKeys.Add(row.Key);
                    var change = new SpecAssetChange { Row = row, ExistingAsset = asset };
                    CollectChangedFields(asset, row, change.ChangedFields);
                    if (change.ChangedFields.Count > 0)
                    {
                        result.Changed.Add(change);
                    }
                }
                else
                {
                    result.New.Add(new SpecAssetChange { Row = row, ExistingAsset = null });
                }
            }

            // Archive 候補: 一度でも仕様書と同期された(SpecUrl 設定済み)のに、今回のシートに無いもの。
            // 手動で個別に作った既存アセットが「仕様書に無い」だけで毎回 Archive 候補に出るのを避けるため
            // (docs/28 の「要判断」に記載)。
            foreach (var kv in existingByKey)
            {
                if (matchedKeys.Contains(kv.Key))
                {
                    continue;
                }

                var asset = kv.Value;
                if (string.IsNullOrEmpty(asset.SpecUrl))
                {
                    continue;
                }

                var separator = kv.Key.IndexOf("::", StringComparison.Ordinal);
                if (separator < 0 || !Enum.TryParse<AssetType>(kv.Key.Substring(0, separator), out var type))
                {
                    continue;
                }

                result.Archived.Add(new SpecArchiveCandidate
                {
                    Asset = asset,
                    Type = type,
                    Identifier = kv.Key.Substring(separator + 2),
                });
            }

            return result;
        }

        // シートが上書きしてよい項目だけを比較する([27] §4.3: 表示名・カテゴリ・状態・担当・備考・仕様リンク)。
        // デザイナーが作った中身(音源・カーブ・Prefab 等)には触れない。
        //
        // 2026-09-17([41] P1-7): **Web 側が空の項目は「未入力」として扱い、差分に含めない**
        // (= 既存値を空で消さない)。もともと「仕様リンク」だけに入っていた保護を、人が書く文字列
        // (表示名・状態・担当・備考)へ広げた防御。契約ずれ(P1-7: `assignee` / `note` を読んでいて
        // 常に空だった)や取得の部分失敗が起きたときに、同期のたびにデザイナーの入力が消えるという
        // 最悪の結果を避けるのが目的。空にしたいときは D-Drive 側(Inspector / 専用エディタ)で消す。
        // Web で発注を作るとき displayName は必須・status は必ず 3 値のどれかが入る
        // (`Tools/SpecWeb/src/Assets.js` の specWebValidateAssetFields_ / assets.create)ので、
        // 「Web 側で意図的に空にする」運用は元から存在しない。
        // カテゴリだけは空が正当な値(カテゴリ無し。GAS も必須にしていない)なので従来どおり比較する。
        // SpecSyncService.ApplyExtraFields / ApplyChanged と対称に保つこと(片方だけ直しても、
        // 別の項目が変わったときの適用で空が書き込まれてしまう)。
        private static void CollectChangedFields(AssetDataBase asset, SpecAssetRow row, List<string> changedFields)
        {
            if (!string.IsNullOrEmpty(row.DisplayName)
                && !string.Equals(asset.DisplayName ?? string.Empty, row.DisplayName, StringComparison.Ordinal))
            {
                changedFields.Add("表示名");
            }

            if (!string.Equals(asset.Category ?? string.Empty, row.Category, StringComparison.Ordinal))
            {
                changedFields.Add("カテゴリ");
            }

            if (!string.IsNullOrEmpty(row.Status)
                && !string.Equals(SpecStatusTag.GetCurrent(asset.Tags), row.Status, StringComparison.Ordinal))
            {
                changedFields.Add("状態");
            }

            if (!string.IsNullOrEmpty(row.Assignee)
                && !string.Equals(asset.Assignee ?? string.Empty, row.Assignee, StringComparison.Ordinal))
            {
                changedFields.Add("担当");
            }

            if (!string.IsNullOrEmpty(row.Note)
                && !string.Equals(asset.Description ?? string.Empty, row.Note, StringComparison.Ordinal))
            {
                changedFields.Add("備考");
            }

            if (!string.IsNullOrEmpty(row.SpecLink)
                && !string.Equals(asset.SpecUrl ?? string.Empty, row.SpecLink, StringComparison.Ordinal))
            {
                changedFields.Add("仕様リンク");
            }
        }

        // Type::Identifier → 既存 AssetDataBase の索引。AssetBrowserWindow.Refresh と同じ発見ロジック
        // (t:AssetDataBase を 1 回検索し、宣言済みの具象型と厳密一致するものだけを対象にする)。
        // public(InternalsVisibleTo 未設定のため、テスト asmdef から直接検証できるようにする。UiButton.cs 参照)。
        public static Dictionary<string, AssetDataBase> BuildExistingIndex()
        {
            var index = new Dictionary<string, AssetDataBase>(StringComparer.Ordinal);
            var definitionByDataType = new Dictionary<Type, AssetType>();
            foreach (var (dataType, assetType) in AssetIdLookup.GetAllDefinitions())
            {
                if (dataType.Namespace != null && dataType.Namespace.Contains("Tests"))
                {
                    continue;
                }

                definitionByDataType[dataType] = assetType;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AssetDataBase)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssetDataBase>(path);
                if (asset == null || !definitionByDataType.TryGetValue(asset.GetType(), out var assetType))
                {
                    continue;
                }

                var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!SpecIdentifierCodec.TryExtractIdentifier(fileName, assetType, out var identifier))
                {
                    continue;
                }

                index[assetType + "::" + identifier] = asset;
            }

            return index;
        }
    }
}
