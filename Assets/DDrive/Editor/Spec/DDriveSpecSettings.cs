using DDrive.Editor.AssetBrowser;
using DDrive.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Spec
{
    // [27_spec_sheet.md] §4.1/§7.1 — 仕様書スプレッドシートの接続設定。プロジェクトに 1 個だけ想定
    // (UiLayerSettings 等と同じ「プロジェクト単位の設定 SO」)。.asset はテキストで書かず、
    // GetOrCreate() が必要になったとき(同期ウィンドウで初めて URL を設定するとき等)に自動生成する
    // (CLAUDE.md §0-2/§0-9: .asset をテキスト編集しない)。
    public sealed class DDriveSpecSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/GameData/Settings/DDriveSpecSettings.asset";
        public const string DefaultAssetSheetName = "アセット";
        public const string DefaultTuningSheetName = "調整値";
        public const string DefaultTuningTablePath = "Assets/GameData/Settings/DDriveTuningTable.asset";

        [Tooltip("Google スプレッドシートの共有URL(またはID)。「リンクを知っている全員が閲覧可」にしておくこと([27] §4.1)。空なら同期・自動取得は何もしない。")]
        public string SpreadsheetUrl;

        [Tooltip("ツール向け「アセット」タブのタブ名。")]
        public string AssetSheetName = DefaultAssetSheetName;

        [Tooltip("ツール向け「調整値」タブのタブ名。")]
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
    }
}
