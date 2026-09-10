using System.Text.RegularExpressions;
using DDrive.Foundation.Identity;

namespace DDrive.Editor.AssetBrowser
{
    // [00_requirements.md] FR-1.5/1.6 / [10_workflow.md] §3 — 命名規則はツールが生成・維持する。
    // 人が入力するのは意味情報(表示名/カテゴリ/識別子)のみで、ファイル名はここが規約に従って組み立てる。
    // 純ロジック(AssetDatabase 非依存)としてテスト可能に保つ。
    public static class AssetNamingService
    {
        private static readonly Regex IdentifierPattern = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

        public static string GetTypePrefix(AssetType type) => type switch
        {
            AssetType.Se => "SE",
            AssetType.Bgm => "BGM",
            AssetType.Vfx => "VFX",
            AssetType.Anim => "ANIM",
            AssetType.Anim2D => "ANIM2D",
            AssetType.Material => "MAT",
            AssetType.Texture => "TEX",
            AssetType.Canvas => "CANVAS",
            AssetType.Prefab => "PREFAB",
            AssetType.Model => "MODEL",
            AssetType.Presentation => "PRES",
            AssetType.Shake => "SHAKE",
            AssetType.Haptics => "HAPTIC",
            AssetType.UiTween => "UITWEEN",
            AssetType.Anchor => "ANC",
            AssetType.AnchorGroup => "ANCG",
            _ => "ASSET",
        };

        // GameData 配下の種別別サブフォルダ([01_architecture.md] §5 の配置図に対応)。
        public static string GetTargetFolder(AssetType type) => type switch
        {
            AssetType.Se => "Audio/SE",
            AssetType.Bgm => "Audio/BGM",
            AssetType.Vfx => "Vfx",
            AssetType.Anim => "Anim",
            AssetType.Anim2D => "Anim2D",
            AssetType.Material => "Material",
            AssetType.Texture => "Texture",
            AssetType.Canvas => "Canvas",
            AssetType.Prefab => "Prefab",
            AssetType.Model => "Model",
            AssetType.Presentation => "Presentation",
            AssetType.Shake => "Camera",
            AssetType.Haptics => "Haptics",
            AssetType.UiTween => "UiTween",
            AssetType.Anchor => "Anchor",
            AssetType.AnchorGroup => "AnchorGroup",
            _ => "Misc",
        };

        // 種別フォルダ + カテゴリ階層。"Player/Attack" → "Audio/SE/Player/Attack"。
        // フォルダはあくまで人間用のビューであり、参照の真実は ID/Address(コードがパスに依存してはならない)。
        public static string GetTargetFolder(AssetType type, string category)
        {
            var baseFolder = GetTargetFolder(type);
            var sub = CategoryFolderPath(category);
            return string.IsNullOrEmpty(sub) ? baseFolder : $"{baseFolder}/{sub}";
        }

        // カテゴリ("Player/Attack" 等)をフォルダ階層に変換する。各セグメントは英数字のみに正規化し、
        // 正規化後に空になるセグメント(日本語のみ等)は畳む。全滅なら空(種別フォルダ直下)。
        public static string CategoryFolderPath(string category)
        {
            if (string.IsNullOrEmpty(category))
            {
                return string.Empty;
            }

            var segments = category.Split('/');
            var sb = new System.Text.StringBuilder();
            foreach (var segment in segments)
            {
                var clean = Regex.Replace(segment.Trim(), "[^A-Za-z0-9]", string.Empty);
                if (clean.Length == 0)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('/');
                }

                sb.Append(clean);
            }

            return sb.ToString();
        }

        // 識別子は英語 PascalCase(先頭大文字、英数字のみ)。ID 定数名としてそのまま使われる。
        // 表示名 → PascalCase の識別子(ASCII 英数字のみ。区切りの次を大文字化)。空か数字始まりなら fallback を前置する。
        // AnimEditor(State 名 → AnimData 識別子)と AnchorAssetFactory(GameObject 名 → AnchorData 識別子)が共用する。
        public static string ToIdentifier(string name, string fallback)
        {
            var sb = new System.Text.StringBuilder();
            var upperNext = true;
            foreach (var c in name ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    upperNext = true;
                }
            }

            var result = sb.ToString();
            if (result.Length == 0 || char.IsDigit(result[0]))
            {
                result = fallback + result;
            }

            return result;
        }

        public static bool IsValidIdentifier(string identifier)
            => !string.IsNullOrEmpty(identifier) && IdentifierPattern.IsMatch(identifier);

        // カテゴリは "Audio/SE/Player" のような階層表記も許容し、ファイル名には最終セグメントのみ使う。
        // 英数字以外は除去する(日本語カテゴリはファイル名に含めず、表示・検索用のフィールド側にのみ残る)。
        public static string CategorySegmentForFileName(string category)
        {
            if (string.IsNullOrEmpty(category))
            {
                return string.Empty;
            }

            var segments = category.Split('/');
            var last = segments[segments.Length - 1].Trim();
            return Regex.Replace(last, "[^A-Za-z0-9]", string.Empty);
        }

        public static string BuildFileName(AssetType type, string category, string identifier)
        {
            var prefix = GetTypePrefix(type);
            var categorySegment = CategorySegmentForFileName(category);

            return string.IsNullOrEmpty(categorySegment)
                ? $"{prefix}_{identifier}"
                : $"{prefix}_{categorySegment}_{identifier}";
        }
    }
}
