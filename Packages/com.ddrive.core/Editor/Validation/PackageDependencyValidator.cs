using System.Collections.Generic;
using DDrive.Editor.Update;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;

namespace DDrive.Editor.Validation
{
    // [42_distribution.md] §4.2 P-15(2026-10-03) — 導入済みパッケージの package.json に書かれた `ddriveUpdate`
    // (requires / compatibleWith)が現在の組み合わせで満たされているかを `Validation > Run All` にも載せる
    // (CI で継続検出できるようにする)。更新ウィンドウと同じ `PackageDependencyChecker` を使う。
    //
    // 重さは §5.8「新しい検査は Warning 始まり」に従い、requires の未充足も含めてすべて Warning
    // (更新ウィンドウ上では requires 未充足を Error として赤く表示する)。MAJOR 差は Info。
    // `ProjectSetupValidator` と同じ制約: IUniversalValidator は ValidatorRegistry.RunAll が Data 1 件以上のときに
    // 呼ぶ(0 件のときは asset=null で 1 回だけ呼ばれる)ため、1 回の Run All(= 1 つの ValidationContext)に
    // つき 1 回だけ報告する。
    //
    // 2026-10-06(P-15 確認 Q-3): この検査はプロジェクト全体の指摘で、どの Data にも属さない。`ValidatorRegistry.RunAll` に渡すと
    // 「最初に呼ばれた Data」(例: ANC_Anim_Jump.asset)に紐付いて無関係なアセットのパスが付くため、`CI.RunValidation` は
    // この Validator を RunAll に載せず asset = null で 1 回だけ実行する(コンソール・JUnit は「(project)」と表示)。
    // 1 アセットの個別検証(DataValidationSection)にも出さない。
    public sealed class PackageDependencyValidator : IUniversalValidator
    {
        private static ValidationContext _reportedForCtx;

        // テスト用フック(CameraExecutionOrderValidator.ScriptOrderProvider と同じ流儀): 既定は実際の導入済みパッケージ。
        // 実 PackageManager に触れずに、検査の結果と報告の紐付け(CI.RunValidation)をテストするために差し替える。
        public static System.Func<IReadOnlyList<PackageState>> PackagesProvider = () => InstalledPackages.Load();

        public AssetType Target => AssetType.None;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (_reportedForCtx == ctx)
            {
                yield break;
            }

            _reportedForCtx = ctx;

            foreach (var result in ToResults(PackageDependencyChecker.Check(PackagesProvider())))
            {
                yield return result;
            }
        }

        // 純関数(テスト用に公開): 検査結果 → ValidationResult(Error は Warning に丸める)。Editor 契約外(外部から呼ばない)。
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static IEnumerable<ValidationResult> ToResults(IReadOnlyList<PackageDependencyIssue> issues)
        {
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                var message = issue.Message + " 更新ウィンドウ(Tools > D-Drive > Update > 更新ウィンドウ)で確認できます。";
                yield return issue.Severity == DependencyIssueSeverity.Info
                    ? ValidationResult.Info(message, code: issue.Code)
                    : ValidationResult.Warning(message, code: issue.Code);
            }
        }
    }
}
