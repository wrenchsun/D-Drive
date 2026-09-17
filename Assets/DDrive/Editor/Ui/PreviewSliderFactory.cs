using DDrive.Editor.Preview;
using DDrive.Runtime.Ui;
using UnityEngine;

namespace DDrive.Editor.Ui
{
    // (レビュー対応 2026-09-14) 確認用シーンに置くプレビュー用 UiSlider(Track + Fill + Handle)の組み立て。
    // SliderSkinEditorWindow と SliderEditorWindow が同じ組み立てを別々に持ち、どちらも子(Fill / Handle 等)を
    // HideFlags.None で作っていた(プレビューを置いたままシーンを保存すると子だけが親無しで保存される)。
    // ここでは全ての GameObject を DontSave で作る。SetVisual 等で実行時コンポーネントが子を足す場合に備え、
    // 呼び出し側は組み立て後に EditorPreviewRoots.MarkDontSaveRecursive(ルート) も掛けること。
    public static class PreviewSliderFactory
    {
        public static readonly Vector2 TrackSize = new(300f, 24f);
        public static readonly Vector2 HandleSize = new(20f, 24f);

        // dontSave=false は U-18(2026-09-17)の Hierarchy 右クリック配置用。プレビューではなく「実際にシーンへ残す
        // UiSlider」を作る場合だけ false にする(組み立て = Track + Fill + Handle の定義をここ 1 箇所に保つため、
        // 配置用に別の組み立てコードを作らない)。
        public static UiSlider Create(Transform parent, string name, Vector2 anchoredPosition, bool dontSave = true)
        {
            var trackGo = CreateGo(parent, name, dontSave, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiSlider));
            var trackRect = (RectTransform)trackGo.transform;
            trackRect.sizeDelta = TrackSize;
            trackRect.anchoredPosition = anchoredPosition;

            var fillGo = CreateGo(trackGo.transform, "Fill", dontSave, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var handleGo = CreateGo(trackGo.transform, "Handle", dontSave, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.sizeDelta = HandleSize;

            var slider = trackGo.GetComponent<UiSlider>();
            slider.TargetGraphic = trackGo.GetComponent<UnityEngine.UI.Image>();
            slider.TrackRect = trackRect;
            slider.FillRect = fillRect;
            slider.HandleRect = handleRect;
            return slider;
        }

        private static GameObject CreateGo(Transform parent, string name, bool dontSave, params System.Type[] components)
        {
            if (dontSave)
            {
                return EditorPreviewRoots.CreateChild(parent, name, components);
            }

            var go = new GameObject(name, components);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }
    }
}
