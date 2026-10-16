using DDrive.Editor.Extension;

namespace SampleExtension.Editor
{
    // ヒーローの「確認用シーン」(docs/1014 §2.4)。この雛形はシーンを同梱しない。
    // ScenePath のシーンが無いと「確認用シーンが見つかりません: <パス>」と Console に案内が出るだけで、何も壊れない。
    // 使うときは、そのパスに確認用のシーン(ライト・カメラ・床など)を保存する。開いた直後に何か置きたければ Setup を書く。
    public sealed class SamplePreviewScene : IPreviewSceneProvider
    {
        public string KindKey => SampleKind.Key;

        public string ScenePath => "Assets/_Project/DDriveExtensions/Sample/Preview/SamplePreview.unity";
    }
}
