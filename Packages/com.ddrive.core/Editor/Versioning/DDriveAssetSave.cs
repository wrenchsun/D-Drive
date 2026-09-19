using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Versioning
{
    // [44_review_2026-09-19.md] P1-1 — 引数なしの AssetDatabase.SaveAssets() は「呼んだ瞬間にプロジェクト全体で
    // dirty な AssetDataBase すべて」を無差別に VersionStampProcessor.OnWillSaveAssets の対象にする
    // (パス単位の呼び出し元を区別できない、VersionStamp.cs:17-20)。デザイナーが実アセットを開いて編集中に
    // 別の一括処理(テスト・カタログ同期・Validator の FixAction 等)が SaveAssets() を呼ぶと、その実アセットの
    // Version/Author/UpdatedAt が意図せず進んでしまう(docs/41「テストが実データを汚す不具合」)。
    //
    // このヘルパーに寄せることで、Editor コードから AssetDatabase.SaveAssets() の直呼びを無くす
    // (再発防止は ForbiddenApiScanner のテスト、Tests/Editor/NoDirectSaveAssetsCallTests.cs 側)。
    //
    // 使い分け(docs/09_editor_tools.md §4.1 に判断表。VersionStamp.cs:11-16 の 3 分類に対応):
    // - SaveAllSuppressed(): 「一括処理」(インポート検知の自動生成・カタログ/Addressables 同期・
    //   仕様書同期の適用・ID 再生成・Validator の FixAction・削除/整理などの機械的なクリーンアップ)。
    //   何個の/どのアセットが変更されたか呼び出し元が把握しきれない、または把握していても
    //   「この操作で版数を進めるべきではない」もの。VersionStampSuppression.Scope() で囲むため、
    //   このタイミングで他に dirty な実アセットが乗っても版数は進まない。
    // - SaveDirty(obj): 「デザイナーが特定の 1 個のアセットを編集して保存する」本来の経路。対象を 1 個に
    //   絞ることで、たまたま他に開いていた無関係な実アセットの dirty を巻き込まない
    //   (AssetDatabase.SaveAssetIfDirty は指定したオブジェクトしか書き込まない)。抑止しないので
    //   VersionStampProcessor が通常どおり Version/Author/UpdatedAt を進める(挙動は変えない)。
    public static class DDriveAssetSave
    {
        // カタログ/Addressables 同期・ID 再生成・仕様書同期の適用・インポート自動生成・Validator の
        // FixAction・削除整理など、「機械的な一括処理」用。対象を版数に乗せない。
        public static void SaveAllSuppressed()
        {
            using (VersionStampSuppression.Scope())
            {
                AssetDatabase.SaveAssets();
            }
        }

        // デザイナーが 1 個のアセットを編集して保存する本来の経路。抑止しないので通常どおり版数が進む。
        // 対象が null / dirty でなければ何もしない。
        public static void SaveDirty(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
