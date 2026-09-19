namespace DDrive.Editor.Setup
{
    // [42_distribution.md] §3.5/§3.6/§6 P-6(2026-09-20) — ウィザード(ProjectSetupWizardWindow)・
    // Validator(ProjectSetupValidator)・純粋な検査ロジック(ProjectSetupInspector)が共有するデータ型。
    // EditorWindow に依存しない(ウィンドウ非依存の検査ロジックにするための土台。§6 P-6 の指示)。

    public enum MissingDependencyKind
    {
        GitPackage,      // Client.Add(gitUrl) で追加できる(UniTask/R3)。
        RegistryPackage, // scoped registry 経由の通常パッケージ(org.nuget.r3)。scoped registry が先に要る。
        ScopedRegistry,  // manifest.json への直接追記でしか追加できない(Client.AddScopedRegistry が
                         // public API に無いことを確認済み。2026-09-20)。
    }

    // package.json に書けない(git 配布・scoped registry)依存のうち、manifest に無いもの 1 件。
    public readonly struct MissingDependency
    {
        public readonly string PackageId;
        public readonly string Code;
        public readonly string Message;

        // Kind==GitPackage: Client.Add にそのまま渡す git URL。
        // Kind==RegistryPackage: Client.Add に渡すバージョン文字列(scoped registry 済み前提)。
        // Kind==ScopedRegistry: 追加する scoped registry の URL(表示用。実際の追加は ManifestJson 経由)。
        public readonly string Value;
        public readonly MissingDependencyKind Kind;

        public MissingDependency(string packageId, string code, string message, string value, MissingDependencyKind kind)
        {
            PackageId = packageId;
            Code = code;
            Message = message;
            Value = value;
            Kind = kind;
        }
    }

    // §3.6 の ProjectSettings 検査(URP / Input System / API Compatibility Level)。
    public readonly struct ProjectSettingsStatus
    {
        public readonly bool UrpActive;
        public readonly bool InputSystemActive; // activeInputHandler が 1(Input System) or 2(Both)
        public readonly bool ApiCompatibilityOk; // .NET Standard 2.1 相当(NET_Standard_2_0)以上

        public ProjectSettingsStatus(bool urpActive, bool inputSystemActive, bool apiCompatibilityOk)
        {
            UrpActive = urpActive;
            InputSystemActive = inputSystemActive;
            ApiCompatibilityOk = apiCompatibilityOk;
        }

        public bool AllOk => UrpActive && InputSystemActive && ApiCompatibilityOk;
    }

    // §3.6/§6 P-6 手順 4 の既定フォルダ・設定の検査。
    public readonly struct FolderLayoutStatus
    {
        public readonly bool GameDataRootExists;
        public readonly bool UiLayerSettingsExists;
        public readonly bool SpecSettingsExists;

        public FolderLayoutStatus(bool gameDataRootExists, bool uiLayerSettingsExists, bool specSettingsExists)
        {
            GameDataRootExists = gameDataRootExists;
            UiLayerSettingsExists = uiLayerSettingsExists;
            SpecSettingsExists = specSettingsExists;
        }

        public bool AllOk => GameDataRootExists && UiLayerSettingsExists && SpecSettingsExists;
    }

    // §7 B-6(置き場所)のプリセット。
    public enum FolderLayoutPreset
    {
        Default,         // 既定(Assets/GameData 等、現状のまま)。
        UnderParentFolder, // 1 つの親フォルダ配下(例 Assets/_Project/DDrive/{GameData,Generated,SourceAssets,Specs})。
        Custom,          // 個別指定(4 つのパスをそれぞれ入力)。
    }

    // フォルダ配置の計算結果(4 パス)。DDriveProjectSettings への反映は呼び出し側(ProjectSetupActions)が行う。
    public readonly struct FolderLayoutPaths
    {
        public readonly string GameDataRoot;
        public readonly string GeneratedRoot;
        public readonly string SourceAssetsRoot;
        public readonly string SpecsRoot;

        public FolderLayoutPaths(string gameDataRoot, string generatedRoot, string sourceAssetsRoot, string specsRoot)
        {
            GameDataRoot = gameDataRoot;
            GeneratedRoot = generatedRoot;
            SourceAssetsRoot = sourceAssetsRoot;
            SpecsRoot = specsRoot;
        }
    }
}
