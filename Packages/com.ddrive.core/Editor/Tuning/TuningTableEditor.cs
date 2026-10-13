using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Tuning
{
    // [09_editor_tools.md] §「Tuning ウィンドウ」(M-2a、2026-09-27)/ docs/1010 UX-15 — TuningTable の Inspector 最上部に
    // 「Tuning Editor で開く」(新デザイン)と「(旧) Tuning ウィンドウで開く」(Archive の旧版)のボタンを出す。
    // [DataEditor]([09] §8)は AssetDataBase 専用のため使えない(TuningTable は AssetDataBase 派生ではない。
    // プロジェクト単位設定という位置付けは TuningTable.cs 冒頭コメント参照)ので、導線はこの CustomEditor に持たせる。
    [CustomEditor(typeof(TuningTable))]
    public sealed class TuningTableEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Tuning Editor で開く", GUILayout.Height(24)))
            {
                TuningEditor.Open((TuningTable)target);
            }

            if (GUILayout.Button("(旧) Tuning ウィンドウで開く"))
            {
                TuningEditorWindow.Open((TuningTable)target);
            }

            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
