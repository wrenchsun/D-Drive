using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Settings;
using DDrive.Editor.Versioning;
using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.1/§7.1 → [32_spec_web.md] §5.1 — 仕様書の接続設定。プロジェクトに 1 個だけ想定
    // (UiLayerSettings 等と同じ「プロジェクト単位の設定 SO」)。.asset はテキストで書かず、
    // GetOrCreate() が必要になったとき(同期ウィンドウで初めて URL を設定するとき等)に自動生成する
    // (CLAUDE.md §0-2/§0-9: .asset をテキスト編集しない)。
    //
    // W-9(2026-09-14): 取得元を Google スプレッドシート(CSV)から Web アプリ(GAS)の API に差し替えた。
    //
    // 2026-09-20(P-4、[42_distribution.md] §5.13): 旧フィールド(SpreadsheetUrl/AssetSheetName/
    // TuningSheetName)を削除した。実データ(Assets/GameData/Settings/DDriveSpecSettings.asset。開発
    // リポジトリに存在する唯一の実 .asset)を確認したところ SpreadsheetUrl は空文字、AssetSheetName/
    // TuningSheetName も既定値のままでユーザー固有の値が入っていなかったため、[32_spec_web.md] §9-12
    // の保留条件(「実データが入っている .asset が存在しない場合は次のチケットで削除してよい」)に該当する。
    // フィールド削除により既存 .asset の当該キーは次回保存時に消える(それまでは YAML に残るが無視される。
    // Unity は未知フィールドを無視するだけで読み込みエラーにはしない)。
    public sealed class DDriveSpecSettings : ScriptableObject
    {
        // 2026-09-20([42_distribution.md] §2.3 #11、P-12 の実移植〔docs/49〕で発見) — 以前はここが
        // `const` のハードコードで、`DDriveProjectSettings.GameDataRoot`(§7 B-6 の置き場所プリセット)を
        // 一切参照していなかった。持ち込み先が「1 つの親フォルダ配下にまとめる」プリセットで GameDataRoot を
        // 変更しても、この 2 ファイルだけが既定の "Assets/GameData/Settings/" に取り残される不具合があった
        // (対照的に SpecSnapshotWriter.cs は元から DDriveProjectSettings.instance.SpecsRoot を正しく
        // 読んでいた)。`ControlSkinPreviewSection.DefaultScrollMaterialPath` を const → プロパティ化した
        // P-4 の対応と同じパターンで static プロパティに変える。
        //
        // 既定値のままなら今までと同じパス("Assets/GameData/Settings/...")になる
        // (DDriveProjectSettings.GameDataRoot の既定値は AssetCreationService.DefaultGameDataRoot と同じ)。
        // 既にこの決め打ちパスにアセットが存在する場合はそれを優先して使う(移動はしない。開発リポジトリの
        // 既存アセット、および GameDataRoot 変更前に作られたアセットが迷子にならないようにするため)。
        private const string LegacyDefaultPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";
        private const string LegacyDefaultTuningTablePath = "Assets/GameData/Settings/DDriveTuningTable.asset";

        public static string DefaultPath => ResolveSettingsPath("DDriveSpecSettings.asset", LegacyDefaultPath);

        public static string DefaultTuningTablePath => ResolveSettingsPath("DDriveTuningTable.asset", LegacyDefaultTuningTablePath);

        private static string ResolveSettingsPath(string fileName, string legacyPath) =>
            ResolveSettingsPathCore(
                fileName,
                legacyPath,
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(legacyPath) != null,
                DDriveProjectSettings.instance.GameDataRoot);

        // 純粋な決定ロジックだけを切り出したもの(AssetDatabase/DDriveProjectSettings への実アクセスを伴わない)。
        // public(InternalsVisibleTo 未設定のため、テスト asmdef から直接検証できるようにする。
        // CodeReferenceScan.cs 等と同じ理由)。
        public static string ResolveSettingsPathCore(string fileName, string legacyPath, bool legacyAssetExists, string gameDataRoot)
            => legacyAssetExists ? legacyPath : $"{gameDataRoot}/Settings/{fileName}";

        private static string FolderOf(string assetPath)
        {
            var slash = assetPath.LastIndexOf('/');
            return slash < 0 ? assetPath : assetPath.Substring(0, slash);
        }

        // ── Web アプリ(GAS)接続設定(W-9 で新設) ──

        [Tooltip("D-Drive → Web API 用の URL(デプロイ②、?api=1 エンドポイント。[32_spec_web.md] §2.3)。空なら同期・自動取得は何もしない。")]
        public string WebAppUrl;

        [Tooltip("人向け SPA の URL(デプロイ①)。AssetDataBase.SpecUrl を組み立てる元になる([32] §6: 5-14 の SpecUrl の意味変更)。空なら SpecUrl は同期時に更新されない。")]
        public string HumanAppUrl;

        [Tooltip("Unity 起動時・ドメインリロード後に取得と差分検出だけ行うか(既定 ON)。適用はしない([27] §4.2)。")]
        public bool AutoFetchOnStartup = true;

        [Tooltip("差分の「新規」だけ、検出時に自動で Placeholder 作成まで行うか(既定 OFF)。")]
        public bool AutoApplyNewPlaceholders;

        [Tooltip("Placeholder 作成先の GameData ルート(通常は変更不要)。")]
        public string GameDataRoot = AssetCreationService.DefaultGameDataRoot;

        [Tooltip("「調整値」タブの取り込み先(5-13)。未設定なら初回同期時に既定パスへ自動生成する。")]
        public TuningTable TuningTable;

        public static DDriveSpecSettings Load() => AssetDatabase.LoadAssetAtPath<DDriveSpecSettings>(DefaultPath);

        public static DDriveSpecSettings GetOrCreate()
        {
            var existing = Load();
            if (existing != null)
            {
                return existing;
            }

            var defaultPath = DefaultPath;
            AssetCreationService.EnsureFolder(FolderOf(defaultPath));
            var asset = CreateInstance<DDriveSpecSettings>();
            // [42_distribution.md] §3.4/§7 B-6(P-5) — 新規作成時だけ DDriveProjectSettings.GameDataRoot を
            // 反映する(フィールド初期化子ではなく GetOrCreate 内で解決することで、任意のデシリアライズ時に
            // ScriptableSingleton へアクセスすることを避ける)。
            asset.GameDataRoot = AssetCreationService.ResolveGameDataRoot(AssetCreationService.DefaultGameDataRoot);
            AssetDatabase.CreateAsset(asset, defaultPath);
            // [44_review_2026-09-19.md] P1-1: 新規作成した asset 1 個だけ保存する。
            DDriveAssetSave.SaveDirty(asset);
            return asset;
        }

        // TuningTable が未設定なら既定パスに作る/読み込む(SpecSyncService・TuningCodegen が呼ぶ)。
        public TuningTable GetOrCreateTuningTable()
        {
            if (TuningTable != null)
            {
                return TuningTable;
            }

            var tuningTablePath = DefaultTuningTablePath;
            var existing = AssetDatabase.LoadAssetAtPath<TuningTable>(tuningTablePath);
            if (existing == null)
            {
                AssetCreationService.EnsureFolder(FolderOf(tuningTablePath));
                existing = ScriptableObject.CreateInstance<TuningTable>();
                AssetDatabase.CreateAsset(existing, tuningTablePath);
            }

            TuningTable = existing;
            EditorUtility.SetDirty(this);
            // [44_review_2026-09-19.md] P1-1: 触った対象(this・existing)だけ保存する。
            DDriveAssetSave.SaveDirty(this);
            DDriveAssetSave.SaveDirty(existing);
            return existing;
        }

        // ── API トークン(W-9・§7) ──
        // .asset(git 管理)には実値を絶対に書かない。EditorPrefs はマシンごとに保存される
        // (docs/32_spec_web.md §5.2/§7「トークンは git に入れない…Unity の EditorPrefs(マシンごと)」)。
        // プロジェクトが複数あっても衝突しないよう、キーにプロジェクトの絶対パスを混ぜる。
        private const string ReadTokenPrefKeyBase = "DDrive.SpecWeb.ReadToken";
        private const string WriteTokenPrefKeyBase = "DDrive.SpecWeb.WriteToken";

        private static string ProjectScopedKey(string baseKey)
            => baseKey + ":" + Application.dataPath;

        public static string ReadToken
        {
            get => EditorPrefs.GetString(ProjectScopedKey(ReadTokenPrefKeyBase), string.Empty);
            set => EditorPrefs.SetString(ProjectScopedKey(ReadTokenPrefKeyBase), value ?? string.Empty);
        }

        public static string WriteToken
        {
            get => EditorPrefs.GetString(ProjectScopedKey(WriteTokenPrefKeyBase), string.Empty);
            set => EditorPrefs.SetString(ProjectScopedKey(WriteTokenPrefKeyBase), value ?? string.Empty);
        }
    }
}
