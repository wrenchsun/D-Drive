using System;
using DDrive.Foundation.Identity;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace DDrive.Editor.Inspectors
{
    // [09_editor_tools.md] §3 — 検索付きドロップダウン + 未登録/削除済みIDの赤表示。
    // 「Browserで開く」導線は AssetBrowser(1-5)が出来てから拡張する(現状はPing代替)。
    [CustomPropertyDrawer(typeof(AssetId<>), true)]
    public sealed class AssetIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var valueProp = property.FindPropertyRelative("value");
            var typeProp = property.FindPropertyRelative("type");

            var markerType = GetMarkerType();
            var candidates = markerType != null ? AssetIdLookup.GetCandidates(markerType) : Array.Empty<AssetIdLookup.Candidate>();
            var currentValue = valueProp.ulongValue;

            var isKnown = false;
            var displayName = string.Empty;
            foreach (var candidate in candidates)
            {
                if (candidate.Id == currentValue)
                {
                    isKnown = true;
                    displayName = candidate.DisplayName;
                    break;
                }
            }

            var fieldRect = EditorGUI.PrefixLabel(position, label);

            var prevColor = GUI.color;
            if (currentValue != 0 && !isKnown)
            {
                GUI.color = Color.red;
            }

            var buttonLabel = currentValue == 0
                ? "<None>"
                : (isKnown ? displayName : $"<Unregistered 0x{currentValue:X}>");

            if (EditorGUI.DropdownButton(fieldRect, new GUIContent(buttonLabel), FocusType.Keyboard))
            {
                ShowDropdown(fieldRect, candidates, property);
            }

            GUI.color = prevColor;
            EditorGUI.EndProperty();
        }

        private Type GetMarkerType()
        {
            var fieldType = fieldInfo.FieldType;

            if (fieldType.IsArray)
            {
                fieldType = fieldType.GetElementType();
            }
            else if (fieldType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(fieldType) && fieldType != typeof(string))
            {
                var args = fieldType.GetGenericArguments();
                if (args.Length == 1)
                {
                    fieldType = args[0];
                }
            }

            if (fieldType != null && fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetId<>))
            {
                return fieldType.GetGenericArguments()[0];
            }

            return null;
        }

        private static void ShowDropdown(Rect rect, AssetIdLookup.Candidate[] candidates, SerializedProperty property)
        {
            var targetObject = property.serializedObject.targetObject;
            var propertyPath = property.propertyPath;

            var dropdown = new AssetIdAdvancedDropdown(new AdvancedDropdownState(), candidates, selected =>
            {
                // 選択コールバックは遅延実行される。対象オブジェクトの削除や配列要素の除去で
                // プロパティが消えている可能性があるため、無効なら黙って何もしない。
                if (targetObject == null)
                {
                    return;
                }

                var so = new SerializedObject(targetObject);
                var prop = so.FindProperty(propertyPath);
                if (prop == null)
                {
                    return;
                }

                so.UpdateIfRequiredOrScript();
                prop.FindPropertyRelative("value").ulongValue = selected?.Id ?? 0UL;
                if (selected.HasValue)
                {
                    prop.FindPropertyRelative("type").enumValueIndex = (int)selected.Value.Type;
                }

                so.ApplyModifiedProperties();
            });

            dropdown.Show(rect);
        }

        private sealed class AssetIdAdvancedDropdown : AdvancedDropdown
        {
            private readonly AssetIdLookup.Candidate[] _candidates;
            private readonly Action<AssetIdLookup.Candidate?> _onSelect;

            public AssetIdAdvancedDropdown(AdvancedDropdownState state, AssetIdLookup.Candidate[] candidates, Action<AssetIdLookup.Candidate?> onSelect)
                : base(state)
            {
                _candidates = candidates;
                _onSelect = onSelect;
                minimumSize = new Vector2(240, 300);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Select Asset");
                root.AddChild(new Item("<None>", null));

                foreach (var candidate in _candidates)
                {
                    root.AddChild(new Item(candidate.DisplayName, candidate));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is Item idItem)
                {
                    _onSelect(idItem.Candidate);
                }
            }

            private sealed class Item : AdvancedDropdownItem
            {
                public readonly AssetIdLookup.Candidate? Candidate;

                public Item(string name, AssetIdLookup.Candidate? candidate) : base(name)
                {
                    Candidate = candidate;
                }
            }
        }
    }
}
