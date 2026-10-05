using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — 導入済みパッケージの一覧(版 + package.json の ddriveUpdate 宣言)を
    // Unity の PackageManager から読む実配線。純関数側(`PackageDependencyChecker` 等)は `PackageState` の一覧だけを
    // 受け取るので、ここは EditMode テストの対象外(実 PackageManager に触れる)。
    public static class InstalledPackages
    {
        public static List<PackageState> Load()
        {
            var result = new List<PackageState>();
            try
            {
                foreach (var info in PackageInfo.GetAllRegisteredPackages())
                {
                    if (info == null || string.IsNullOrEmpty(info.name))
                    {
                        continue;
                    }

                    // 宣言を持ち得るのは git / 埋め込み / ローカルのパッケージだけ(レジストリ・組み込みは読まない)。
                    var declaration = DdriveUpdateDeclaration.Empty;
                    if (info.source == PackageSource.Git
                        || info.source == PackageSource.Embedded
                        || info.source == PackageSource.Local)
                    {
                        declaration = ReadDeclaration(info.resolvedPath);
                    }

                    result.Add(new PackageState(info.name, info.displayName, info.version, info.resolvedPath, declaration));
                }
            }
            catch (Exception)
            {
                // 例外で止めない(CLAUDE.md §0-4)。取得できた分だけ返す。
            }

            return result;
        }

        private static DdriveUpdateDeclaration ReadDeclaration(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return DdriveUpdateDeclaration.Empty;
            }

            try
            {
                var path = Path.Combine(resolvedPath, "package.json");
                if (!File.Exists(path))
                {
                    return DdriveUpdateDeclaration.Empty;
                }

                // 読めなかった(壊れた JSON 等)ときは「宣言なし」にせず Unreadable にする(更新後の検査が Warning で知らせる。2026-10-06 BUG-1)。
                return DdriveUpdateDeclaration.TryParse(File.ReadAllText(path), out var declaration) ? declaration : DdriveUpdateDeclaration.Unreadable;
            }
            catch (Exception)
            {
                return DdriveUpdateDeclaration.Empty; // ファイルを読めない(権限等)ときは従来どおり宣言なし
            }
        }
    }
}
