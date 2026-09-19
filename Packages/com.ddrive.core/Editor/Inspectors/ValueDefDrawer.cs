using DDrive.Foundation.Values;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DDrive.Editor.Inspectors
{
    // [17_value_definition.md] §5 — 定数/曲線/カーブの共通 Drawer。以降の全エディタで使い回す。
    //
    // 手動 Rect + GetPropertyHeight の IMGUI 実装(ミニグラフ/スクラブ付き)から UI Toolkit の
    // CreatePropertyGUI へ全面的に書き直した(2026-07-27)。IMGUI 版は AnimationCurve フィールドの
    // カーブエディタを開いた際にクラッシュする不具合があり、原因は手動 Rect/高さ計算と
    // カーブエディタのポップアップ表示が絡む相性問題と見られる。UI Toolkit の PropertyField は
    // レイアウトを自前計算しないため、この種の不整合が構造的に起きない。
    // ミニグラフ・スクラブ表示は本書き直しで一旦落としている(1-6/1-7 の実 Manager 駆動プレビューで
    // 改めて実装する)。モード切替でのデータ保持・Undo対応は従来通り(SerializedProperty 経由)。
    [CustomPropertyDrawer(typeof(ValueDef))]
    public sealed class ValueDefDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var foldout = new Foldout { text = property.displayName, value = property.isExpanded };
            foldout.RegisterValueChangedCallback(evt => property.isExpanded = evt.newValue);
            root.Add(foldout);

            var modeProp = property.FindPropertyRelative("Mode");
            var modeField = new PropertyField(modeProp, "モード");
            foldout.Add(modeField);

            var constantField = new PropertyField(property.FindPropertyRelative("Constant"), "定数");
            foldout.Add(constantField);

            var paramProp = property.FindPropertyRelative("Parametric");
            var kindProp = paramProp.FindPropertyRelative("Kind");
            var parametricContainer = new VisualElement();
            var kindField = new PropertyField(kindProp, "Parametric種別");
            var easeField = new PropertyField(paramProp.FindPropertyRelative("Ease"), "Ease");
            var p1Field = new PropertyField(paramProp.FindPropertyRelative("BezierP1"), "Bezier P1");
            var p2Field = new PropertyField(paramProp.FindPropertyRelative("BezierP2"), "Bezier P2");
            parametricContainer.Add(kindField);
            parametricContainer.Add(easeField);
            parametricContainer.Add(p1Field);
            parametricContainer.Add(p2Field);
            parametricContainer.Add(new PropertyField(property.FindPropertyRelative("From"), "From"));
            parametricContainer.Add(new PropertyField(property.FindPropertyRelative("To"), "To"));
            foldout.Add(parametricContainer);

            var normalizedProp = property.FindPropertyRelative("Normalized");
            var curveContainer = new VisualElement();
            var curveField = new PropertyField(property.FindPropertyRelative("Curve"), "カーブ");
            var normalizedField = new PropertyField(normalizedProp, "Normalized");
            var curveFromField = new PropertyField(property.FindPropertyRelative("From"), "From");
            var curveToField = new PropertyField(property.FindPropertyRelative("To"), "To");
            curveContainer.Add(curveField);
            curveContainer.Add(normalizedField);
            curveContainer.Add(curveFromField);
            curveContainer.Add(curveToField);
            foldout.Add(curveContainer);

            var timeProp = property.FindPropertyRelative("Time");
            foldout.Add(new PropertyField(timeProp.FindPropertyRelative("Mode"), "Time Mode"));
            foldout.Add(new PropertyField(timeProp.FindPropertyRelative("Value"), "Value"));
            foldout.Add(new PropertyField(timeProp.FindPropertyRelative("SpeedScale"), "Speed"));
            foldout.Add(new PropertyField(timeProp.FindPropertyRelative("IgnoreTimeScale"), "Ignore TimeScale"));
            foldout.Add(new PropertyField(property.FindPropertyRelative("Loop"), "Loop"));
            foldout.Add(new PropertyField(property.FindPropertyRelative("LoopCount"), "Loop Count"));

            void UpdateVisibility()
            {
                var mode = (ValueMode)modeProp.enumValueIndex;
                constantField.style.display = mode == ValueMode.Constant ? DisplayStyle.Flex : DisplayStyle.None;
                parametricContainer.style.display = mode == ValueMode.Parametric ? DisplayStyle.Flex : DisplayStyle.None;
                curveContainer.style.display = mode == ValueMode.Curve ? DisplayStyle.Flex : DisplayStyle.None;

                var isBezier = (ParametricKind)kindProp.enumValueIndex == ParametricKind.CustomBezier;
                easeField.style.display = isBezier ? DisplayStyle.None : DisplayStyle.Flex;
                p1Field.style.display = isBezier ? DisplayStyle.Flex : DisplayStyle.None;
                p2Field.style.display = isBezier ? DisplayStyle.Flex : DisplayStyle.None;

                var normalized = normalizedProp.boolValue;
                curveFromField.style.display = normalized ? DisplayStyle.None : DisplayStyle.Flex;
                curveToField.style.display = normalized ? DisplayStyle.None : DisplayStyle.Flex;
            }

            modeField.RegisterValueChangeCallback(_ => UpdateVisibility());
            kindField.RegisterValueChangeCallback(_ => UpdateVisibility());
            normalizedField.RegisterValueChangeCallback(_ => UpdateVisibility());
            UpdateVisibility();

            return root;
        }
    }
}
