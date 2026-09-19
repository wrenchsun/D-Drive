using System;
using System.Text.RegularExpressions;

namespace DDrive.Editor.Cutscene
{
    // [26_timeline.md] §5.1/§5.4/§5.5(6-10c) — 「1 ショット = FBX セット」の命名規則を解釈する純ロジック
    // (AssetDatabase 非依存。AssetNamingService と同じ方針でテスト可能に保つ)。
    // ルール: "<ショット>.fbx" = カメラ + 小物 / "<ショット>__<Model識別子>.fbx" = キャラごとの骨アニメ。
    public static class CutsceneShotParser
    {
        private static readonly Regex DuplicateSuffixPattern = new(@"^(.*)_(\d+)$", RegexOptions.Compiled);

        // ファイル名(拡張子無し)から「ショット名」「キャラ FBX かどうか」「Model 識別子(生、_2 等の末尾を含む)」を得る。
        public static void ParseFileName(string fileNameNoExt, out string shot, out bool isCharacter, out string modelIdentifierRaw)
        {
            fileNameNoExt ??= string.Empty;
            var sep = fileNameNoExt.IndexOf("__", StringComparison.Ordinal);
            if (sep < 0)
            {
                shot = fileNameNoExt;
                isCharacter = false;
                modelIdentifierRaw = null;
                return;
            }

            shot = fileNameNoExt.Substring(0, sep);
            isCharacter = true;
            modelIdentifierRaw = fileNameNoExt.Substring(sep + 2);
        }

        // "Hero_2" → "Hero"([26] §5.4「同じキャラ 2 体は末尾に _2 以降。ModelData は Hero を引く」)。
        // 末尾が数字サフィックスでなければそのまま返す。
        public static string StripDuplicateSuffix(string modelIdentifierRaw)
        {
            if (string.IsNullOrEmpty(modelIdentifierRaw))
            {
                return modelIdentifierRaw;
            }

            var match = DuplicateSuffixPattern.Match(modelIdentifierRaw);
            return match.Success ? match.Groups[1].Value : modelIdentifierRaw;
        }

        // "PRP_Sword" / "Sword:root" のような小物のノード名 → Model 識別子候補("Sword")。
        // 名前空間区切り(':')以降を捨て、"PRP_" 接頭辞を外す([26] §5.5)。
        public static string StripPropPrefix(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName))
            {
                return nodeName;
            }

            var colon = nodeName.IndexOf(':', StringComparison.Ordinal);
            var name = colon >= 0 ? nodeName.Substring(0, colon) : nodeName;

            const string prefix = "PRP_";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name.Substring(prefix.Length);
            }

            return name;
        }
    }
}
