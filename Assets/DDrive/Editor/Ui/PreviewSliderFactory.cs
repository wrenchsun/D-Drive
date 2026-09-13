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

        public static UiSlider Create(Transform parent, string name, Vector2 anchoredPosition)
        {
            var trackGo = EditorPreviewRoots.CreateChild(parent, name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UiSlider));
            var trackRect = (RectTransform)trackGo.transform;
            trackRect.sizeDelta = TrackSize;
            trackRect.anchoredPosition = anchoredPosition;

            var fillGo = EditorPreviewRoots.CreateChild(trackGo.transform, "Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var handleGo = EditorPreviewRoots.CreateChild(trackGo.transform, "Handle", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.sizeDelta = HandleSize;

            var slider = trackGo.GetComponent<UiSlider>();
            slider.TargetGraphic = trackGo.GetComponent<UnityEngine.UI.Image>();
            slider.TrackRect = trackRect;
            slider.FillRect = fillRect;
            slider.HandleRect = handleRect;
            return slider;
        }
    }
}
