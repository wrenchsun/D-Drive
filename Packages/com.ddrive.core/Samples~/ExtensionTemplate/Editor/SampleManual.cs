using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DDrive.Editor.Extension;

namespace SampleExtension.Editor
{
    // 「？」と ManualLauncher.OpenPage が開く外部のマニュアルページ(docs/1014 §2.4)。key は SampleEditor.ManualPage が返す名前と同じにする。
    // path はこのファイルの位置から求める(CallerFilePath)ので、フォルダごと動かしても、Package Manager の Import
    // (Assets/Samples/D-Drive/<版>/Extension Template/)でも合う。Documentation~ は Unity からは見えない(インポートされない)置き場で、ファイルとして開く。
    public sealed class SampleManual : IManualPageProvider
    {
        public const string PageKey = "sample-editor";

        public IReadOnlyList<(string key, string path, string title)> Pages => new[]
        {
            (PageKey, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile()) ?? string.Empty, "..", "Documentation~", "sample-editor.md")), "Sample Editor"),
        };

        private static string ThisFile([CallerFilePath] string file = "") => file;
    }
}
