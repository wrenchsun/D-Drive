using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Materials;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using UnityEditor;
using UnityEngine;
using MaterialId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Material.MaterialMarker>;

namespace DDrive.Editor.Model
{
    // [05_model_animation.md] A-4 / [06_material_texture.md] A-2 — ModelData.Slots の自動収集と
    // 「Renderer が実際に使っている Material」→「その Material から生成された MaterialData」の結び付け(U-2、2026-09-17)。
    //
    // それまで「Slot 自動収集」は RendererPath + SlotIndex を並べるだけで Material は常に None のままだった。
    // FBX インポート時に MayaMaterialImporter が MaterialData を作っていても、ModelData 側と繋がる経路がどこにも無く、
    // デザイナーが 1 スロットずつ手で ID を選ばないと Slots が機能しなかった。
    //
    // ここは「探して結び付ける」だけで、MaterialData の生成自体は既存経路(MayaMaterialImporter / UnityMaterialMigrator)を
    // そのまま呼ぶ(新しい並行経路を作らない)。同定キーも MaterialData.SourceMaterial(既存)をそのまま使う:
    //   FBX 内蔵 Material      → MayaMaterialImporter.BuildSourceMaterial(source, "<FBX名>")
    //   単体の .mat アセット    → UnityMaterialMigrator.SourceKey("UnityMaterial")を sourceKey にした同じ形
    //
    // 呼び出し元: ModelImportHandler(元ファイル配置での自動生成)/ MayaModelPostprocessor(FBX インポート・手動生成の直後)/
    //             ModelEditorWindow(「Slot 自動収集」「元ファイル再読み込み」)。
    public static class ModelSlotBinder
    {
        public sealed class Report
        {
            public readonly List<string> Lines = new();
            public int Slots;      // 収集したスロット数
            public int Bound;      // 新たに MaterialData の ID を割り当てた数
            public int Kept;       // 既存の割り当てを保持した数
            public int Unresolved; // 対応する MaterialData が見つからなかった数

            public void Log(string line) => Lines.Add(line);

            public override string ToString()
                => $"スロット {Slots}(新規割当 {Bound} / 既存保持 {Kept} / 未解決 {Unresolved})"
                   + (Lines.Count > 0 ? "\n" + string.Join("\n", Lines) : string.Empty);
        }

        // ── 入口 1: ModelData へ反映する(Undo + SetDirty。[00] §0 TL;DR 5) ──
        // ensureMaterials=true なら、先に元モデル(FBX)/ Material から MaterialData を作り直してから結び付ける(U-3 の「再読み込み」)。
        // 既に有効な ID が入っているスロットは上書きしない(デザイナーの差し替えを壊さない)。
        public static bool Rebuild(ModelData data, bool ensureMaterials, Report report = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            report ??= new Report();
            if (data == null)
            {
                Debug.LogWarning("[DDrive] ModelSlotBinder: 対象の ModelData がありません。");
                return false;
            }

            if (data.Prefab == null)
            {
                Debug.LogWarning($"[DDrive] ModelSlotBinder: '{data.name}' に Prefab がありません。");
                return false;
            }

            MayaMaterialImporter.BeginBatch(gameDataRoot);
            try
            {
                if (ensureMaterials)
                {
                    EnsureMaterialData(data.Prefab, report, gameDataRoot);
                }

                var slots = BuildSlots(data.Prefab, data.Slots, report, gameDataRoot);
                if (SlotsEqual(data.Slots, slots))
                {
                    return false;
                }

                Undo.RecordObject(data, "Rebuild Model Slots");
                data.Slots = slots;
                EditorUtility.SetDirty(data);
                return true;
            }
            finally
            {
                MayaMaterialImporter.EndBatch();
            }
        }

        // ── 入口 2: その元モデル(FBX)を Prefab に使っている ModelData 全部を貼り直す ──
        // MayaMaterialImporter が MaterialData を作った直後に呼ばれる(FBX を差し替えて再インポートしたときの自動追従)。
        public static int RebindForModelPath(string modelPath, Report report = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            report ??= new Report();
            if (string.IsNullOrEmpty(modelPath) || !AssetDatabase.IsValidFolder(gameDataRoot))
            {
                return 0;
            }

            var updated = 0;
            MayaMaterialImporter.BeginBatch(gameDataRoot);
            try
            {
                foreach (var guid in AssetSearch.FindAssets("t:" + nameof(ModelData), new[] { gameDataRoot }))
                {
                    var model = AssetDatabase.LoadAssetAtPath<ModelData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (model == null || model.Prefab == null || !UsesModel(model.Prefab, modelPath))
                    {
                        continue;
                    }

                    // ここでの MaterialData 生成は呼び出し側(MayaMaterialImporter)が済ませている。
                    if (Rebuild(model, ensureMaterials: false, report, gameDataRoot))
                    {
                        updated++;
                        report.Log($"Slots 更新: {model.name}");
                    }
                }
            }
            finally
            {
                MayaMaterialImporter.EndBatch();
            }

            return updated;
        }

        // ── スロット組み立て(Undo なし。AssetCreationService.Create の configure からも呼べる) ──
        // 既存(existing)に RendererPath + SlotIndex が一致する有効な割り当てがあればそれを保持し、
        // 未割当のスロットだけ Renderer の sharedMaterials[slotIndex] から MaterialData を引き当てて埋める。
        public static MaterialSlot[] BuildSlots(GameObject prefab, MaterialSlot[] existing, Report report = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            report ??= new Report();
            if (prefab == null)
            {
                return System.Array.Empty<MaterialSlot>();
            }

            existing ??= System.Array.Empty<MaterialSlot>();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var collected = new List<MaterialSlot>();
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var path = DDrive.Runtime.Ui.TransformPath.GetRelative(prefab.transform, renderer.transform);
                var materials = renderer.sharedMaterials;
                for (var slotIndex = 0; slotIndex < materials.Length; slotIndex++)
                {
                    report.Slots++;
                    var preserved = FindExistingSlot(existing, path, slotIndex);
                    if (preserved.Material.IsValid)
                    {
                        report.Kept++;
                        collected.Add(new MaterialSlot { RendererPath = path, SlotIndex = slotIndex, Material = preserved.Material });
                        continue;
                    }

                    var source = materials[slotIndex];
                    var id = FindMaterialId(source, gameDataRoot);
                    if (id.IsValid)
                    {
                        report.Bound++;
                    }
                    else
                    {
                        report.Unresolved++;
                        report.Log(source == null
                            ? $"未解決: {DescribePath(path)}[{slotIndex}] は Prefab 側の Material が空です"
                            : $"未解決: {DescribePath(path)}[{slotIndex}] の Material '{source.name}' に対応する MaterialData がありません");
                    }

                    collected.Add(new MaterialSlot { RendererPath = path, SlotIndex = slotIndex, Material = id });
                }
            }

            return collected.ToArray();
        }

        // ── Material(実体) → MaterialData の ID ──
        // MaterialData.SourceMaterial(既存の同定キー)で引く。FBX 内蔵 Material は sourceKey = FBX 名、
        // 単体の .mat は UnityMaterialMigrator.SourceKey。旧形式(GUID 無し)のキーも MayaMaterialImporter が面倒を見る。
        public static MaterialId FindMaterialId(UnityEngine.Material source, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            var data = FindMaterialData(source, gameDataRoot);
            return data != null ? new MaterialId(data.Id, AssetType.Material) : MaterialId.Invalid;
        }

        public static MaterialData FindMaterialData(UnityEngine.Material source, string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            if (source == null)
            {
                return null;
            }

            foreach (var sourceKey in SourceKeysFor(source))
            {
                var hit = MayaMaterialImporter.FindExisting(
                    MayaMaterialImporter.BuildSourceMaterial(source, sourceKey),
                    MayaMaterialImporter.BuildLegacySourceMaterial(source, sourceKey),
                    gameDataRoot);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        // ── 足りない MaterialData を既存経路で作る(U-3 の再読み込み) ──
        // FBX 内蔵 Material は MayaMaterialImporter.ImportModel(モデル単位・1 回だけ)、
        // 単体の .mat は UnityMaterialMigrator.Migrate。どちらも再実行は Common だけ更新し、固有調整は保持する。
        public static void EnsureMaterialData(GameObject prefab, Report report = null,
            string gameDataRoot = AssetCreationService.DefaultGameDataRoot)
        {
            report ??= new Report();
            if (prefab == null)
            {
                return;
            }

            var profile = MayaImportProfile.FindOrDefault();
            var modelPaths = new List<string>();
            var loose = new List<UnityEngine.Material>();

            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (IsModelAsset(prefabPath))
            {
                modelPaths.Add(prefabPath);
            }

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    var path = AssetDatabase.GetAssetPath(material);
                    if (IsModelAsset(path))
                    {
                        if (!modelPaths.Contains(path))
                        {
                            modelPaths.Add(path);
                        }
                    }
                    else if (!string.IsNullOrEmpty(path) && !loose.Contains(material))
                    {
                        loose.Add(material);
                    }
                }
            }

            var materialReport = new MayaMaterialImporter.Report();
            foreach (var path in modelPaths)
            {
                MayaMaterialImporter.ImportModel(path, profile, gameDataRoot);
                report.Log($"MaterialData を再生成: {path}");
            }

            foreach (var material in loose)
            {
                if (UnityMaterialMigrator.Migrate(material, null, materialReport, gameDataRoot) != null)
                {
                    report.Log($"MaterialData を再生成: {AssetDatabase.GetAssetPath(material)}({material.name})");
                }
            }

            if (modelPaths.Count == 0 && loose.Count == 0)
            {
                report.Log("再生成の対象になる元アセット(FBX / .mat)が Prefab の Renderer から見つかりませんでした。");
            }
        }

        // ── 内部 ──

        // 同じ Material が FBX 内蔵として取り込まれた場合と、単体 .mat として変換された場合の両方を順に試す。
        private static IEnumerable<string> SourceKeysFor(UnityEngine.Material source)
        {
            var path = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(path))
            {
                yield return System.IO.Path.GetFileNameWithoutExtension(path);
            }

            yield return UnityMaterialMigrator.SourceKey;
        }

        private static bool IsModelAsset(string assetPath)
            => !string.IsNullOrEmpty(assetPath) && AssetImporter.GetAtPath(assetPath) is ModelImporter;

        // prefab(ModelData.Prefab)がその元モデルに由来するか。FBX を直接参照している場合と、
        // FBX から作ったラッパー Prefab の場合(Renderer の Material が FBX 内蔵)の両方を拾う。
        private static bool UsesModel(GameObject prefab, string modelPath)
        {
            if (AssetDatabase.GetAssetPath(prefab) == modelPath)
            {
                return true;
            }

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && AssetDatabase.GetAssetPath(material) == modelPath)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static MaterialSlot FindExistingSlot(MaterialSlot[] slots, string path, int slotIndex)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].RendererPath == path && slots[i].SlotIndex == slotIndex)
                {
                    return slots[i];
                }
            }

            return default;
        }

        private static bool SlotsEqual(MaterialSlot[] a, MaterialSlot[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].RendererPath != b[i].RendererPath || a[i].SlotIndex != b[i].SlotIndex || a[i].Material != b[i].Material)
                {
                    return false;
                }
            }

            return true;
        }

        private static string DescribePath(string path) => string.IsNullOrEmpty(path) ? "(ルート)" : path;
    }
}
