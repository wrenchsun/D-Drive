using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Tuning
{
    // [09_editor_tools.md] §「Tuning ウィンドウ」(M-2a、2026-09-27) — TuningTable の Inspector 最上部に
    // 「Tuning ウィンドウで開く」ボタンを出す。[DataEditor]([09] §8)は AssetDataBase 専用のため使えない
    // (TuningTable は AssetDataBase 派生ではない。プロジェクト単位設定という位置付けは
    // TuningTable.cs 冒頭コメント参照)。
    [CustomEditor(typeof(DDrive.Runtime.Tuning.TuningTable))]
    public sealed class TuningTableEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Tuning ウィンドウで開く", GUILayout.Height(24)))
            {
                TuningEditorWindow.Open((DDrive.Runtime.Tuning.TuningTable)target);
            }

            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
