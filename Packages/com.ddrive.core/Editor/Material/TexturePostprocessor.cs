using UnityEditor;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] B-3 — インポート規約(命名 → 自動設定)を実際のインポート時に適用する(チケット 3-8、2026-09-10)。
    // TextureImportProfile.AppliesTo/TryMatch で対象と規約を決め、TextureImportProfile.Apply で書き込む。
    // ここでは SaveAndReimport は呼ばない(OnPreprocessTexture の途中でやるべきではない。設定はこのインポート自体に反映される)。
    public sealed class TexturePostprocessor : AssetPostprocessor
    {
        // テストから一時的に無効化するためのスイッチ(通常運用では常に true)。
        public static bool Suppress;

        public const string Version = "1.0";

        private void OnPreprocessTexture()
        {
            if (Suppress)
            {
                return;
            }

            var profile = TextureImportProfile.FindOrDefault();
            if (profile == null || !profile.Enabled || !profile.AppliesTo(assetPath))
            {
                return;
            }

            if (!profile.TryMatch(assetPath, out var rule))
            {
                return;
            }

            var importer = assetImporter as TextureImporter;
            TextureImportProfile.Apply(importer, rule);
        }
    }
}
