using System.Text.RegularExpressions;

namespace DDrive.Editor.Anim2D
{
    // 命名規則の構築と、画像名末尾からの角度抽出を担当。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.NamingRuleResolver(ロジックは同一)。
    //
    // 命名規則:
    //   方向あり : Anim_{name}_{state}_{angle}
    //   方向なし : Anim_{name}_{state}
    public static class NamingRuleResolver
    {
        // 画像名末尾の "_0" "_45" ... "_315" を抽出。
        private static readonly Regex SuffixAngleRegex =
            new(@"_(?<angle>0|45|90|135|180|225|270|315)$", RegexOptions.Compiled);

        // 画像名末尾から角度(0/45/.../315)を抽出。該当しない場合は false。
        public static bool TryExtractAngleFromName(string textureName, out int angle)
        {
            angle = -1;
            if (string.IsNullOrEmpty(textureName))
            {
                return false;
            }

            var m = SuffixAngleRegex.Match(textureName);
            if (!m.Success)
            {
                return false;
            }

            return int.TryParse(m.Groups["angle"].Value, out angle);
        }

        // AnimationClip のファイル名(拡張子抜き)を構築。angle が負なら方向なし表記にする。
        public static string BuildClipName(string name, string state, int angle)
        {
            var baseName = $"Anim_{name}_{state}";
            return angle < 0 ? baseName : $"{baseName}_{angle}";
        }
    }
}
