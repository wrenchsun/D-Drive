using System;
using System.Collections.Generic;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.Spec
{
    // [32_spec_web.md] §3.1 の assets.json の 1 件(パース後)。W-9 で取得元を Web(GAS) API に
    // 差し替えたが、行の意味(種別+識別子+上書き可能フィールド)は [27_spec_sheet.md] §3.1 の
    // CSV 版と同じため、既存の SpecDiffService/SpecSyncService はこの型のまま変更不要で使える。
    // RowNumber は CSV 時代の「行番号」の名残(衝突メッセージの表示用)。JSON には行番号が無いため、
    // Web 版では配列内の 0 始まりインデックス + 1 を入れる。
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

    // [32_spec_web.md] §3.2.1 の tuning.json(スカラー)の 1 件(パース後)。値・型はまだ文字列のまま
    // (TuningEntry への変換は SpecSyncService。数値変換の失敗は Issues に積む)。
    // W-10(案 A)で Enum 型に対応するため RawEnumOptions を追加した(CSV 時代には無かった)。
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
        public string[] RawEnumOptions = Array.Empty<string>();
    }

    // [32_spec_web.md] §3.2.2 の tuning.json(テーブル型)の 1 件(パース後)。列定義・行は
    // Newtonsoft.Json.Linq の JObject/JArray のまま保持する(SpecSyncService.ApplyTuningTable が
    // TuningTableEntry へ変換する 1 か所に変換ロジックを閉じ込めるため。既存の CSV 用の型
    // (SpecAssetRow/SpecTuningRow のような文字列フィールド)に展開しても使う場所が
    // ApplyTuningTable だけのため、素朴に JSON ツリーのまま運ぶ)。
    public sealed class SpecTuningTableRow
    {
        public int RowNumber;
        public string Key = string.Empty;

        // tuningTables コレクションの 1 アイテムの生 JSON(columns/rows/locked を含む)。
        public Newtonsoft.Json.Linq.JObject Raw;
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
