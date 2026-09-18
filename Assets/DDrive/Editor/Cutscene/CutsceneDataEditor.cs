using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Preview;
using DDrive.Runtime.Cutscene;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §3/§6(6-10d) — CutsceneData の Inspector 導線。編集 UI は Unity 標準の Timeline
    // ウィンドウ(独自 EditorWindow は持たない、[DataEditorRegistryTests] の Exempt 理由と同じ)なので、
    // [DataEditor] は付けない。代わりにここで「標準 Timeline ウィンドウを開く」「確認用シーンを開く」
    // 「バインド検査」「Play Mode 中の再生(実 Manager 経由)」の導線を提供する。
    [CustomEditor(typeof(CutsceneData))]
    public sealed class CutsceneDataEditor : AssetDataInspector
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawOpenEditorHeader();

            var cutscene = (CutsceneData)target;
            DrawTimelineButtons(cutscene);

            EditorGUILayout.Space(4);
            DrawBindingInspection(cutscene);

            EditorGUILayout.Space(4);
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawTimelineButtons(CutsceneData cutscene)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(cutscene.Timeline == null))
                {
                    if (GUILayout.Button("▶ Timeline ウィンドウで開く", GUILayout.Height(26)))
                    {
                        AssetDatabase.OpenAsset(cutscene.Timeline);
                    }
                }

                if (GUILayout.Button("▶ Cutscene確認用シーンを開く", GUILayout.Height(26)))
                {
                    CutscenePreviewSceneSetup.TryOpenOrCreate();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var harness = Object.FindFirstObjectByType<CutscenePreviewHarness>();
                var canPlay = Application.isPlaying && harness != null;

                using (new EditorGUI.DisabledScope(!canPlay))
                {
                    if (GUILayout.Button("● 再生(Play Mode)", GUILayout.Height(22)))
                    {
                        harness.Play(cutscene);
                    }

                    if (GUILayout.Button("Cancel", GUILayout.Height(22)))
                    {
                        harness.Cancel();
                    }

                    if (GUILayout.Button("Skip", GUILayout.Height(22)))
                    {
                        harness.Skip();
                    }
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "実際の Camera/SE/VFX 適用の確認には Play Mode が必要です(CutsceneManager は " +
                    "DDriveRuntimeBootstrap 経由でしか組み立てられません、[26_timeline.md] §4.4/§6)。" +
                    "確認用シーンを開いて Play ボタンを押してから、上の「再生」を押してください。",
                    MessageType.Info);
            }
            else if (Object.FindFirstObjectByType<CutscenePreviewHarness>() == null)
            {
                EditorGUILayout.HelpBox("シーンに CutscenePreviewHarness が見つかりません。「Cutscene確認用シーンを開く」で確認用シーンを開いてください。", MessageType.Info);
            }
        }

        // [26_timeline.md] §4.2(6-10d) — Bindings(役割名の一覧)と Timeline の実際のトラック名の食い違い
        // (typo・削除されたトラック・Binding未追加のトラック)を一覧で見せる。Validator には昇格させない
        // (このアシスト表示自体はデータ不整合の確定判定ではなく、目視の手助けに留める)。
        private static void DrawBindingInspection(CutsceneData cutscene)
        {
            EditorGUILayout.LabelField("バインド検査(Timeline のトラック名 ⇔ Bindings)", EditorStyles.boldLabel);

            if (cutscene.Timeline == null)
            {
                EditorGUILayout.HelpBox("Timeline が未設定のため検査できません。", MessageType.None);
                return;
            }

            var trackNames = new List<string>();
            foreach (var track in cutscene.Timeline.GetOutputTracks())
            {
                if (track != null)
                {
                    trackNames.Add(track.name);
                }
            }

            var boundNames = new HashSet<string>();
            if (cutscene.Bindings != null)
            {
                foreach (var binding in cutscene.Bindings)
                {
                    if (!string.IsNullOrEmpty(binding.TrackName))
                    {
                        boundNames.Add(binding.TrackName);
                    }
                }
            }

            foreach (var name in trackNames)
            {
                var bound = boundNames.Contains(name);
                EditorGUILayout.LabelField(bound ? $"✓ {name}" : $"✗ {name}(Bindings に未登録)");
            }

            if (cutscene.Bindings != null)
            {
                foreach (var binding in cutscene.Bindings)
                {
                    if (!string.IsNullOrEmpty(binding.TrackName) && !trackNames.Contains(binding.TrackName))
                    {
                        EditorGUILayout.HelpBox($"Bindings の '{binding.TrackName}' に一致する Timeline トラックがありません(削除された可能性)。", MessageType.Warning);
                    }
                }
            }
        }
    }
}
