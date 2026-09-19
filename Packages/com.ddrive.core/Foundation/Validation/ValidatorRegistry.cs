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
