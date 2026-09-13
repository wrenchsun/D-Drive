using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Inspector
{
    // [27_spec_sheet.md] §5 / [11_tasks.md] 5-14 — Data.SpecUrl が設定されているときだけ
    // Inspector 上部に「仕様書を開く」ボタンを出す(空なら何も描かない = ボタンを無効表示にはしない)。
    // AssetDataInspector.DrawOpenEditorHeader() から呼ばれる(DataEditorHeader/AssetIconGui と同じ列)。
    public static class SpecUrlGui
    {
        public static void Draw(AssetDataBase target)
        {
            if (target == null || string.IsNullOrEmpty(target.SpecUrl))
            {
                return;
            }

            var content = new GUIContent("📄 仕様書を開く", target.SpecUrl);
            if (GUILayout.Button(content, GUILayout.Height(22)))
            {
                Application.OpenURL(target.SpecUrl);
            }
        }
    }
}
