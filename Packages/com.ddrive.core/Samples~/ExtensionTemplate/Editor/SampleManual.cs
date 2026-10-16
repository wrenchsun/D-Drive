using System.Collections.Generic;
using DDrive.Editor.Extension;

namespace SampleExtension.Editor
{
    // 「？」と ManualLauncher.OpenPage が開く外部のマニュアルページ(docs/1014 §2.4)。key は SampleEditor.ManualPage が返す名前と同じにする。
    // path はプロジェクト基準。Documentation~ は Unity からは見えない(インポートされない)置き場で、ファイルとして開く。
    public sealed class SampleManual : IManualPageProvider
    {
        public const string PageKey = "sample-editor";

        public IReadOnlyList<(string key, string path, string title)> Pages => new[]
        {
            (PageKey, "Assets/_Project/DDriveExtensions/Sample/Documentation~/sample-editor.md", "Sample Editor"),
        };
    }
}
