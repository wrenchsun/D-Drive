using System.Collections.Generic;
using System.Reflection;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace DDrive.Foundation.Validation
{
    public sealed class ValidatorRegistry
    {
        private readonly List<IValidator> _validators = new();

        public void Register(IValidator validator) => _validators.Add(validator);

        public IReadOnlyList<ValidationReport> RunAll(IEnumerable<AssetDataBase> assets)
        {
            var context = new ValidationContext(ToList(assets));
            var reports = new List<ValidationReport>();

            foreach (var asset in context.AllAssets)
            {
                if (asset == null)
                {
                    continue;
                }

                var assetType = ResolveAssetType(asset);

                for (var i = 0; i < _validators.Count; i++)
                {
                    var validator = _validators[i];
                    var applies = validator is IUniversalValidator || validator.Target == assetType;
                    if (!applies)
                    {
                        continue;
                    }

                    foreach (var result in validator.Validate(asset, context))
                    {
                        reports.Add(new ValidationReport(asset, result));
                    }
                }
            }

            // [47_review_p_tickets_2026-09-20.md] P2-5(2026-09-20 修正) — Data が 1 件も無いプロジェクトでは
            // 上の foreach が 1 回も回らず、IUniversalValidator(空プロジェクトの ProjectSetupValidator 等)も
            // 一度も呼ばれなかった。「Run All で Error 0 / Warning 0」が実際には「何も検査していないから」
            // であることに気づけない([42_distribution.md] P-6 の AC を検査が空振りしたまま満たしてしまう)。
            // Data が 0 件のときだけ、IUniversalValidator を asset=null で 1 回ずつ明示的に呼ぶ
            // (Data が 1 件以上あるときは既存どおり各 Data に対して呼ばれるため、二重には呼ばない)。
            if (context.AllAssets.Count == 0)
            {
                for (var i = 0; i < _validators.Count; i++)
                {
                    if (_validators[i] is IUniversalValidator universal)
                    {
                        foreach (var result in universal.Validate(null, context))
                        {
                            reports.Add(new ValidationReport(null, result));
                        }
                    }
                }
            }

            return reports;
        }

        private static List<AssetDataBase> ToList(IEnumerable<AssetDataBase> assets)
        {
            var list = new List<AssetDataBase>();
            foreach (var asset in assets)
            {
                list.Add(asset);
            }

            return list;
        }

        private static AssetType ResolveAssetType(AssetDataBase asset)
        {
            var attr = asset.GetType().GetCustomAttribute<AssetIdDefinitionAttribute>();
            return attr?.Type ?? AssetType.None;
        }
    }
}
