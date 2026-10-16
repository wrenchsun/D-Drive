using System;
using DDrive.Foundation.Data;
using DDrive.Foundation.Extension;
using UnityEngine;

namespace SampleExtension
{
    // 種別の定義(docs/1014 §2.1)。D-Drive の AssetBrowser / 新規作成 / 命名規則 / カタログ / ID 生成がここから種別を知る。
    // 発見は全アセンブリの走査(public・非 abstract・引数なしコンストラクタ)。KindKey は他の種別と重ならない文字列にする。
    public sealed class SampleKind : IAssetKindProvider
    {
        public const string Key = "Sample";

        public string KindKey => Key;
        public Type DataType => typeof(SampleData);
        public string DisplayName => "Sample";

        // ファイル名の接頭辞(SMP_Xxx.asset)と、ID 定数のクラス名の元(SMPID)。
        public string Prefix => "SMP";

        // GameData ルートからの相対フォルダ(Assets/GameData/Sample)。
        public string Folder => "Sample";

        // カタログの名前(Assets/GameData/Catalogs/SampleCatalog.asset)。
        public string CatalogName => "SampleCatalog";

        // Manager が同期 API(ResolveOrPlaceholder)だけで解決する種別は Preload にする(docs/02 §4)。
        public LoadMode DefaultLoad => LoadMode.Preload;

        public Texture2D Icon => null;

        // AssetBrowser の種別フィルタ・「＋ 新規作成」の並び(小さい順)。
        public int Order => 1000;
    }
}
