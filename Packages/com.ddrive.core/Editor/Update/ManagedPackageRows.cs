using System;
using System.Collections.Generic;
using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Update
{
    // 更新ウィンドウの「パッケージ」一覧の 1 行。
    public sealed class PackageRow
    {
        public string Id;
        public string DisplayName;
        public bool IsDDrive;

        // manifest.dependencies の値(無ければ null)。
        public string ManifestValue;
        public GitPackageUrl Url;

        // 導入済みの状態(manifest にあるが未解決・未導入のときは null)。
        public PackageState Installed;

        public bool InManifest => ManifestValue != null;
        public bool IsGit => Url.IsGitUrl;
        public string CurrentVersion => Installed?.Version ?? string.Empty;
    }

    // manifest にある git URL 依存のうち、まだ管理対象でないもの(1 クリックで登録できる候補)。
    public sealed class PackageCandidate
    {
        public string Id;
        public string ManifestValue;

        // false: `#vX.Y.Z` 形式の ref ではない(UniTask / R3 のようにタグ運用が違う)ため、最新版の判定が当てにならない。
        public bool HasVersionTagRef;
    }

    // [42_distribution.md] §4.2 P-15(2026-10-03) — 一覧の行・候補の構築(純関数。manifest は JObject、導入済みの情報は
    // `PackageState` の一覧で受け取るので、実 manifest・実 PackageManager に触れずにテストできる)。
    public static class ManagedPackageRows
    {
        public const string DDrivePackageId = ProjectSetupActions.DDriveCorePackageId;

        // 1 行目は常に D-Drive。続けて `managedIds` の順(重複・D-Drive 自身は無視)。
        public static List<PackageRow> Build(JObject manifest, IReadOnlyList<string> managedIds, IReadOnlyList<PackageState> installed)
        {
            var rows = new List<PackageRow> { BuildRow(DDrivePackageId, true, manifest, installed) };
            if (managedIds == null)
            {
                return rows;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { DDrivePackageId };
            for (var i = 0; i < managedIds.Count; i++)
            {
                var id = managedIds[i];
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                {
                    continue;
                }

                rows.Add(BuildRow(id, false, manifest, installed));
            }

            return rows;
        }

        public static List<PackageCandidate> FindCandidates(JObject manifest, IReadOnlyList<string> managedIds)
        {
            var managed = new HashSet<string>(StringComparer.Ordinal) { DDrivePackageId };
            if (managedIds != null)
            {
                for (var i = 0; i < managedIds.Count; i++)
                {
                    managed.Add(managedIds[i]);
                }
            }

            var result = new List<PackageCandidate>();
            foreach (var dep in PackageManifestOps.ListGitDependencies(manifest))
            {
                if (managed.Contains(dep.Id))
                {
                    continue;
                }

                result.Add(new PackageCandidate { Id = dep.Id, ManifestValue = dep.Value, HasVersionTagRef = dep.HasVersionTagRef });
            }

            return result;
        }

        public static PackageState FindState(IReadOnlyList<PackageState> installed, string id)
        {
            if (installed == null)
            {
                return null;
            }

            for (var i = 0; i < installed.Count; i++)
            {
                if (installed[i] != null && string.Equals(installed[i].Id, id, StringComparison.Ordinal))
                {
                    return installed[i];
                }
            }

            return null;
        }

        private static PackageRow BuildRow(string id, bool isDDrive, JObject manifest, IReadOnlyList<PackageState> installed)
        {
            var value = ManifestJson.GetDependencyValue(manifest, id);
            var state = FindState(installed, id);
            return new PackageRow
            {
                Id = id,
                DisplayName = isDDrive ? "D-Drive" : (state != null ? state.DisplayName : id),
                IsDDrive = isDDrive,
                ManifestValue = value,
                Url = GitPackageUrl.Parse(value),
                Installed = state,
            };
        }
    }
}
