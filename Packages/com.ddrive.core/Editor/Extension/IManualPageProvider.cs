using System.Collections.Generic;

namespace DDrive.Editor.Extension
{
    // [1014_extension_platform.md] §2.4(X-4、2026-10-16) — 持ち込み先のマニュアルページ(HTML / Markdown)を ManualLauncher に載せる拡張点。
    // ManualLauncher.OpenPage(key) と、専用エディターのカードの「?」(UxEditorWindow.ManualPage / ManualAnchor が返すページ名)が、
    // 組み込みのページ名(docs/DesignerManual)に無い key を、ここで宣言されたページから探して開く(組み込みのページが先)。
    // 発見は ExtensionProviders(重複した key は警告 1 回 + 先勝ち)。
    public interface IManualPageProvider
    {
        // key = ページ名(UxEditorWindow.ManualPage が返す文字列と同じ)、path = ファイルのパス(プロジェクト基準。相対はプロジェクトのルートから。
        // 絶対パスも可)、title = 表示名。
        IReadOnlyList<(string key, string path, string title)> Pages { get; }
    }
}
