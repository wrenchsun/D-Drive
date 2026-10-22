using UnityEditor;

namespace DDrive.Editor.Import
{
    // 1017 TA-2(DD-3) — Assets/SourceAssets/Anim/ 配下の FBX の初回取り込みで preserveHierarchy を ON にする。
    // トップレベルが 1 つの FBX はそのノードがプレハブのルートになり名前がファイル名に替わって、
    // アニメーションカーブの道筋(階層)がずれる(T-Drive の A_<ModelId>_<動作>.fbx を Generic で使うとき)。
    // 書くのは初回取り込み(importSettingsMissing = .meta が無い)のときだけ = デザイナーが Inspector で変えた値は上書きしない。
    // E-17 (c) の許可ファイル(docs/42 §5.14。ExternalContractStaticScanTests の許可リスト)。Model フォルダには触らない。
    public sealed class AnimFbxPreprocessor : AssetPostprocessor
    {
        public const string TypeFolder = "Anim";

        private void OnPreprocessModel()
        {
            if (ImportRulePostprocessor.Suppress || !ImportRuleService.AutoImport)
            {
                return;
            }

            if (!IsUnderAnimFolder(assetPath) || assetImporter is not ModelImporter modelImporter)
            {
                return;
            }

            ApplyInitialImportSettings(modelImporter, assetImporter.importSettingsMissing);
        }

        // テストから直接呼べるように分離している(isFirstImport = importSettingsMissing)。
        public static void ApplyInitialImportSettings(ModelImporter modelImporter, bool isFirstImport)
        {
            if (modelImporter == null || !isFirstImport)
            {
                return;
            }

            modelImporter.preserveHierarchy = true;
        }

        public static bool IsUnderAnimFolder(string assetPath)
        {
            var normalized = assetPath?.Replace('\\', '/');
            return normalized != null
                   && normalized.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                   && normalized.StartsWith(ImportRuleService.DefaultSourceRoot + "/" + TypeFolder + "/", System.StringComparison.Ordinal);
        }
    }
}
