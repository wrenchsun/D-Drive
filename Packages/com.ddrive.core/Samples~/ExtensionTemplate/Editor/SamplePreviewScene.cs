using System.IO;
using System.Runtime.CompilerServices;
using DDrive.Editor.Extension;
using UnityEngine;

namespace SampleExtension.Editor
{
    // ヒーローの「確認用シーン」(docs/1014 §2.4)。この雛形はシーンを同梱しない。
    // ScenePath のシーンが無いと「確認用シーンが見つかりません: <パス>」と Console に案内が出るだけで、何も壊れない。
    // 使うときは、そのパスに確認用のシーン(ライト・カメラ・床など)を保存する。開いた直後に何か置きたければ Setup を書く。
    // パスはこのファイルの位置から求める(フォルダごと動かしても、Package Manager の Import でも合う)。Assets の外(Samples~ の中)なら固定の既定パス。
    public sealed class SamplePreviewScene : IPreviewSceneProvider
    {
        private const string Fallback = "Assets/_Project/DDriveExtensions/Sample/Preview/SamplePreview.unity";

        public string KindKey => SampleKind.Key;

        public string ScenePath => Resolve();

        private static string Resolve()
        {
            var dir = Path.GetDirectoryName(ThisFile());
            if (string.IsNullOrEmpty(dir))
            {
                return Fallback;
            }

            var parent = Path.GetFullPath(Path.Combine(dir, "..")).Replace('\\', '/');
            var assets = Application.dataPath.Replace('\\', '/');
            return parent.StartsWith(assets + "/", System.StringComparison.OrdinalIgnoreCase)
                ? "Assets/" + parent.Substring(assets.Length + 1) + "/Preview/SamplePreview.unity"
                : Fallback;
        }

        private static string ThisFile([CallerFilePath] string file = "") => file;
    }
}
