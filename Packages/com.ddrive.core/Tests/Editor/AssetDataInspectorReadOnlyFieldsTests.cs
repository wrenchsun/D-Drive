using DDrive.Editor.Inspector;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Material;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Tests.Editor
{
    // [09_editor_tools.md] §8.6 / [39_usability_fixes_2026-09-17.md] U-11 —
    // AssetDataBase 共通フィールド(Id/Version/Author/UpdatedAt)は [InspectorReadOnly] が付いており、
    // AssetDataInspector.CreateInspectorGUI が生成する PropertyField を disabled にすること、
    // 他の編集可能フィールド(DisplayName 等)はそのまま有効であることを検証する。
    public class AssetDataInspectorReadOnlyFieldsTests
    {
        [Test]
        public void CreateInspectorGUI_MarksCommonAutoManagedFieldsDisabled()
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            var editor = UnityEditor.Editor.CreateEditor(data);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, "TextureData には独自 Inspector が無いので UI Toolkit の本文が返る");

                AssertFieldDisabled(root, "Id", true);
                AssertFieldDisabled(root, "Version", true);
                AssertFieldDisabled(root, "Author", true);
                AssertFieldDisabled(root, "UpdatedAt", true);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void CreateInspectorGUI_LeavesOrdinaryFieldsEditable()
        {
            var data = ScriptableObject.CreateInstance<TextureData>();
            var editor = UnityEditor.Editor.CreateEditor(data);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root);

                AssertFieldDisabled(root, "DisplayName", false);
                AssertFieldDisabled(root, "Description", false);
                AssertFieldDisabled(root, "ChangeNote", false);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }

        // 種別独自の Inspector(SeDataEditor)は OnInspectorGUI を上書きしているので CreateInspectorGUI は
        // null を返し、この機構(UI Toolkit の disabled 化)は素通りする。IMGUI 側は SeDataEditor が
        // 自前で描くフィールドしか対象にしないため、ここでは「null が返ること」だけ確認する
        // (SeDataEditor 自体の描画内容は既存の SeDataEditor 関連テストの範囲)。
        [Test]
        public void CreateInspectorGUI_OnSubclassWithOwnOnInspectorGui_ReturnsNull()
        {
            var data = ScriptableObject.CreateInstance<SeData>();
            var editor = UnityEditor.Editor.CreateEditor(data);
            try
            {
                Assert.IsInstanceOf<AssetDataInspector>(editor);
                Assert.IsNull(editor.CreateInspectorGUI(), "SeDataEditor は OnInspectorGUI を上書きしているので IMGUI 経路に戻す");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }

        private static void AssertFieldDisabled(UnityEngine.UIElements.VisualElement root, string bindingPath, bool expectDisabled)
        {
            PropertyField found = null;
            root.Query<PropertyField>().ForEach(field =>
            {
                if (found == null && field.bindingPath == bindingPath)
                {
                    found = field;
                }
            });

            Assert.IsNotNull(found, $"PropertyField(bindingPath=\"{bindingPath}\") が見つかりません");
            Assert.AreEqual(!expectDisabled, found.enabledSelf,
                $"\"{bindingPath}\" の disabled 状態が想定と異なります(expectDisabled={expectDisabled})");
        }
    }
}
