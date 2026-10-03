using System.Collections.Generic;
using DDrive.Runtime.Cutscene;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §4.2 / [51_tdrive_integration.md] §4.2(FC-1) — CutsceneData.Bindings の 1 要素の Drawer。
    // Target に応じて入力欄を出し分ける(Model = SpawnModel のみ / SceneObjectName = SceneObjectByName・AnchorPoint のみ /
    // SourceTrackName = SameAsTrack のみ。SameAsTrack は Bindings のトラック名から選ぶ)。
    // Editor 専用の見た目だけで、シリアライズ形式は変えない(非表示の項目の値は保持する)。
    // CutsceneDataEditor は IMGUI(DrawPropertiesExcluding)なので IMGUI 実装にする。
    [CustomPropertyDrawer(typeof(CutsceneBinding))]
    internal sealed class CutsceneBindingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var line = EditorGUIUtility.singleLineHeight;
            var gap = EditorGUIUtility.standardVerticalSpacing;
            if (!property.isExpanded)
            {
                return line;
            }

            var height = line + gap; // foldout
            height += line + gap;    // TrackName
            height += line + gap;    // Target

            switch ((CutsceneBindTarget)property.FindPropertyRelative(nameof(CutsceneBinding.Target)).enumValueIndex)
            {
                case CutsceneBindTarget.SpawnModel:
                    height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(nameof(CutsceneBinding.Model)), true) + gap;
                    break;
                case CutsceneBindTarget.SceneObjectByName:
                case CutsceneBindTarget.AnchorPoint:
                    height += line + gap;
                    break;
                case CutsceneBindTarget.SameAsTrack:
                    height += line + gap;
                    break;
            }

            return height - gap;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = EditorGUIUtility.singleLineHeight;
            var gap = EditorGUIUtility.standardVerticalSpacing;
            var trackNameProp = property.FindPropertyRelative(nameof(CutsceneBinding.TrackName));
            var targetProp = property.FindPropertyRelative(nameof(CutsceneBinding.Target));

            EditorGUI.BeginProperty(position, label, property);
            var rect = new Rect(position.x, position.y, position.width, line);
            var title = string.IsNullOrEmpty(trackNameProp.stringValue)
                ? label.text
                : $"{label.text}: {trackNameProp.stringValue} ({targetProp.enumDisplayNames[targetProp.enumValueIndex]})";
            property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, title, true);
            EditorGUI.EndProperty();
            if (!property.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            rect.y += line + gap;
            EditorGUI.PropertyField(rect, trackNameProp);
            rect.y += line + gap;
            EditorGUI.PropertyField(rect, targetProp);
            rect.y += line + gap;

            switch ((CutsceneBindTarget)targetProp.enumValueIndex)
            {
                case CutsceneBindTarget.SpawnModel:
                    var modelProp = property.FindPropertyRelative(nameof(CutsceneBinding.Model));
                    rect.height = EditorGUI.GetPropertyHeight(modelProp, true);
                    EditorGUI.PropertyField(rect, modelProp, true);
                    break;

                case CutsceneBindTarget.SceneObjectByName:
                case CutsceneBindTarget.AnchorPoint:
                    EditorGUI.PropertyField(rect, property.FindPropertyRelative(nameof(CutsceneBinding.SceneObjectName)));
                    break;

                case CutsceneBindTarget.SameAsTrack:
                    DrawSourceTrack(rect, property.FindPropertyRelative(nameof(CutsceneBinding.SourceTrackName)), property.serializedObject.targetObject as CutsceneData);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        // 参照先の候補 = Bindings に登録済みのトラック名(SourceTrackName は Bindings の TrackName と照合されるため)。
        private static void DrawSourceTrack(Rect rect, SerializedProperty sourceProp, CutsceneData cutscene)
        {
            var choices = new List<string>();
            if (cutscene != null && cutscene.Bindings != null)
            {
                foreach (var b in cutscene.Bindings)
                {
                    if (!string.IsNullOrEmpty(b.TrackName) && !choices.Contains(b.TrackName))
                    {
                        choices.Add(b.TrackName);
                    }
                }
            }

            var label = new GUIContent("Source Track Name", "同じ相手にバインドする別トラックの TrackName(Target=SameAsTrack)。");
            if (choices.Count == 0)
            {
                EditorGUI.PropertyField(rect, sourceProp, label);
                return;
            }

            var current = sourceProp.stringValue ?? string.Empty;
            if (current.Length > 0 && !choices.Contains(current))
            {
                choices.Add(current); // 候補に無い(typo / 削除済み)値も消さずに見せる。検査・Validator で警告される。
            }

            var index = current.Length > 0 ? choices.IndexOf(current) : -1;
            choices.Insert(0, "(未設定)");
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.Popup(rect, label.text, index + 1, choices.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                sourceProp.stringValue = picked <= 0 ? string.Empty : choices[picked];
            }
        }
    }
}
