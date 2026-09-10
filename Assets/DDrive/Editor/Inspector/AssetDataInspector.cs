using DDrive.Foundation.Data;
using UnityEditor;

namespace DDrive.Editor.Inspector
{
    // [09_editor_tools.md] §8 — 全 Data アセット共通の Inspector。最上部に「〜で開く」ボタン(DataEditorRegistry)を出し、
    // その下は既定の描画。種別ごとに独自 Inspector を作る場合はこのクラスを継承し、先頭で DrawOpenEditorHeader() を呼ぶ
    // (SeDataEditor 参照)。editorForChildClasses=true なので、今後追加される Data 種別にも自動で適用される。
    [CustomEditor(typeof(AssetDataBase), true)]
    [CanEditMultipleObjects]
    public class AssetDataInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawOpenEditorHeader();
            DrawDefaultInspector();
        }

        // 「〜で開く」ボタン列 + アイコン行(フォルダから選択 / シーンから作成)。
        protected void DrawOpenEditorHeader()
        {
            if (targets.Length == 1)
            {
                DataEditorHeader.Draw(target as AssetDataBase);
                AssetIconGui.Draw(target as AssetDataBase);
            }
        }
    }
}
