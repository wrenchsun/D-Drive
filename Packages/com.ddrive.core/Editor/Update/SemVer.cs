using System;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.1/§6 P-8(2026-09-20) — MAJOR.MINOR.PATCH の比較(pre-release サフィックスは
    // 無視)。CHANGELOG の見出し・パッケージ版・`DDriveProjectSettings.LastAppliedVersion` の比較で
    // 共通して使う純関数。Unity API に依存しないので EditMode テストから直接検証できる。
    public static class SemVer
    {
        // "1.0.0" / "1.0.0-dev" のどちらも受け付ける(pre-release サフィックスは "-" 以降を切り捨てる)。
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var core = text.Split('-')[0].Trim();
            return Version.TryParse(core, out version);
        }

        // a と b のどちらかがパースできないときは null(比較不能)。それ以外は a.CompareTo(b) と同じ符号。
        public static int? Compare(string a, string b)
        {
            if (!TryParse(a, out var va) || !TryParse(b, out var vb))
            {
                return null;
            }

            return va.CompareTo(vb);
        }

        // a が b より古い(小さい)ときだけ true。パース不能なときは false(「古いとは判定できない」)。
        public static bool IsOlderThan(string a, string b)
        {
            var cmp = Compare(a, b);
            return cmp.HasValue && cmp.Value < 0;
        }
    }
}
