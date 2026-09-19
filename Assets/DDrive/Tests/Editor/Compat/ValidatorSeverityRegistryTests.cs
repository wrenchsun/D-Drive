using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DDrive.Editor.Compat;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.8 / §5.11-8(P-3、2026-09-20) — Validator の「どの検査(Code) がどの重さ
    // (Severity)か」を固定する。新しい Error の追加は 2 段階(まず Warning、次の MINOR で昇格。
    // 昇格は CHANGELOG 必須)が原則([12_review.md] §3「互換性」節)。
    //
    // 現状の制約(2026-09-20 時点): `ValidationResult.Code` は本チケットで新設したばかりで、既存の
    // Validator の呼び出しはほとんど Code 未設定のまま(既存呼び出しを変えない方針、[42] §5.11-8
    // 「Code 未設定の結果はゴールデン対象外にしてよい」)。代表として `AddressablesRegistrationValidator`
    // (共通検査、[02] §11)にだけ Code を付与し、この仕組み自体が機能することを実証した。
    // 他の Validator に Code を広げるのは本チケットのスコープ外(新規に書く Validator から Code 必須、
    // という方針を [12_review.md] に明記した)。
    public class ValidatorSeverityRegistryTests
    {
        private const string Hint = "Validator の Code の削除・Severity 変更は互換性ポリシー対象([42] §5.8)。新しい Error への昇格は CHANGELOG 必須。";

        [Test]
        public void KnownValidatorCodes_MatchGolden()
        {
            var pairs = new List<string>();

            CollectAddressablesRegistrationValidatorCodes(pairs);
            CollectGenericValidatorCodes(pairs);

            var distinct = pairs.Distinct().ToList();
            distinct.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var p in distinct)
            {
                sb.Append(p).Append('\n');
            }

            CompatGoldenAssert.AssertMatches(CompatSnapshotPaths.ValidatorSeverity, sb.ToString(), Hint);
        }

        [Test]
        public void AddressablesRegistrationValidator_CatalogMissing_IsError()
        {
            var pairs = new List<string>();
            CollectAddressablesRegistrationValidatorCodes(pairs);
            CollectionAssert.Contains(pairs, "DD-ADDR-CATALOG-MISSING=Error");
        }

        private static void CollectAddressablesRegistrationValidatorCodes(List<string> pairs)
        {
            // AddressablesRegistrationValidator は `AssetDatabase.GetAssetPath(data)` が空(= まだ
            // ディスクに保存されていない)だと即座に抜ける(実行時ロード対象外の判定)。メモリ上の
            // ScriptableObject.CreateInstance のままでは経路を再現できないため、一時アセットとして
            // 保存してから検証する([09_editor_tools.md] の一時アセットの流儀。Tests/Editor/Temp を使う)。
            const string tempDir = "Assets/DDrive/Tests/Editor/Temp";
            if (!UnityEditor.AssetDatabase.IsValidFolder(tempDir))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets/DDrive/Tests/Editor", "Temp");
            }

            var path = $"{tempDir}/ValidatorSeverityRegistryProbe.asset";
            var data = ScriptableObject.CreateInstance<TestAssetData>();
            data.Id = 0x7FFFFFFFFFFFFFFEUL; // 実カタログ・Addressables のどちらにも存在しない前提の値
            UnityEditor.AssetDatabase.CreateAsset(data, path);

            try
            {
                var validator = new DDrive.Editor.Validation.AddressablesRegistrationValidator { IncludeTestFolders = true };
                var ctx = new ValidationContext(new List<AssetDataBase> { data });
                foreach (var result in validator.Validate(data, ctx))
                {
                    if (!string.IsNullOrEmpty(result.Code))
                    {
                        pairs.Add($"{result.Code}={result.Severity}");
                    }
                }
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(path);
            }
        }

        // 汎用スイープ: DiscoverValidators() で見つかる全 IValidator を、対応する具象 Data 型の既定
        // インスタンスに対して実行し、Code が設定されている結果だけを拾う。新しい Validator が Code を
        // 付けて追加されたときに、このテストを直さなくても自動でゴールデン対象へ入るようにするための保険。
        private static void CollectGenericValidatorCodes(List<string> pairs)
        {
            foreach (var validator in DDrive.Editor.CI.DiscoverValidators())
            {
                if (validator is DDrive.Editor.Validation.AddressablesRegistrationValidator)
                {
                    continue; // 上で個別に踏んだ(Target=None の共通検査は具象型だけでは経路を再現できない)
                }

                var dataType = ResolveDataType(validator.Target);
                if (dataType == null)
                {
                    continue;
                }

                AssetDataBase instance;
                try
                {
                    instance = (AssetDataBase)ScriptableObject.CreateInstance(dataType);
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    List<ValidationResult> results;
                    try
                    {
                        var ctx = new ValidationContext(new List<AssetDataBase> { instance });
                        results = validator.Validate(instance, ctx).ToList();
                    }
                    catch (Exception)
                    {
                        // 未設定フィールド(null 参照等)で例外を投げる Validator は対象外。
                        // 本テストの目的は「Code が付いている結果を漏らさず拾う」ことであり、
                        // Validator 自体の堅牢性はここでは検証しない。
                        continue;
                    }

                    foreach (var result in results)
                    {
                        if (!string.IsNullOrEmpty(result.Code))
                        {
                            pairs.Add($"{result.Code}={result.Severity}");
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        private static Type ResolveDataType(AssetType target)
        {
            foreach (var type in SerializedLayoutSnapshotBuilder.ConcreteDataTypes())
            {
                var attr = type.GetCustomAttribute<AssetIdDefinitionAttribute>();
                if (attr != null && attr.Type == target)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
