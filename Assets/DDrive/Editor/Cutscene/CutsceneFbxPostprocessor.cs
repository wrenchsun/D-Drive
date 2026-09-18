using System.Collections.Generic;
using System.IO;
using DDrive.Editor.Import;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §5(6-10c) — Assets/SourceAssets/Cutscene/ 配下の FBX を検知する入口。
    // 役割は 2 つ: (1) OnPreprocessModel でキャラ FBX(<ショット>__<Model識別子>.fbx)を Humanoid +
    // CopyFromOther(ModelData.Avatar) に設定する(インポート設定はインポート前にしか変更できないため、
    // ここでしか行えない)。(2) MayaModelPostprocessor/ImportRulePostprocessor と同じ delayCall バッチで
    // CutsceneImportService.ProcessPaths を呼び、CutsceneData + TimelineAsset を構築する。
    public sealed class CutsceneFbxPostprocessor : AssetPostprocessor
    {
        // テスト / 一括インポート中の抑止。
        public static bool Suppress;

        private static readonly List<string> Pending = new();
        private static readonly HashSet<string> PendingSet = new();

        // [26_timeline.md] §5.4 — キャラ FBX(ファイル名に "__" を含む)を Humanoid + CopyFromOther に設定する。
        // インポート前の設定変更のみここで行える(モデルが出来上がった後の OnPostprocessModel では変更できない)。
        private void OnPreprocessModel()
        {
            if (Suppress || !CutsceneImportService.AutoImport || !CutsceneImportProfile.FindOrDefault().AutoImport)
            {
                return;
            }

            if (!IsUnderCutsceneFolder(assetPath))
            {
                return;
            }

            var fileNameNoExt = Path.GetFileNameWithoutExtension(assetPath);
            CutsceneShotParser.ParseFileName(fileNameNoExt, out _, out var isCharacter, out var modelIdentifierRaw);
            if (!isCharacter)
            {
                return; // カメラ+小物 FBX は Generic のまま(既定)。
            }

            if (assetImporter is not ModelImporter modelImporter)
            {
                return;
            }

            var modelIdentifier = CutsceneShotParser.StripDuplicateSuffix(modelIdentifierRaw);
            var model = CutsceneImportService.FindModelDataByIdentifier(modelIdentifier);

            modelImporter.animationType = ModelImporterAnimationType.Human;
            if (model != null && model.Avatar != null)
            {
                modelImporter.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                modelImporter.sourceAvatar = model.Avatar;
            }
            else
            {
                // [26_timeline.md] §5.4 — ModelData/Avatar が見つからない場合は仮に CreateFromThisModel で
                // 取り込む(骨だけの FBX からでも Avatar は作れる)。Validation は 6-10d の拡張で警告する。
                modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                Debug.LogWarning($"[DDrive] Cutscene 取り込み: {assetPath} 用の ModelData('{modelIdentifier}')または Avatar が見つからないため、"
                                  + "仮に CreateFromThisModel で取り込みます([26_timeline.md] §5.4)。");
            }
        }

        private static bool IsUnderCutsceneFolder(string assetPath)
        {
            var normalized = assetPath?.Replace('\\', '/');
            return normalized != null
                   && normalized.StartsWith(ImportRuleService.DefaultSourceRoot + "/" + CutsceneImportService.TypeFolder + "/", System.StringComparison.Ordinal);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Suppress || !CutsceneImportService.AutoImport || !CutsceneImportProfile.FindOrDefault().AutoImport)
            {
                return;
            }

            AddPending(importedAssets);
            AddPending(movedAssets);

            if (Pending.Count == 0)
            {
                return;
            }

            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        private static void AddPending(string[] paths)
        {
            if (paths == null)
            {
                return;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(path) && IsUnderCutsceneFolder(path) && PendingSet.Add(path))
                {
                    Pending.Add(path);
                }
            }
        }

        private static void Flush()
        {
            var paths = Pending.ToArray();
            Pending.Clear();
            PendingSet.Clear();
            if (paths.Length == 0)
            {
                return;
            }

            var report = CutsceneImportService.ProcessPaths(paths);
            if (report.Created > 0 || report.Updated > 0)
            {
                Debug.Log($"[DDrive] Cutscene 取り込み: {report}");
            }
        }

        // 手動: AutoImport=OFF の期間や機能導入前から置かれていた FBX を一括で取り込む。
        [MenuItem(DDriveMenu.Generate + "SourceAssets/Cutscene からインポートルールを再実行")]
        private static void RunScanAll()
        {
            var report = CutsceneImportService.ScanAll();
            Debug.Log($"[DDrive] Cutscene 取り込み(再実行): {report}");
        }
    }
}
