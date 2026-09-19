using System.Collections.Generic;
using DDrive.Foundation.Data;
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

            // 共通チャンネル名等が Specific に紛れていると Common を黙って上書きするので警告する(2026-09-11 レビュー対応)。
            // 値は消さない(データを失わない)。取り除くのは RemoveConflicts。
            if (report.Conflict.Count > 0)
            {
                Debug.LogWarning($"[DDrive] '{data.name}' の Specific に共通チャンネル / 描画ステート名が含まれています(Common の値を上書きします): " +
                                 string.Join(", ", report.Conflict));
            }

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

        public const string RemoveConflictsUndoName = "Remove Common Channel Overrides";

        // Common を上書きしてしまう項目(共通チャンネル / 描画ステート / 予約 / 付随 / フラグ除外)を Specific から取り除く。
        // 戻り値は取り除いた件数。Validator の警告に対する手当て(2026-09-11 レビュー対応)。
        public static int RemoveConflicts(MaterialData data, bool recordUndo = true)
        {
            if (data == null || data.Shader == null || data.Specific == null || data.Specific.Length == 0)
            {
                return 0;
            }

            var kept = new List<ShaderParam>(data.Specific.Length);
            var removed = 0;
            for (var i = 0; i < data.Specific.Length; i++)
            {
                if (MaterialSpecificResolver.IsConflicting(data.Shader, data.Specific[i].Property))
                {
                    removed++;
                    continue;
                }

                kept.Add(data.Specific[i]);
            }

            if (removed == 0)
            {
                return 0;
            }

            if (recordUndo)
            {
                Undo.RecordObject(data, RemoveConflictsUndoName);
            }

            data.Specific = kept.ToArray();
            EditorUtility.SetDirty(data);
            return removed;
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

            if (report.Conflict.Count > 0)
            {
                text += "\n共通チャンネル名のため Common を上書きします(残しています): " + string.Join(", ", report.Conflict);
            }

            return text;
        }
    }
}
