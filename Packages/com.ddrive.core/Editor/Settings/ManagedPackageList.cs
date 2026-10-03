using System;
using System.Collections.Generic;

namespace DDrive.Editor.Settings
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — `DDriveProjectSettings.ManagedPackages` の追加・解除・検索を
    // 「ただの List」に対する純関数として切り出したもの(ScriptableSingleton の保存を伴わないので
    // EditMode テストが実設定ファイルに触れずに検証できる)。
    public static class ManagedPackageList
    {
        public static ManagedPackageEntry Find(IReadOnlyList<ManagedPackageEntry> list, string packageId)
        {
            if (list == null || string.IsNullOrEmpty(packageId))
            {
                return null;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e != null && string.Equals(e.PackageId, packageId, StringComparison.Ordinal))
                {
                    return e;
                }
            }

            return null;
        }

        // 冪等。新しく追加したときだけ true。
        public static bool Register(List<ManagedPackageEntry> list, string packageId)
        {
            if (list == null || string.IsNullOrWhiteSpace(packageId) || Find(list, packageId) != null)
            {
                return false;
            }

            list.Add(new ManagedPackageEntry { PackageId = packageId.Trim() });
            return true;
        }

        public static bool Unregister(List<ManagedPackageEntry> list, string packageId)
        {
            var entry = Find(list, packageId);
            return entry != null && list.Remove(entry);
        }
    }
}
