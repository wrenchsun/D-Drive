using System;
using DDrive.Foundation.Data;
using UnityEngine;

namespace DDrive.Foundation.Extension
{
    // [1014_extension_platform.md] §2.1(X-1、2026-10-16) — 持ち込み先(外部アセンブリ)が新しい種別を定義する拡張点。
    // public・非 abstract・引数なしコンストラクタの実装は AssetKindRegistry が全アセンブリから発見する
    // ([42_distribution.md] §5.14 の発見規則と同じ)。種別は AssetType.Extension + KindKey で区別する。
    public interface IAssetKindProvider
    {
        // 種別キー(例 "LightCue")。AssetIdDefinitionAttribute.KindKey / CatalogEntry.KindKey と同じ文字列。
        string KindKey { get; }

        // この種別の Data 型(AssetDataBase 派生。[AssetIdDefinition(AssetType.Extension, …, KindKey = …)] を付ける)。
        Type DataType { get; }

        // AssetBrowser・新規作成に出す名前。
        string DisplayName { get; }

        // ファイル名の接頭辞(例 "CUE")。ID 定数名から接頭辞を除くためにも使われる。
        string Prefix { get; }

        // GameData ルートからの相対フォルダ(例 "LightCue")。
        string Folder { get; }

        // 登録先のカタログ名(例 "LightCueCatalog")。
        string CatalogName { get; }

        // 新規作成時の既定 Load。同期 API(ResolveOrPlaceholder)でしか引かない種別は Preload にする。
        LoadMode DefaultLoad { get; }

        // 任意(null 可)。
        Texture2D Icon { get; }

        // AssetBrowser・新規作成の並び順(小さいほど先)。
        int Order { get; }
    }
}
