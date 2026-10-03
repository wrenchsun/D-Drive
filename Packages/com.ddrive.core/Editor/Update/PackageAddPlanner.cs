using DDrive.Editor.Setup;
using Newtonsoft.Json.Linq;

namespace DDrive.Editor.Update
{
    public enum PackageAddOutcome
    {
        // 入力を解釈できない / git URL でない(何も変えない)。
        Invalid,

        // manifest に既にある git URL 依存。管理対象に登録するだけ(`PackageId`)。
        RegisterExisting,

        // manifest に無い。`ManifestValue`(#ref 付き)で `PackageManager.Client.Add` する。
        AddNew,

        // タグが 1 つも無い(`vX.Y.Z` 形式)。何も変えない。
        NoTags,

        // `git ls-remote` が失敗した。何も変えない。
        TagListFailed,
    }

    public readonly struct PackageAddPlan
    {
        public readonly PackageAddOutcome Outcome;
        public readonly string PackageId;
        public readonly string ManifestValue;
        public readonly string Message;

        public PackageAddPlan(PackageAddOutcome outcome, string packageId, string manifestValue, string message)
        {
            Outcome = outcome;
            PackageId = packageId;
            ManifestValue = manifestValue;
            Message = message;
        }
    }

    // [42_distribution.md] §4.2 P-15(2026-10-03) — 「URL を入力して追加」の入力を解釈して、何をするかを決める
    // 純粋に近いロジック(manifest は JObject で受け取り、`git ls-remote` は `IGitTagLister` 越し。
    // 実 `PackageManager.Client.Add` はここでは呼ばない = ウィンドウ側の責務)。例外で止めない。
    public sealed class PackageAddPlanner
    {
        private readonly IGitTagLister _tagLister;

        public PackageAddPlanner(IGitTagLister tagLister)
        {
            _tagLister = tagLister;
        }

        public PackageAddPlan Plan(string input, JObject manifest)
        {
            var text = input?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return Invalid("URL かパッケージ ID を入力してください。");
            }

            // 1. パッケージ ID(manifest のキー)として入力された。
            if (ManifestJson.HasDependency(manifest, text))
            {
                var existing = GitPackageUrl.Parse(ManifestJson.GetDependencyValue(manifest, text));
                return existing.IsGitUrl
                    ? new PackageAddPlan(PackageAddOutcome.RegisterExisting, text, null, $"{text} を管理対象に登録します。")
                    : Invalid($"{text} は git URL で導入されたパッケージではないため、更新ウィンドウでは扱えません。");
            }

            // 2. git URL として入力された。
            var parsed = GitPackageUrl.Parse(text);
            if (!parsed.IsGitUrl)
            {
                return Invalid("git URL として解釈できませんでした(例: https://github.com/<owner>/<repo>.git?path=<dir> / git+https://… / ssh://…。manifest にあるパッケージ ID も使えます)。");
            }

            var sameId = PackageManifestOps.FindDependencyIdByUrl(manifest, parsed);
            if (sameId != null)
            {
                return new PackageAddPlan(PackageAddOutcome.RegisterExisting, sameId, null, $"manifest に同じ URL の {sameId} があります。管理対象に登録します。");
            }

            // 3. manifest に無い → 導入する。#ref 指定があればそれ、無ければ最新の vX.Y.Z。
            if (!string.IsNullOrEmpty(parsed.Ref))
            {
                return new PackageAddPlan(PackageAddOutcome.AddNew, null, text, $"{parsed.Ref} で導入します。");
            }

            if (_tagLister == null)
            {
                return new PackageAddPlan(PackageAddOutcome.TagListFailed, null, null, "タグを取得できませんでした。");
            }

            var stdout = _tagLister.ListTags(parsed.CloneUrl, out var warning);
            if (warning != null || stdout == null)
            {
                return new PackageAddPlan(PackageAddOutcome.TagListFailed, null, null, "タグを取得できませんでした: " + (warning ?? "不明なエラー"));
            }

            // 元のタグ名をそのまま #ref に使う(`v1.5.0-rc.1` を `v1.5.0` に丸めない)。既定では正式版だけを「最新」にする。
            var tags = GitTagListParser.ParseTags(stdout, true);
            if (tags.Count == 0)
            {
                return new PackageAddPlan(
                    PackageAddOutcome.NoTags, null, null,
                    "vX.Y.Z 形式のタグが 1 つもありません。#vX.Y.Z またはブランチ・コミットを URL に付けて入力してください。");
            }

            string latest = null;
            string newestPre = null;
            for (var i = 0; i < tags.Count; i++)
            {
                if (!tags[i].IsPrerelease)
                {
                    latest = tags[i].Name;
                    break;
                }

                newestPre ??= tags[i].Name;
            }

            if (latest == null)
            {
                return new PackageAddPlan(
                    PackageAddOutcome.NoTags, null, null,
                    $"正式版(vX.Y.Z)のタグがありません(プレリリースのみ: {newestPre} など)。プレリリースを導入するには #{newestPre} のように URL にタグ名を付けて入力してください。");
            }

            return new PackageAddPlan(PackageAddOutcome.AddNew, null, parsed.WithRef(latest), $"最新の {latest} で導入します。");
        }

        private static PackageAddPlan Invalid(string message) => new(PackageAddOutcome.Invalid, null, null, message);
    }
}
