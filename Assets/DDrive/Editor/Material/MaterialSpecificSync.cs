using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2 — MaterialData.Specific をシェーダーから同期する Editor 入口(2026-09-11)。
    // 解決ロジックは MaterialSpecificResolver(Runtime、純関数)。ここは Undo.RecordObject + SetDirty で Data に書き戻すだけ。
    // 呼び出し元: MaterialEditorWindow(Shader 変更時の自動同期 + ボタン)/ MaterialConvertWindow(変換後の補完)/
    //             MayaMaterialImporter(新規作成時)/ MaterialDataValidator の FixAction はこの Runtime 版が無いので Info のみ。
    public static class MaterialSpecificSync
    {
        public const string UndoName = "Sync Material Specific";

        // 既存の値は保持し、足りない固有だけを既定値で追加する。変更が無ければ Data に触らない。
        public static MaterialSpecificResolver.MergeReport Sync(MaterialData data, bool recordUndo = true)
        {
            var report = new MaterialSpecificResolver.MergeReport();
            if (data == null || data.Shader == null)
            {
                return report;
            }

            var merged = MaterialSpecificResolver.Merge(data.Specific, data.Shader, report);
            if (!report.Changed)
            {
                return report;
            }

            if (recordUndo)
            {
                Undo.RecordObject(data, UndoName);
            }

            data.Specific = merged;
            EditorUtility.SetDirty(data);
            return report;
        }

        public static string Describe(MaterialSpecificResolver.MergeReport report)
        {
            if (report == null)
            {
                return string.Empty;
            }

            var text = $"固有: 追加 {report.Added.Count} / 保持 {report.Kept.Count} / シェーダーに無い {report.Stale.Count}";
            if (report.Added.Count > 0)
            {
                text += "\n追加: " + string.Join(", ", report.Added);
            }

            if (report.Stale.Count > 0)
            {
                text += "\nシェーダーに無い(残しています): " + string.Join(", ", report.Stale);
            }

            return text;
        }
    }
}
