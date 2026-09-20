using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using UnityEngine;

namespace DDrive.Foundation.Data
{
    // 全種別 Data の共通基底。ロード後は読み取り専用として全 Instance から共有される。
    public abstract class AssetDataBase : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("安定ID(内部用)。初回生成後は名前を変えても変化しない。手編集しないこと。")]
        [InspectorReadOnly]
        public ulong Id;

        [Tooltip("AssetBrowser 等での表示名・ID定数生成時の名前解決に使う(未設定ならファイル名を使う)。")]
        public string DisplayName;

        [TextArea]
        [Tooltip("このアセットの説明(任意)。")]
        public string Description;

        [Tooltip("AssetBrowser でのフォルダ分け用カテゴリ(任意)。")]
        public string Category;

        [Tooltip("検索用タグ。TagCatalog から選択する運用(自由入力は非推奨)。仕様書同期(5-13)は状態を \"State/仮\" 等のタグで表す。")]
        public string[] Tags;

        [Tooltip("AssetBrowser の一覧に出すアイコン(任意)。")]
        public Texture2D Icon;

        [Tooltip("担当者(自由入力)。仕様書の「担当」列から同期時に設定される([27_spec_sheet.md] §3.1、5-13)。")]
        public string Assignee;

        [Tooltip("この仕様の参照先URL(仕様書の該当セル等)。Inspector の「仕様書を開く」ボタンで使う([27_spec_sheet.md] §5、5-14)。")]
        public string SpecUrl;

        [Header("Meta")]
        [Tooltip("保存フックで自動的に+1される。手編集しないこと。")]
        [InspectorReadOnly]
        public int Version;

        [Tooltip("最終更新者(保存フックで自動記録)。")]
        [InspectorReadOnly]
        public string Author;

        [Tooltip("最終更新日時(保存フックで自動記録)。")]
        [InspectorReadOnly]
        public string UpdatedAt;

        [TextArea]
        [Tooltip("変更内容のメモ(任意)。")]
        public string ChangeNote;

        [Header("Common")]
        [Tooltip("ポーズ挙動・ロードタイミング・プール設定・優先度・永続化・ドメイン・ネット配送区分をまとめた共通フラグ。")]
        public AssetFlags Flags;

        [Tooltip("生成/常時/消滅などのライフサイクル節目で発火するイベント一覧(他アセットの再生等に使う)。")]
        public AssetEvent[] Events;

        [Header("Import")]
        [Tooltip("インポート検知(ImportRule、5-11)がこの Data を自動生成した元ファイルの GUID(内部用)。" +
                 "再インポート時の二重生成防止に使う。手動作成や Maya 経由(MaterialData.SourceMaterial 等、種別独自のキーを使う)の Data は空。手編集しないこと。")]
        [HideInInspector]
        public string ImportSourceGuid;

        [Header("Schema")]
        [Tooltip("データのスキーマ版(保存フックで DDriveSchema.Current が自動的に書き込まれる)。" +
                 "Version(保存回数)とは別物。既存 .asset は 0 = 「1.0.0 以前の形式」。手編集しないこと。" +
                 "[42_distribution.md] §4.3。")]
        [HideInInspector]
        public int SchemaVersion;

        public virtual IAssetBehaviour CreateBehaviour() => null;
    }
}
