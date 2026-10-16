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

    // ── X-R-05 / X-R-03 用の provider ──
    // 既定では無害な名前を返し(他のテストに影響しない)、ExternalBadKinds.Enabled = true のときだけ組み込みの種別名・既存の DisplayName と衝突する。

    public static class ExternalBadKinds
    {
        public static bool Enabled;
        // ExternalStagePrefixKind の Prefix を "STAGE" にする(X-R-02)。
        public static bool StagePrefix;
    }

    // KindKey が組み込みの種別名(大小無視)と衝突する。
    public sealed class ExternalBadKeyKind : IAssetKindProvider
    {
        public string KindKey => ExternalBadKinds.Enabled ? "model" : "ZzBadKey";
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => "Zz Bad Key";
        public string Prefix => "ZZA";
        public string Folder => "ZzBadKey";
        public string CatalogName => "ZzBadKeyCatalog";
        public LoadMode DefaultLoad => LoadMode.LazyLoad;
        public Texture2D Icon => null;
        public int Order => 5001;
    }

    // DisplayName が組み込みの種別名と衝突する。
    public sealed class ExternalBadDisplayKind : IAssetKindProvider
    {
        public string KindKey => "ZzBadDisplay";
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => ExternalBadKinds.Enabled ? "Material" : "Zz Bad Display";
        public string Prefix => "ZZB";
        public string Folder => "ZzBadDisplay";
        public string CatalogName => "ZzBadDisplayCatalog";
        public LoadMode DefaultLoad => LoadMode.LazyLoad;
        public Texture2D Icon => null;
        public int Order => 5002;
    }

    // DisplayName が他の provider(Sample)と重なる。型の完全名が ExternalSampleKind より後ろなので Sample が先勝ちになる。
    public sealed class ZzExternalDupDisplayKind : IAssetKindProvider
    {
        public string KindKey => "ZzDupDisplay";
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => ExternalBadKinds.Enabled ? "sample" : "Zz Dup Display";
        public string Prefix => "ZZC";
        public string Folder => "ZzDupDisplay";
        public string CatalogName => "ZzDupDisplayCatalog";
        public LoadMode DefaultLoad => LoadMode.LazyLoad;
        public Texture2D Icon => null;
        public int Order => 5003;
    }

    // public でない provider(X-R-03): internal な型、internal な型の中の public 入れ子は、どちらも発見されない。
    internal sealed class ExternalInternalKind : IAssetKindProvider
    {
        public string KindKey => "ZzInternal";
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => "Zz Internal";
        public string Prefix => "ZZD";
        public string Folder => "ZzInternal";
        public string CatalogName => "ZzInternalCatalog";
        public LoadMode DefaultLoad => LoadMode.LazyLoad;
        public Texture2D Icon => null;
        public int Order => 5004;
    }

    internal static class ExternalInternalKindHolder
    {
        public sealed class NestedKind : IAssetKindProvider
        {
            public string KindKey => "ZzNestedInternal";
            public Type DataType => typeof(ExternalSampleData);
            public string DisplayName => "Zz Nested Internal";
            public string Prefix => "ZZE";
            public string Folder => "ZzNestedInternal";
            public string CatalogName => "ZzNestedInternalCatalog";
            public LoadMode DefaultLoad => LoadMode.LazyLoad;
            public Texture2D Icon => null;
            public int Order => 5005;
        }
    }

    // X-R-02 用: Prefix が組み込み種別 Model の手付け名ファイル(Stage_Light)の 1 語目と一致する provider。
    // 既定では別の接頭辞(ZZF)を返し、ExternalBadKinds.Enabled のときだけ "STAGE" を名乗る。
    public sealed class ExternalStagePrefixKind : IAssetKindProvider
    {
        public string KindKey => "ZzStage";
        public Type DataType => typeof(ExternalSampleData);
        public string DisplayName => "Zz Stage";
        public string Prefix => ExternalBadKinds.StagePrefix ? "STAGE" : "ZZF";
        public string Folder => "ZzStage";
        public string CatalogName => "ZzStageCatalog";
        public LoadMode DefaultLoad => LoadMode.LazyLoad;
        public Texture2D Icon => null;
        public int Order => 5006;
    }
}
