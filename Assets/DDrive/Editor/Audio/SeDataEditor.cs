using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Audio
{
    // [03_audio.md] §2 の非破壊トリミング(Source→Clips ベイク)・無音自動検出・再生開始位置の GUI。
    // 波形表示・ドラッグでのトリム編集は 1-7 AudioEditor で拡張する(ここは数値入力ベースの最小GUI)。
    [CustomEditor(typeof(SeData))]
    public sealed class SeDataEditor : UnityEditor.Editor
    {
        private const float SilenceThreshold = 0.01f;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", "Sources");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("元データ・トリミング(非破壊)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Source は常に元のインポート済みクリップのまま保持されます。TrimStart/TrimEnd を調整して" +
                "「トリミングを適用」を押すと、Clips がトリム済みコピーで再生成されます(Source 自体は変更されません)。",
                MessageType.Info);

            var sourcesProp = serializedObject.FindProperty("Sources");
            EditorGUILayout.PropertyField(sourcesProp, true);

            var data = (SeData)target;

            if (data.Sources != null)
            {
                for (var i = 0; i < data.Sources.Length; i++)
                {
                    var entry = data.Sources[i];
                    if (entry.Source == null)
                    {
                        continue;
                    }

                    if (GUILayout.Button($"無音自動トリミング検出 → Sources[{i}] ({entry.Source.name})"))
                    {
                        if (AudioClipTrimUtility.TryDetectSilenceTrim(entry.Source, SilenceThreshold, out var start, out var end))
                        {
                            entry.TrimStartSec = start;
                            entry.TrimEndSec = end;
                            data.Sources[i] = entry;
                            EditorUtility.SetDirty(data);
                        }
                        else
                        {
                            Debug.LogWarning($"[DDrive] Could not read PCM data from '{entry.Source.name}' for silence detection.");
                        }
                    }
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("トリミングを適用(Clips を再生成)", GUILayout.Height(24)))
                {
                    SeTrimApplier.Apply(data);
                }

                if (GUILayout.Button("AudioEditor で開く(波形編集)", GUILayout.Height(20)))
                {
                    AudioEditorWindow.Open(data);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
