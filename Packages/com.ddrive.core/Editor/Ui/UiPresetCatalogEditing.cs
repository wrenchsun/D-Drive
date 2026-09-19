using DDrive.Runtime.Ui;
using UnityEditor;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-3.5(4-12)「独自プリセットとして登録」の実体。UiPresetCatalog.Entries への
    // 追加/更新(同名なら上書き)だけを行う純粋な配列操作(UiPresetGalleryWindow / テストの双方から使う)。
    public static class UiPresetCatalogEditing
    {
        public static void Register(UiPresetCatalog catalog, string name, UiTweenData tween, string category)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(name) || tween == null)
            {
                return;
            }

            Undo.RecordObject(catalog, "UiPresetCatalog: プリセットを登録");

            var entries = catalog.Entries ?? System.Array.Empty<UiPresetCatalog.Entry>();
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Name == name)
                {
                    entries[i] = new UiPresetCatalog.Entry { Name = name, Tween = tween, Category = category };
                    catalog.Entries = entries;
                    EditorUtility.SetDirty(catalog);
                    return;
                }
            }

            var next = new UiPresetCatalog.Entry[entries.Length + 1];
            System.Array.Copy(entries, next, entries.Length);
            next[entries.Length] = new UiPresetCatalog.Entry { Name = name, Tween = tween, Category = category };
            catalog.Entries = next;
            EditorUtility.SetDirty(catalog);
        }
    }
}
