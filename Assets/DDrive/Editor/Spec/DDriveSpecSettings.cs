using DDrive.Editor.AssetBrowser;
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
    // CLAUDE.md §0-9「シリアライズ形式の変更は着手前に確認」への保守的な既定として、
    // 旧フィールド(SpreadsheetUrl/AssetSheetName/TuningSheetName)は削除せずそのまま残し、
    // 新フィールド(WebAppUrl/HumanAppUrl)を追加するだけにした(docs/32_spec_web.md §9 の
    // 要判断に追記。ユーザー確認が取れたら旧フィールドの削除を検討する)。
    // 旧フィールドは SpecAutoSync/SpecSyncWindow からは呼ばれなくなったが、既存 .asset の
    // 値は失われない(そのまま読める)。
    public sealed class DDriveSpecSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";
        public const string DefaultAssetSheetName = "アセット";
        public const string DefaultTuningSheetName = "調整値";
        public const string DefaultTuningTablePath = "Assets/GameData/Settings/DDriveTuningTable.asset";

        // ── Web アプリ(GAS)接続設定(W-9 で新設) ──

        [Tooltip("D-Drive → Web API 用の URL(デプロイ②、?api=1 エンドポイント。[32_spec_web.md] §2.3)。空なら同期・自動取得は何もしない。")]
        public string WebAppUrl;

        [Tooltip("人向け SPA の URL(デプロイ①)。AssetDataBase.SpecUrl を組み立てる元になる([32] §6: 5-14 の SpecUrl の意味変更)。空なら SpecUrl は同期時に更新されない。")]
        public string HumanAppUrl;

        // ── 旧: Google スプレッドシート(CSV)接続設定。[27_spec_sheet.md] 廃止予定、削除はしない(上記コメント参照) ──

        [Tooltip("[旧方式・未使用] Google スプレッドシートの共有URL(またはID)。Web アプリ方式に移行したため SpecAutoSync/SpecSyncWindow からは参照されない。")]
        public string SpreadsheetUrl;

        [Tooltip("[旧方式・未使用] ツール向け「アセット」タブのタブ名。")]
        public string AssetSheetName = DefaultAssetSheetName;

        [Tooltip("[旧方式・未使用] ツール向け「調整値」タブのタブ名。")]
        public string TuningSheetName = DefaultTuningSheetName;

        [Tooltip("Unity 起動時・ドメインリロード後に取得と差分検出だけ行うか(既定 ON)。適用はしない([27] §4.2)。")]
        public bool AutoFetchOnStartup = true;

        [Tooltip("差分の「新規(未着手)」だけ、検出時に自動で Placeholder 作成まで行うか(既定 OFF)。")]
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

            AssetCreationService.EnsureFolder("Assets/GameData/Settings");
            var asset = CreateInstance<DDriveSpecSettings>();
            AssetDatabase.CreateAsset(asset, DefaultPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // TuningTable が未設定なら既定パスに作る/読み込む(SpecSyncService・TuningCodegen が呼ぶ)。
        public TuningTable GetOrCreateTuningTable()
        {
            if (TuningTable != null)
            {
                return TuningTable;
            }

            var existing = AssetDatabase.LoadAssetAtPath<TuningTable>(DefaultTuningTablePath);
            if (existing == null)
            {
                AssetCreationService.EnsureFolder("Assets/GameData/Settings");
                existing = ScriptableObject.CreateInstance<TuningTable>();
                AssetDatabase.CreateAsset(existing, DefaultTuningTablePath);
            }

            TuningTable = existing;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
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
