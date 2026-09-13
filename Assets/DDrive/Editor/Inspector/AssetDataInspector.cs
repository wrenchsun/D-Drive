using DDrive.Foundation.Data;
using UnityEditor;
using UnityEngine;

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

        // [09_editor_tools.md] §8.2 — Project ウィンドウのグリッド表示サムネイル(5-10)。
        // Icon が設定されていれば要求サイズに縮小して返す(全 Data 型共通。種別独自の Inspector も本クラスを
        // 継承していれば自動で効く。SeDataEditor 参照)。未設定なら既定の動作(スクリプトアイコン)に委ねる。
        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var icon = (target as AssetDataBase)?.Icon;
            return icon != null
                ? AssetIconService.ScaleForPreview(icon, width, height)
                : base.RenderStaticPreview(assetPath, subAssets, width, height);
        }
    }
}
