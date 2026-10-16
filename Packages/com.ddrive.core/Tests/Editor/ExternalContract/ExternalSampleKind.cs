using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;

// 名前空間に "Tests" を含める: AssetBrowser / 新規作成ダイアログは Namespace に "Tests" を含む Data 型を一覧から除くため、
// 実プロジェクトでこのテスト用の種別(Sample)が並ばない。
namespace ExternalContract.Tests
{
    // [1014_extension_platform.md] §2.1 / [42_distribution.md] §5.14 E-26 — 持ち込み先が定義する種別 "Sample" を模したテスト用の外部定義。
    public struct ExternalSampleMarker
    {
    }

    public sealed class ExternalSampleKind : IAssetKindProvider
    {
        public const string Key = "Sample";
        public const string CatalogNameValue = "SampleCatalog";

        public string KindKey => Key;
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => "Sample";
        public string Prefix => "SMP";
        public string Folder => "Sample";
        public string CatalogName => CatalogNameValue;
        public LoadMode DefaultLoad => LoadMode.Preload;
        public Texture2D Icon => null;
        public int Order => 1000;
    }

    // 同じ KindKey を名乗る 2 つ目の provider(重複の警告 + 先勝ち)。型の完全名が "ExternalSampleKind" より後ろになるようにしてある。
    public sealed class ExternalSampleKindDuplicate : IAssetKindProvider
    {
        public string KindKey => ExternalSampleKind.Key;
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => "Sample (dup)";
        public string Prefix => "DUP";
        public string Folder => "Dup";
        public string CatalogName => "DupCatalog";
        public LoadMode DefaultLoad => LoadMode.LazyLoad;
        public Texture2D Icon => null;
        public int Order => 1001;
    }

    // Target=Extension + TargetKindKey で Sample の Data にだけ適用される Validator。
    public sealed class ExternalSampleValidator : IKindValidator
    {
        public const string Message = "ExternalContract: Sample 種別の IKindValidator が実行された(契約テスト用ダミー)";

        public AssetType Target => AssetType.Extension;
        public string TargetKindKey => ExternalSampleKind.Key;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not ExternalSampleData)
            {
                yield break;
            }

            yield return ValidationResult.Info(Message);
        }
    }
}
