using System.Collections.Generic;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2「Maya FBX 自動生成」— モデルのインポート後に MaterialData を自動生成する入口(チケット 3-7)。
    // インポート中はアセットを作れないため、delayCall でインポート完了後に MayaMaterialImporter を走らせる。
    public sealed class MayaModelPostprocessor : AssetPostprocessor
    {
        // テスト / 一括インポート中の抑止。
        public static bool Suppress;

        private static readonly List<string> Pending = new();

        private void OnPostprocessModel(GameObject root)
        {
            if (Suppress)
            {
                return;
            }

            var profile = MayaImportProfile.FindOrDefault();
            if (!profile.AutoImport || !profile.AppliesTo(assetPath))
            {
                return;
            }

            if (!Pending.Contains(assetPath))
            {
                Pending.Add(assetPath);
            }

            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            var profile = MayaImportProfile.FindOrDefault();
            var paths = Pending.ToArray();
            Pending.Clear();
            foreach (var path in paths)
            {
                var report = MayaMaterialImporter.ImportModel(path, profile);
                Debug.Log($"[DDrive] Maya インポート: {path}\n{report}{RebindSlots(path)}");
            }
        }

        // 生成した MaterialData を、その FBX を使っている ModelData の Material スロットへ結び付ける(U-2、2026-09-17)。
        // それまでは MaterialData を作るところで経路が途切れており、ModelData.Slots は None のままだった。
        private static string RebindSlots(string modelPath)
        {
            var slotReport = new DDrive.Editor.Model.ModelSlotBinder.Report();
            var updated = DDrive.Editor.Model.ModelSlotBinder.RebindForModelPath(modelPath, slotReport);
            return updated > 0 ? $"\nModelData の Slots を更新: {updated} 件\n{slotReport}" : string.Empty;
        }

        // 手動: 選択した FBX / モデルから生成(AutoImport=OFF の運用や、規約変更後のやり直し用)。
        [MenuItem(DDriveMenu.Generate + "選択したモデルから MaterialData を生成")]
        private static void GenerateFromSelection()
        {
            var profile = MayaImportProfile.FindOrDefault();
            var count = 0;
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not ModelImporter)
                {
                    continue;
                }

                var report = MayaMaterialImporter.ImportModel(path, profile);
                Debug.Log($"[DDrive] Maya インポート(手動): {path}\n{report}{RebindSlots(path)}");
                count++;
            }

            if (count == 0)
            {
                Debug.LogWarning("[DDrive] モデル(FBX 等)を Project で選択してから実行してください。");
            }
        }

        [MenuItem(DDriveMenu.Generate + "選択したモデルから MaterialData を生成", true)]
        private static bool ValidateGenerateFromSelection()
        {
            foreach (var obj in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
