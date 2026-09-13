using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using UnityEngine;

namespace DDrive.Editor.Import
{
    // [11_tasks.md] 5-11 / [09_editor_tools.md] / [10_workflow.md] §3.3 —
    // 種別ごとの「元ファイル → Data」差分だけをここに閉じ、探索・作成・カタログ/Addressables登録の
    // 共通処理は ImportRuleService(+AssetCreationService)に一本化する(Maya→Material と同じ方針)。
    public interface IImportRuleHandler
    {
        // 監視フォルダ名(SourceAssets/<TypeFolder>/<カテゴリ>/)。AssetType の英語表記と揃える。
        string TypeFolder { get; }

        AssetType Target { get; }

        Type DataType { get; }

        // 対象拡張子(小文字・ドット付き。例: ".wav")。
        string[] Extensions { get; }

        // 識別子(ID定数名)が生成できないとき(数字始まり等)の前置語。
        string IdentifierFallback { get; }

        // 元ファイルから、この Data が参照する Unity Object を読み込む。取得できない/対応するサブアセットが無ければ null
        // (呼び出し側はこの場合作成をスキップする。例外にしない)。
        UnityEngine.Object LoadSource(string assetPath);

        // 新規作成した Data に source を割り当てる(AssetCreationService.Create の configure から呼ばれる。
        // まだアセット化されていないため Undo は積まない)。
        void Configure(AssetDataBase data, UnityEngine.Object source, string assetPath);
    }
}
