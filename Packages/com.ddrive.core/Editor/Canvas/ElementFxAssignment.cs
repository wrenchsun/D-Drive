using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;

namespace DDrive.Editor.CanvasTool
{
    // [15_ui_interaction.md] B-3.5 / B-4(4-12) — CanvasData.ElementEffects への 1 要素・1 フェーズ分の
    // 割当を書き込む共通処理。元は CanvasEditorWindow.BuildPhaseRow のコールバック内にインライン実装されていた
    // ものを、UiPresetGalleryWindow(4-12「この要素に適用」)からも使えるよう static へ切り出した。
    public enum ElementFxPhase
    {
        Appear,
        Idle,
        Disappear,
    }

    public static class ElementFxAssignment
    {
        // path に一致する行が無ければ末尾に追加してから、指定フェーズへ Preset を設定し(Id 側はクリア)、
        // Undo.RecordObject + SetDirty で包む。
        public static void SetPreset(CanvasData canvas, string elementPath, ElementFxPhase phase, UiPresetRef preset)
        {
            if (canvas == null)
            {
                return;
            }

            Undo.RecordObject(canvas, "ElementFx: プリセットを割当");
            var index = FindOrAddRow(canvas, elementPath);
            var row = canvas.ElementEffects[index];
            ApplyPreset(ref row, phase, preset);
            ApplyId(ref row, phase, default);
            canvas.ElementEffects[index] = row;
            EditorUtility.SetDirty(canvas);
        }

        // カタログ/UiTweenData 直接指定用(Preset 側はクリアされる)。
        public static void SetTween(CanvasData canvas, string elementPath, ElementFxPhase phase, AssetId<UiTweenMarker> id)
        {
            if (canvas == null)
            {
                return;
            }

            Undo.RecordObject(canvas, "ElementFx: Tween を割当");
            var index = FindOrAddRow(canvas, elementPath);
            var row = canvas.ElementEffects[index];
            ApplyId(ref row, phase, id);
            ApplyPreset(ref row, phase, default);
            canvas.ElementEffects[index] = row;
            EditorUtility.SetDirty(canvas);
        }

        private static int FindOrAddRow(CanvasData canvas, string elementPath)
        {
            elementPath ??= string.Empty;
            var rows = canvas.ElementEffects;
            if (rows != null)
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    if (rows[i].ElementPath == elementPath)
                    {
                        return i;
                    }
                }
            }

            var newRows = new ElementFx[(rows?.Length ?? 0) + 1];
            if (rows != null)
            {
                System.Array.Copy(rows, newRows, rows.Length);
            }

            newRows[newRows.Length - 1] = new ElementFx { ElementPath = elementPath };
            canvas.ElementEffects = newRows;
            return newRows.Length - 1;
        }

        private static void ApplyPreset(ref ElementFx row, ElementFxPhase phase, UiPresetRef preset)
        {
            switch (phase)
            {
                case ElementFxPhase.Appear: row.AppearPreset = preset; break;
                case ElementFxPhase.Idle: row.IdlePreset = preset; break;
                case ElementFxPhase.Disappear: row.DisappearPreset = preset; break;
            }
        }

        private static void ApplyId(ref ElementFx row, ElementFxPhase phase, AssetId<UiTweenMarker> id)
        {
            switch (phase)
            {
                case ElementFxPhase.Appear: row.Appear = id; break;
                case ElementFxPhase.Idle: row.Idle = id; break;
                case ElementFxPhase.Disappear: row.Disappear = id; break;
            }
        }
    }
}
