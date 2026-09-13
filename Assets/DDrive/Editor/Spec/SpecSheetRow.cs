using System.Collections.Generic;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §3.1 の「アセット」タブ 1 行(パース後)。列の生の文字列のまま保持し、
    // 実際の Data への反映(状態→タグ 等の変換)は SpecSyncService が行う。
    public sealed class SpecAssetRow
    {
        public int RowNumber;
        public AssetType Type;
        public string Category = string.Empty;
        public string Identifier = string.Empty;
        public string DisplayName = string.Empty;
        public string Status = string.Empty;
        public string Assignee = string.Empty;
        public string SpecLink = string.Empty;
        public string Note = string.Empty;

        // 同期のキー([27] §3.1: 種別+識別子)。
        public string Key => Type + "::" + Identifier;
    }

    // [27_spec_sheet.md] §3.2 の「調整値」タブ 1 行(パース後)。値・型はまだ文字列のまま
    // (TuningEntry への変換は SpecSyncService。数値変換の失敗は Issues に積む)。
    public sealed class SpecTuningRow
    {
        public int RowNumber;
        public string Key = string.Empty;
        public string RawValue = string.Empty;
        public string RawType = string.Empty;
        public string RawMin = string.Empty;
        public string RawMax = string.Empty;
        public string Unit = string.Empty;
        public string Description = string.Empty;
    }

    // 差分プレビュー画面([27] §4.3)の「衝突」区分に出す 1 件(行番号付き)。
    public readonly struct SpecIssue
    {
        public readonly int RowNumber;
        public readonly string Message;

        public SpecIssue(int rowNumber, string message)
        {
            RowNumber = rowNumber;
            Message = message;
        }
    }

    public sealed class SpecParseResult<T>
    {
        public readonly List<T> Rows = new();
        public readonly List<SpecIssue> Issues = new();
    }
}
