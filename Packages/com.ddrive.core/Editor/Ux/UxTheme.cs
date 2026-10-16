using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // UxEditorWindow を継承しない窓(ウィザードなど)に共通テーマ(DDriveUx.uss)を当てる(docs/1010 UX-7)。
    // root に .ux-root と Light / Dark のクラスを付ける。Light / Dark が切り替わったかは IsStale で見る(窓が自分で作り直す)。
    public static class UxTheme
    {
        private const string SheetName = "DDriveUx";
        private static StyleSheet _sheet;

        public static void Apply(VisualElement root)
        {
            if (_sheet == null)
            {
                var guids = AssetDatabase.FindAssets(SheetName + " t:StyleSheet");
                for (var i = 0; i < guids.Length && _sheet == null; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (path.EndsWith(SheetName + ".uss", StringComparison.Ordinal))
                    {
                        _sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    }
                }
            }

            if (_sheet != null && !root.styleSheets.Contains(_sheet))
            {
                root.styleSheets.Add(_sheet);
            }

            root.AddToClassList("ux-root");
            var pro = EditorGUIUtility.isProSkin;
            root.EnableInClassList("ux-dark", pro);
            root.EnableInClassList("ux-light", !pro);
        }

        // 適用したときと Light / Dark が変わっているか。
        public static bool IsStale(VisualElement root) => root.ClassListContains("ux-dark") != EditorGUIUtility.isProSkin;
    }
}
