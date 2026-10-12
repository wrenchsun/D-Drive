using UnityEngine;

namespace DDrive.Runtime.Ui
{
    // [07_canvas_prefab.md] 2026-10-12 追記(C-1) — UiManager のレイヤールート Canvas に付ける CanvasScaler の既定
    // (ScaleWithScreenSize)。Open した Prefab に CanvasScaler があればその設定が優先され、無いときだけこの値を使う。
    // Editor では Project Settings > D-Drive > Canvas の値が起動時に反映される(ランタイムには ProjectSettings が無いため)。
    // 製品ビルドで変えたいときはゲーム起動時(最初の Ui.Open の前)にこのクラスを直接設定する。
    public static class UiCanvasScalerDefaults
    {
        public const float FallbackReferenceWidth = 1920f;
        public const float FallbackReferenceHeight = 1080f;
        public const float FallbackMatch = 0.5f;

        private static Vector2 _referenceResolution = new Vector2(FallbackReferenceWidth, FallbackReferenceHeight);
        private static float _match = FallbackMatch;

        public static Vector2 ReferenceResolution
        {
            get => _referenceResolution;
            set => _referenceResolution = value.x > 0f && value.y > 0f
                ? value
                : new Vector2(FallbackReferenceWidth, FallbackReferenceHeight);
        }

        public static float Match
        {
            get => _match;
            set => _match = Mathf.Clamp01(value);
        }
    }
}
