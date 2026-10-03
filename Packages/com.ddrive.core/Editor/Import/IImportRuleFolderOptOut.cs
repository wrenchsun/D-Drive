using System.Collections.Generic;

namespace DDrive.Editor.Import
{
    // [51_tdrive_integration.md] §4.7(FC-6、2026-10-03) — 「SourceAssets/<名前>/ は自分(外部パッケージ)が管理するので、
    // ImportRule の『不明な種別フォルダ』の案内を出さないでほしい」と宣言するだけの軽い拡張点。
    // ハンドラ(IImportRuleHandler)を持たない外部パッケージ向け。D-Drive はそのフォルダを取り込まない(何もしない)。
    //
    // 発見: `TypeCache`(FC-5 の ICutsceneImportListener と同じ方式)。public な非 abstract 型 + public な引数なし
    // コンストラクタが必須。`DDrive.Tests*` で始まるアセンブリの実装は除外する。結果はドメインリロードまでキャッシュされる。
    //
    // 規則:
    //  - 名前は SourceAssets 直下のフォルダ名(大文字小文字を区別。区切り文字を含まない)。null / 空 / 空白 / 重複は無視する。
    //  - 組み込みのハンドラ / 外部の IImportRuleHandler が使っている種別フォルダ名、および D-Drive が既に別経路で使っている
    //    フォルダ名(Shaders / Data / Samples / Cutscene 等の KnownNonTargetTypeFolders)は、宣言しても意味が無いので
    //    警告 1 回 + 無視する(組み込み優先)。
    //  - 実装の例外は隔離する(Debug.LogException + 継続)。
    // 互換: DDrive.Editor の弱い互換面(Editor 契約。docs/42 §5.9 / §5.14 E-21)。追加のみ。
    public interface IImportRuleFolderOptOut
    {
        // 案内の対象外にする SourceAssets 直下のフォルダ名(例: "Facial")。
        IEnumerable<string> FolderNames { get; }
    }
}
