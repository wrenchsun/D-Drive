using UnityEngine.SceneManagement;

namespace DDrive.Editor.Extension
{
    // [1014_extension_platform.md] §2.4(X-4、2026-10-16) — 持ち込み先が定義した種別の確認用シーンを出す拡張点。
    // UxEditorWindow<TData> のヒーローに「確認用シーン」ボタンが付き(KindKey が同じ provider があるときだけ。無ければ出さない)、
    // 押すと ScenePath のシーンを開く(開く前に未保存のシーンの確認をする。組み込み種別の確認用シーンと同じ)。
    // 組み込み種別の確認用シーン(PreviewPlacementButton)には影響しない。発見・重複の扱いは ISampleProvider と同じ。
    public interface IPreviewSceneProvider
    {
        string KindKey { get; }

        // 開くシーンのパス(プロジェクト基準、例 "Assets/MyGame/Preview/LightPreview.unity")。無ければ警告して何もしない。
        string ScenePath { get; }

        // 開いた直後の追加配置(任意。既定は何もしない)。例外は握りつぶして警告。
        void Setup(Scene scene) { }
    }
}
