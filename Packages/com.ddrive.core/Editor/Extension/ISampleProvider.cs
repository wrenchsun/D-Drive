using DDrive.Foundation.Data;

namespace DDrive.Editor.Extension
{
    // [1014_extension_platform.md] §2.4(X-4、2026-10-16) — 持ち込み先が定義した種別(AssetType.Extension + KindKey)の
    // 空状態「サンプルを作る」を出す拡張点。UxEditorWindow<TData> の空状態が、TData の [AssetIdDefinition].KindKey と
    // 同じ KindKey の provider を探して呼ぶ(provider が無ければボタンを出さない)。組み込み種別の経路(HasSample / CreateSample)は変わらない。
    // 発見は ExtensionProviders(TypeCache。public・非 abstract・引数なしコンストラクタ、テスト用アセンブリは既定で除外、
    // KindKey の重複は警告 1 回 + 先勝ち)。
    public interface ISampleProvider
    {
        // 対象の種別キー(IAssetKindProvider.KindKey / AssetIdDefinition.KindKey と同じ)。
        string KindKey { get; }

        // ボタンの文言に使う表示名(例 "Sample")。
        string DisplayName { get; }

        // サンプルの Data を作って返す。folder = D-Drive が用意したプロジェクト内のフォルダ(<GameData>/<種別のフォルダ>/Sample)。
        // 音・画像・Prefab などの素材を作るならここへ置く(Data そのものを置く場所ではない)。
        //  - Data を保存していない ScriptableObject(CreateInstance しただけ)で返すと、D-Drive が命名規則どおりに保存し、
        //    カタログと Addressables へ登録する(AssetCreationService.Create。カテゴリ "Sample"、既定 Load は種別の既定)。
        //  - 既にアセットとして保存して Id を振ってあるものを返すと、D-Drive はカタログ / Addressables への登録だけを行う。
        // 同じ DisplayName のサンプルが既にあれば呼ばれず、それが選ばれる。null を返すと何も起きない(例外は握りつぶして警告)。
        AssetDataBase CreateSample(string folder);
    }
}
