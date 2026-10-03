using System.Collections.Generic;
using System.IO;
using DDrive.Editor.Menu;
using DDrive.Editor.Migration;
using DDrive.Editor.Settings;
using DDrive.Editor.Setup;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Update
{
    // [42_distribution.md] §4.2 手順 5/§6 P-8(2026-09-20) — 持ち込み先が版を進めた直後に開く更新ウィンドウ。
    // 表示 = 現在の版 / 前回適用した版 / その間の CHANGELOG の該当節(「破壊あり」があれば警告)。
    // 「更新を適用」= マイグレーション → ID/Tuning 再生成 → Addressables 同期 → Validation → LastAppliedVersion
    // 更新の順で実行し、途中で失敗したら止まる(UpdateActions.Apply に委譲)。
    // P-7 のマイグレーション専用メニュー(MigrationMenu)はそのまま残しつつ、同じ操作をここにも置く
    // (統合してよい、の指示に沿って重複実装はしない)。
    // [09_editor_tools.md] §7 のとおり ScrollView ルート、メニューは DDriveMenu 経由。
    //
    // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 「1. 更新チェック」を追加。
    // `git ls-remote --tags` で最新のタグを取得し、現在の参照(manifest の "#ref")と比較して
    // 最新です/MINOR/MAJOR を表示する。「manifest を選んだ版に更新する」は `#ref` だけを差し替える
    // (`GitPackageUrl.WithRef`)。差し替え前の値は `DDriveProjectSettings.PreviousPackageRef` に退避し、
    // 「前の参照に戻す」で入れ替えられる(ロールバック用、[42] §4.4)。「起動時に確認」は作らない(手動のみ)。
    //
    // [42_distribution.md] §4.2/§6 P-15(2026-10-03) — 最上段に「パッケージ」の一覧を追加し、D-Drive 以外の
    // git URL パッケージも同じ更新チェック・版上げ・元に戻す・CHANGELOG 表示で扱えるようにした
    // (1 行目は常に D-Drive。行を選ぶと下の節がそのパッケージの内容に切り替わる)。URL を入力して管理対象に追加でき、
    // `package.json` の `ddriveUpdate`(requires / compatibleWith)による依存の確認も行う。D-Drive 専用の
    // 「3〜6」(マイグレーション・更新を適用・テスト・スキル)は D-Drive を選んだときだけ出る。
    // ロジックは純関数側(`PackageAddPlanner` / `PackageManifestOps` / `ManagedPackageRows` /
    // `PackageDependencyChecker` / `UpdatePreflight`)に置き、このウィンドウは配線と表示だけを持つ。
    public sealed class UpdateWindow : EditorWindow
    {
        private const string SelectedPackageKey = "DDrive.Update.SelectedPackage";
        private const string PendingAddKey = "DDrive.Update.PendingAdd";

        private static readonly Color ErrorColor = new(0.85f, 0.2f, 0.2f);
        private static readonly Color WarningColor = new(0.85f, 0.55f, 0.1f);

        [MenuItem(DDriveMenu.Update + "更新ウィンドウ")]
        public static void Open()
        {
            var window = GetWindow<UpdateWindow>("D-Drive 更新");
            window.minSize = new Vector2(480, 360);
        }

        private VisualElement _packagesBody;
        private Label _selectedHeader;
        private VisualElement _updateCheckBody;
        private VisualElement _versionsBody;
        private VisualElement _changelogBody;
        private VisualElement _migrationBody;
        private VisualElement _applyBody;
        private VisualElement _testablesBody;
        private VisualElement _skillBody;
        private VisualElement _postCheckBody;
        private Foldout _migrationFoldout;
        private Foldout _applyFoldout;
        private Foldout _testablesFoldout;
        private Foldout _skillFoldout;
        private Foldout _postCheckFoldout;

        // [42_distribution.md] §4.2/§6 P-14(2026-09-20) — 実 `git` CLI 呼び出しをここで 1 回だけ生成する
        // (テストからは `IGitTagLister` を差し替えられる設計だが、ウィンドウ自体は EditMode テストの
        // 対象外なので既定実装を直接持つ)。
        private readonly IGitTagLister _gitTagLister = new GitCliTagLister();
        private readonly IRemotePackageJsonFetcher _packageJsonFetcher = new GitSparsePackageJsonFetcher();

        // 一覧の状態(再構築のたびに作り直す)。
        private List<PackageState> _installed = new();
        private List<PackageRow> _rows = new();
        private List<PackageDependencyIssue> _issues = new();
        private readonly Dictionary<string, UpdateCheckLogic.Result> _latestById = new();
        private string _addInput = string.Empty;
        private string _addMessage;
        private AddRequest _addRequest;
        private string _addRequestValue;

        public void CreateGUI()
        {
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(scrollView);

            scrollView.Add(new Label("D-Drive 更新ウィンドウ") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginBottom = 4 } });
            scrollView.Add(WrappingLabel("持ち込み先の manifest.json を新しい版に進めた直後に使います([docs/42_distribution.md] §4.2)。D-Drive 以外の git URL パッケージも、URL を入力して管理対象に追加すれば同じ形式で更新できます。"));

            scrollView.Add(BuildSection("パッケージ", out _packagesBody));

            _selectedHeader = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, marginBottom = 2 } };
            scrollView.Add(_selectedHeader);

            scrollView.Add(BuildSection("1. 更新チェック", out _updateCheckBody));

            // CHANGELOG 表示部分は「2. 版と CHANGELOG」の再検査時に丸ごと Clear() されるため、
            // _versionsBody の子ではなく同じ Foldout の別の子(兄弟)にする(親を Clear すると
            // 子ごと消えてしまい、再検査後に _changelogBody が描画されなくなる事故を避ける)。
            var versionsSection = new Foldout { text = "2. 版と CHANGELOG", value = true };
            _versionsBody = new VisualElement();
            _changelogBody = new VisualElement();
            versionsSection.Add(_versionsBody);
            versionsSection.Add(_changelogBody);
            scrollView.Add(versionsSection);

            _migrationFoldout = BuildSection("3. マイグレーション(プレビュー)", out _migrationBody);
            _applyFoldout = BuildSection("4. 更新を適用", out _applyBody);
            _testablesFoldout = BuildSection("5. テストを有効化する(既定 OFF)", out _testablesBody);
            _skillFoldout = BuildSection("6. エージェント向けスキルを更新", out _skillBody);
            _postCheckFoldout = BuildSection("3. 更新後の確認", out _postCheckBody);
            scrollView.Add(_migrationFoldout);
            scrollView.Add(_applyFoldout);
            scrollView.Add(_testablesFoldout);
            scrollView.Add(_skillFoldout);
            scrollView.Add(_postCheckFoldout);

            ResumePendingAdd();
            RefreshAll();
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollAddRequest;
        }

        private static Foldout BuildSection(string title, out VisualElement body)
        {
            var foldout = new Foldout { text = title, value = true };
            body = new VisualElement();
            foldout.Add(body);
            return foldout;
        }

        private static Label WrappingLabel(string text) => new(text)
        {
            style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1, minWidth = 0, marginBottom = 4 },
        };

        private static Label ColoredLabel(string text, Color color)
        {
            var label = WrappingLabel(text);
            label.style.color = color;
            return label;
        }

        // ── 全体の再構築 ──

        private void RefreshAll()
        {
            RefreshPackagesSection();
            RefreshSelectedSections();
        }

        // 選択中のパッケージに依存する節(1〜6)をまとめて作り直す。
        private void RefreshSelectedSections()
        {
            var row = SelectedRow();
            var isDDrive = row != null && row.IsDDrive;

            _selectedHeader.text = row == null ? string.Empty : $"選択中のパッケージ: {row.DisplayName}({row.Id})";

            _migrationFoldout.style.display = isDDrive ? DisplayStyle.Flex : DisplayStyle.None;
            _applyFoldout.style.display = isDDrive ? DisplayStyle.Flex : DisplayStyle.None;
            _testablesFoldout.style.display = isDDrive ? DisplayStyle.Flex : DisplayStyle.None;
            _skillFoldout.style.display = isDDrive ? DisplayStyle.Flex : DisplayStyle.None;
            _postCheckFoldout.style.display = isDDrive ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshUpdateCheckSection();
            RefreshVersionsSection();
            if (isDDrive)
            {
                RefreshMigrationSection();
                RefreshApplySection();
                RefreshTestablesSection();
                RefreshSkillSection();
            }
            else
            {
                RefreshPostCheckSection();
            }
        }

        private PackageRow SelectedRow()
        {
            var id = SessionState.GetString(SelectedPackageKey, ManagedPackageRows.DDrivePackageId);
            foreach (var row in _rows)
            {
                if (row.Id == id)
                {
                    return row;
                }
            }

            return _rows.Count > 0 ? _rows[0] : null;
        }

        private void Select(string packageId)
        {
            SessionState.SetString(SelectedPackageKey, packageId);
            RefreshPackagesSection();
            RefreshSelectedSections();
        }

        // ── パッケージ(一覧・URL 入力・候補・依存の確認) ──

        private void RebuildModel()
        {
            var manifest = ManifestJson.LoadProjectManifest();
            var settings = DDriveProjectSettings.instance;
            var managedIds = new List<string>();
            foreach (var entry in settings.ManagedPackages)
            {
                if (entry != null)
                {
                    managedIds.Add(entry.PackageId);
                }
            }

            _installed = InstalledPackages.Load();
            _rows = ManagedPackageRows.Build(manifest, managedIds, _installed);
            _issues = PackageDependencyChecker.Check(_installed);
        }

        private void RefreshPackagesSection()
        {
            _packagesBody.Clear();
            RebuildModel();

            var manifest = ManifestJson.LoadProjectManifest();
            if (manifest == null)
            {
                _packagesBody.Add(WrappingLabel("Packages/manifest.json が読めませんでした。"));
                return;
            }

            var selectedId = SelectedRow()?.Id;
            foreach (var row in _rows)
            {
                _packagesBody.Add(BuildRowElement(row, row.Id == selectedId));
            }

            var checkAll = new Button(CheckLatestForAllRows) { text = "一覧の最新版をまとめて確認" };
            _packagesBody.Add(checkAll);

            // URL を入力して追加
            _packagesBody.Add(new Label("URL を入力して追加") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });
            _packagesBody.Add(WrappingLabel("git URL(https://github.com/<owner>/<repo>.git?path=<dir>、git+https://…、ssh://…。末尾に #vX.Y.Z を付けても可)、または manifest にあるパッケージ ID を入力します。manifest に無い URL は、最新の vX.Y.Z で導入してから管理対象に登録します。"));
            var field = new TextField { value = _addInput, style = { flexGrow = 1 } };
            field.RegisterValueChangedCallback(evt => _addInput = evt.newValue);
            _packagesBody.Add(field);
            _packagesBody.Add(new Button(() => AddFromInput(_addInput)) { text = "追加" });
            if (!string.IsNullOrEmpty(_addMessage))
            {
                _packagesBody.Add(WrappingLabel(_addMessage));
            }

            // 未登録の git URL 依存(候補)
            var managedIds = new List<string>();
            foreach (var row in _rows)
            {
                managedIds.Add(row.Id);
            }

            var candidates = ManagedPackageRows.FindCandidates(manifest, managedIds);
            if (candidates.Count > 0)
            {
                _packagesBody.Add(new Label("manifest にある git URL の依存(未登録)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });
                foreach (var candidate in candidates)
                {
                    var line = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    var text = candidate.HasVersionTagRef
                        ? candidate.Id
                        : $"{candidate.Id}(#vX.Y.Z 形式の参照ではないため、最新版は判定できません)";
                    line.Add(new Label(text) { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } });
                    var id = candidate.Id;
                    line.Add(new Button(() => RegisterManaged(id)) { text = "登録" });
                    _packagesBody.Add(line);
                }
            }

            // 依存の確認
            _packagesBody.Add(new Label("依存の確認(package.json の ddriveUpdate)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });
            if (_issues.Count == 0)
            {
                _packagesBody.Add(WrappingLabel("満たされていない依存はありません。"));
            }
            else
            {
                AddIssueLabels(_packagesBody, _issues);
            }

            _packagesBody.Add(new Button(RefreshAll) { text = "依存を再検査" });
        }

        private VisualElement BuildRowElement(PackageRow row, bool selected)
        {
            var line = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 2,
                },
            };
            if (selected)
            {
                line.style.backgroundColor = new Color(0.25f, 0.45f, 0.75f, 0.35f);
            }

            var id = row.Id;
            var button = new Button(() => Select(id))
            {
                text = DescribeRow(row),
                style = { flexGrow = 1, flexShrink = 1, unityTextAlign = TextAnchor.MiddleLeft, whiteSpace = WhiteSpace.Normal },
            };
            line.Add(button);

            if (!row.IsDDrive)
            {
                line.Add(new Button(() => UnregisterManaged(id)) { text = "登録解除" });
            }

            return line;
        }

        private string DescribeRow(PackageRow row)
        {
            var version = row.Installed != null ? "v" + row.CurrentVersion : "未導入";
            string latest;
            if (_latestById.TryGetValue(row.Id, out var check))
            {
                latest = check.LatestVersion == null
                    ? "判定不可"
                    : $"v{check.LatestVersion}{BumpSuffix(check.Bump)}";
            }
            else
            {
                latest = "未確認";
            }

            var issues = new List<PackageDependencyIssue>();
            foreach (var issue in _issues)
            {
                if (issue.Involves(row.Id))
                {
                    issues.Add(issue);
                }
            }

            string dependency;
            if (PackageDependencyChecker.HasAtLeast(issues, DependencyIssueSeverity.Error))
            {
                dependency = "✗ 依存を満たしていません";
            }
            else if (PackageDependencyChecker.HasAtLeast(issues, DependencyIssueSeverity.Warning))
            {
                dependency = "⚠ 依存に注意";
            }
            else if (issues.Count > 0)
            {
                dependency = "ℹ 確認事項あり";
            }
            else
            {
                dependency = "依存 OK";
            }

            return $"{row.DisplayName}({row.Id})  現在 {version} / 最新 {latest} / {dependency}";
        }

        private void AddIssueLabels(VisualElement body, IReadOnlyList<PackageDependencyIssue> issues)
        {
            foreach (var issue in issues)
            {
                switch (issue.Severity)
                {
                    case DependencyIssueSeverity.Error:
                        body.Add(ColoredLabel("✗ " + issue.Message, ErrorColor));
                        break;
                    case DependencyIssueSeverity.Warning:
                        body.Add(ColoredLabel("⚠ " + issue.Message, WarningColor));
                        break;
                    default:
                        body.Add(WrappingLabel("ℹ " + issue.Message));
                        break;
                }
            }
        }

        private void RegisterManaged(string packageId)
        {
            DDriveProjectSettings.instance.RegisterManagedPackage(packageId);
            Debug.Log($"[DDrive][Update] {packageId} を管理対象に登録しました。");
            SessionState.SetString(SelectedPackageKey, packageId);
            _addMessage = null;
            RefreshAll();
        }

        private void UnregisterManaged(string packageId)
        {
            DDriveProjectSettings.instance.UnregisterManagedPackage(packageId);
            Debug.Log($"[DDrive][Update] {packageId} を管理対象から外しました(manifest.json からは消していません)。");
            if (SessionState.GetString(SelectedPackageKey, string.Empty) == packageId)
            {
                SessionState.SetString(SelectedPackageKey, ManagedPackageRows.DDrivePackageId);
            }

            _latestById.Remove(packageId);
            RefreshAll();
        }

        private void AddFromInput(string input)
        {
            var manifest = ManifestJson.LoadProjectManifest();
            if (manifest == null)
            {
                SetAddMessage("Packages/manifest.json が読めませんでした。", true);
                return;
            }

            var plan = new PackageAddPlanner(_gitTagLister).Plan(input, manifest);
            switch (plan.Outcome)
            {
                case PackageAddOutcome.RegisterExisting:
                    RegisterManaged(plan.PackageId);
                    _addInput = string.Empty;
                    return;

                case PackageAddOutcome.AddNew:
                    if (_addRequest != null && !_addRequest.IsCompleted)
                    {
                        SetAddMessage("別のパッケージを導入中です。完了するまで待ってください。", true);
                        return;
                    }

                    if (!EditorUtility.DisplayDialog(
                            "D-Drive 更新",
                            $"次の内容でパッケージを導入します(manifest.json に追加されます)。\n\n{plan.ManifestValue}\n\nよろしいですか?",
                            "導入する", "キャンセル"))
                    {
                        return;
                    }

                    _addRequestValue = plan.ManifestValue;
                    SessionState.SetString(PendingAddKey, plan.ManifestValue);
                    _addRequest = Client.Add(plan.ManifestValue);
                    EditorApplication.update -= PollAddRequest;
                    EditorApplication.update += PollAddRequest;
                    SetAddMessage($"導入中: {plan.ManifestValue}", false);
                    return;

                default:
                    // Invalid / NoTags / TagListFailed: 何も変えない(警告のみ)。
                    SetAddMessage(plan.Message, true);
                    return;
            }
        }

        private void PollAddRequest()
        {
            if (_addRequest == null)
            {
                EditorApplication.update -= PollAddRequest;
                return;
            }

            if (!_addRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollAddRequest;
            var request = _addRequest;
            _addRequest = null;
            SessionState.EraseString(PendingAddKey);

            if (request.Status == StatusCode.Success && request.Result != null)
            {
                var id = request.Result.name;
                DDriveProjectSettings.instance.RegisterManagedPackage(id);
                Debug.Log($"[DDrive][Update] {id} を導入し、管理対象に登録しました({_addRequestValue})。");
                SessionState.SetString(SelectedPackageKey, id);
                _addInput = string.Empty;
                _addMessage = null;
                RefreshAll();
            }
            else
            {
                SetAddMessage("導入に失敗しました: " + (request.Error != null ? request.Error.message : "不明なエラー"), true);
            }
        }

        // 導入の完了後にドメインリロードで要求の結果を受け取れなかった場合に備え、
        // 開き直したときに manifest に入っていれば管理対象へ登録する。
        private void ResumePendingAdd()
        {
            var pending = SessionState.GetString(PendingAddKey, string.Empty);
            if (string.IsNullOrEmpty(pending))
            {
                return;
            }

            var manifest = ManifestJson.LoadProjectManifest();
            var plan = new PackageAddPlanner(null).Plan(pending, manifest);
            if (plan.Outcome == PackageAddOutcome.RegisterExisting)
            {
                DDriveProjectSettings.instance.RegisterManagedPackage(plan.PackageId);
                SessionState.SetString(SelectedPackageKey, plan.PackageId);
                SessionState.EraseString(PendingAddKey);
                Debug.Log($"[DDrive][Update] 導入が完了していた {plan.PackageId} を管理対象に登録しました。");
            }
        }

        private void SetAddMessage(string message, bool warning)
        {
            _addMessage = (warning ? "⚠ " : string.Empty) + message;
            if (warning)
            {
                Debug.LogWarning("[DDrive][Update] " + message);
            }

            RefreshPackagesSection();
        }

        private void CheckLatestForAllRows()
        {
            foreach (var row in _rows)
            {
                if (row.InManifest && row.IsGit)
                {
                    CheckLatest(row, out _);
                }
            }

            RefreshPackagesSection();
            RefreshSelectedSections();
        }

        // `git ls-remote --tags` を実行して最新を判定し、一覧用にキャッシュする。失敗時は warning を返す(例外で止めない)。
        private List<System.Version> CheckLatest(PackageRow row, out string warning)
        {
            var stdout = _gitTagLister.ListTags(row.Url.CloneUrl, out warning);
            if (warning != null)
            {
                Debug.LogWarning($"[DDrive][Update] 更新チェック({row.Id}): {warning}");
                return null;
            }

            var tags = row.IsDDrive ? GitTagListParser.Parse(stdout) : GitTagListParser.ParseVersionTags(stdout);
            _latestById[row.Id] = UpdateCheckLogic.Evaluate(row.Url.Ref, CurrentVersionOf(row), tags);
            return tags;
        }

        // ── 対象パッケージごとの差(D-Drive は従来の単数フィールド、その他は ManagedPackages の要素) ──

        private static string PreviousRefOf(PackageRow row)
            => row.IsDDrive
                ? DDriveProjectSettings.instance.PreviousPackageRef
                : DDriveProjectSettings.instance.FindManagedPackage(row.Id)?.PreviousRef ?? string.Empty;

        private static void SetPreviousRef(PackageRow row, string value)
        {
            if (row.IsDDrive)
            {
                DDriveProjectSettings.instance.PreviousPackageRef = value ?? string.Empty;
            }
            else
            {
                DDriveProjectSettings.instance.SetManagedPackagePreviousRef(row.Id, value);
            }
        }

        private static string LastAppliedOf(PackageRow row)
            => row.IsDDrive
                ? DDriveProjectSettings.instance.LastAppliedVersion
                : DDriveProjectSettings.instance.FindManagedPackage(row.Id)?.LastAppliedVersion ?? string.Empty;

        private static string CurrentVersionOf(PackageRow row)
            => row.IsDDrive ? ResolveCurrentVersion(ResolvePackageInfo()) : row.CurrentVersion;

        // ── 1. 更新チェック ──

        private void RefreshUpdateCheckSection()
        {
            _updateCheckBody.Clear();

            var row = SelectedRow();
            if (row == null)
            {
                return;
            }

            var manifest = ManifestJson.LoadProjectManifest();
            if (manifest == null)
            {
                _updateCheckBody.Add(WrappingLabel("Packages/manifest.json が読めませんでした。"));
                return;
            }

            var rawValue = ManifestJson.GetDependencyValue(manifest, row.Id);
            var parsed = GitPackageUrl.Parse(rawValue);

            if (rawValue == null && !row.IsDDrive)
            {
                _updateCheckBody.Add(WrappingLabel("manifest.json にこのパッケージがありません(未導入)。"));
                return;
            }

            if (!parsed.IsGitUrl)
            {
                _updateCheckBody.Add(WrappingLabel("更新チェック対象外(git URL 参照ではありません)。"));
                return;
            }

            var currentPackageVersion = CurrentVersionOf(row);

            _updateCheckBody.Add(WrappingLabel(
                $"現在の参照: {(string.IsNullOrEmpty(parsed.Ref) ? "(不明)" : parsed.Ref)}(package.json の版: {currentPackageVersion})"));

            var resultBody = new VisualElement();
            var dropdown = new DropdownField("更新先の版", new List<string>(), 0) { style = { display = DisplayStyle.None } };
            var applyButton = new Button { text = "manifest を選んだ版に更新する", style = { display = DisplayStyle.None } };

            var checkButton = new Button(() =>
            {
                resultBody.Clear();
                dropdown.style.display = DisplayStyle.None;
                applyButton.style.display = DisplayStyle.None;

                var tags = CheckLatest(row, out var warning);
                if (warning != null)
                {
                    resultBody.Add(ColoredLabel("⚠ " + warning, ErrorColor));
                    return;
                }

                var check = _latestById[row.Id];

                if (check.LatestVersion == null)
                {
                    resultBody.Add(WrappingLabel(row.IsDDrive
                        ? "タグを取得できませんでした(vX.Y.Z 形式のタグが見つかりません)。"
                        : "vX.Y.Z 形式のタグが見つかりません(最新版を判定できません。このパッケージの版上げは URL の #ref を手で書き換えてください)。"));
                    RefreshPackagesSection();
                    return;
                }

                resultBody.Add(WrappingLabel($"最新: v{check.LatestVersion}{BumpSuffix(check.Bump)}"));

                if (check.Bump == UpdateCheckLogic.BumpKind.UpToDate)
                {
                    resultBody.Add(WrappingLabel("最新です。"));
                }
                else if (check.Bump == UpdateCheckLogic.BumpKind.Major)
                {
                    resultBody.Add(ColoredLabel("⚠ MAJOR 更新です。docs/migrations/vN.md(無ければ CHANGELOG の「互換性」)を先に読んでください([docs/42_distribution.md] §5.12)。", ErrorColor));
                }

                if (tags.Count > 0)
                {
                    var choices = new List<string>();
                    foreach (var v in tags) // GitTagListParser は降順
                    {
                        choices.Add("v" + v);
                    }

                    dropdown.choices = choices;
                    dropdown.value = choices[0]; // 最新(降順の先頭)
                    dropdown.style.display = DisplayStyle.Flex;
                    applyButton.style.display = DisplayStyle.Flex;
                }

                RefreshPackagesSection();
            }) { text = "最新の版を確認" };

            applyButton.clicked += () =>
            {
                if (dropdown.choices == null || dropdown.choices.Count == 0 || string.IsNullOrEmpty(dropdown.value))
                {
                    return;
                }

                var targetRef = dropdown.value;
                if (!ConfirmWithPreflight(
                        row, parsed, targetRef,
                        $"manifest.json の {row.Id} を {targetRef} に更新します。",
                        row.IsDDrive
                            ? "再コンパイル後に「4. 更新を適用」を実行してください。元に戻したいときは「前の参照に戻す」を使えます。"
                            : "再コンパイル後に「3. 更新後の確認」を実行してください。元に戻したいときは「前の参照に戻す」を使えます。",
                        "更新する"))
                {
                    return;
                }

                var currentManifest = ManifestJson.LoadProjectManifest();
                if (currentManifest == null)
                {
                    Debug.LogError("[DDrive][Update] Packages/manifest.json が読めませんでした。");
                    return;
                }

                if (!PackageManifestOps.TryBumpRef(currentManifest, row.Id, targetRef, out var previousValue, out _))
                {
                    Debug.LogWarning($"[DDrive][Update] {row.Id} は git URL 参照ではないため更新しませんでした。");
                    return;
                }

                SetPreviousRef(row, previousValue ?? string.Empty);
                ManifestJson.SaveProjectManifest(currentManifest);
                AssetDatabase.Refresh();
                Client.Resolve();

                Debug.Log($"[DDrive][Update] manifest.json の {row.Id} を {targetRef} に更新しました。再コンパイル後に依存を再確認します。");
                RefreshAll();
            };

            var previous = PreviousRefOf(row);
            var hasPrevious = !string.IsNullOrEmpty(previous);

            // (c) 版を上げた後(再解決後)に依存が満たされない場合は「元に戻す」を目立たせる。
            var rowIssues = new List<PackageDependencyIssue>();
            foreach (var issue in _issues)
            {
                if (issue.Involves(row.Id))
                {
                    rowIssues.Add(issue);
                }
            }

            var broken = hasPrevious && PackageDependencyChecker.HasAtLeast(rowIssues, DependencyIssueSeverity.Warning);

            var rollbackButton = new Button(() =>
            {
                var prev = PreviousRefOf(row);
                if (string.IsNullOrEmpty(prev))
                {
                    return;
                }

                if (!ConfirmWithPreflight(
                        row, GitPackageUrl.Parse(prev), GitPackageUrl.Parse(prev).Ref,
                        $"manifest.json の {row.Id} を前の参照に戻します。\n\n{prev}",
                        string.Empty,
                        "戻す"))
                {
                    return;
                }

                var currentManifest = ManifestJson.LoadProjectManifest();
                if (currentManifest == null)
                {
                    Debug.LogError("[DDrive][Update] Packages/manifest.json が読めませんでした。");
                    return;
                }

                PackageManifestOps.TryRestore(currentManifest, row.Id, prev, out var replaced);
                ManifestJson.SaveProjectManifest(currentManifest);
                SetPreviousRef(row, replaced ?? string.Empty); // 入れ替え(再度押すと戻せる)
                AssetDatabase.Refresh();
                Client.Resolve();

                Debug.Log($"[DDrive][Update] manifest.json の {row.Id} を前の参照に戻しました。");
                RefreshAll();
            })
            { text = broken ? "⚠ 前の参照に戻す(依存を満たしていません)" : "前の参照に戻す" };
            rollbackButton.SetEnabled(hasPrevious);
            if (broken)
            {
                rollbackButton.style.backgroundColor = new Color(0.75f, 0.25f, 0.2f);
                rollbackButton.style.color = Color.white;
            }

            if (rowIssues.Count > 0)
            {
                _updateCheckBody.Add(WrappingLabel("このパッケージに関わる依存の確認:"));
                AddIssueLabels(_updateCheckBody, rowIssues);
            }

            _updateCheckBody.Add(checkButton);
            _updateCheckBody.Add(resultBody);
            _updateCheckBody.Add(dropdown);
            _updateCheckBody.Add(applyButton);
            _updateCheckBody.Add(rollbackButton);
            _updateCheckBody.Add(WrappingLabel(row.IsDDrive
                ? "manifest を更新すると Unity が再コンパイルします。再コンパイル後は下の「4. 更新を適用」がそのまま使えます" +
                  "(前回適用した版が新しい現在の版と異なるため)。"
                : "manifest を更新すると Unity が再コンパイルします。再コンパイル後に依存を再確認し、下の「3. 更新後の確認」から Validation > Run All を実行できます。"));
        }

        // 版を上げる(または戻す)前に、上げ先の package.json を取得して依存を事前確認し、結果を添えて確認ダイアログを出す。
        // 取得に失敗しても続行できる(「事前確認できなかった。更新後に確認します」)。true = 実行してよい。
        private bool ConfirmWithPreflight(PackageRow row, GitPackageUrl targetUrl, string targetRef, string headline, string footer, string okLabel)
        {
            var url = targetUrl.IsGitUrl ? targetUrl : row.Url;
            var preflight = UpdatePreflight.Run(_packageJsonFetcher, url, row.Id, targetRef, _installed);

            foreach (var issue in preflight.Issues)
            {
                Debug.LogWarning($"[DDrive][Update] 事前確認({row.Id} → {targetRef}): {issue.Message}");
            }

            var body = headline + "\n\n" + preflight.Message;
            var hasProblem = PackageDependencyChecker.HasAtLeast(preflight.Issues, DependencyIssueSeverity.Warning);
            if (preflight.Issues.Count > 0)
            {
                body += "\n";
                foreach (var issue in preflight.Issues)
                {
                    body += "\n・" + issue.Message;
                }
            }

            if (hasProblem)
            {
                body += "\n\n依存が満たされなくなる可能性があります。それでも続けますか?";
            }

            if (!string.IsNullOrEmpty(footer))
            {
                body += "\n\n" + footer;
            }

            if (!hasProblem)
            {
                body += "\n\nよろしいですか?";
            }

            return EditorUtility.DisplayDialog("D-Drive 更新", body, hasProblem ? "それでも" + okLabel : okLabel, "キャンセル");
        }

        private static string BumpSuffix(UpdateCheckLogic.BumpKind bump) => bump switch
        {
            UpdateCheckLogic.BumpKind.Major => "(MAJOR)",
            UpdateCheckLogic.BumpKind.Minor => "(MINOR)",
            UpdateCheckLogic.BumpKind.Patch => "(PATCH)",
            _ => string.Empty,
        };

        // ── 2. 版と CHANGELOG ──

        private static UnityEditor.PackageManager.PackageInfo ResolvePackageInfo()
            => UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DDrive.Runtime.DDriveVersion).Assembly);

        private static string ResolveCurrentVersion(UnityEditor.PackageManager.PackageInfo packageInfo)
            => !string.IsNullOrEmpty(packageInfo?.version) ? packageInfo.version : DDrive.Runtime.DDriveVersion.Value;

        private void RefreshVersionsSection()
        {
            _versionsBody.Clear();
            _changelogBody.Clear();

            var row = SelectedRow();
            if (row == null)
            {
                return;
            }

            var settings = DDriveProjectSettings.instance;
            var currentVersion = CurrentVersionOf(row);
            var lastApplied = LastAppliedOf(row);

            _versionsBody.Add(WrappingLabel($"現在の版: {(string.IsNullOrEmpty(currentVersion) ? "(未導入)" : currentVersion)}"));
            _versionsBody.Add(WrappingLabel(row.IsDDrive
                ? $"前回適用した版: {(string.IsNullOrEmpty(lastApplied) ? "未適用" : lastApplied)}"
                : $"前回確認した版: {(string.IsNullOrEmpty(lastApplied) ? "未確認" : lastApplied)}"));

            // D-Drive の開発リポジトリ判定(2 階層上の CHANGELOG)は D-Drive のときだけ使う。
            // その他のパッケージは「パッケージ直下の CHANGELOG.md」だけを見る(resolvedPath 基準)。
            string changelogPath;
            if (row.IsDDrive)
            {
                var packageInfo = ResolvePackageInfo();
                changelogPath = packageInfo != null
                    ? ChangelogLocator.ResolvePath(packageInfo.resolvedPath, settings.IsDevelopmentRepo)
                    : null;
            }
            else
            {
                changelogPath = ChangelogLocator.ResolvePackageOnlyPath(row.Installed?.ResolvedPath);
            }

            if (changelogPath == null)
            {
                _changelogBody.Add(WrappingLabel("CHANGELOG.md が見つかりませんでした。"));
            }
            else
            {
                var sections = ChangelogRangeReader.ParseSections(File.ReadAllText(changelogPath));
                var range = ChangelogRangeReader.ExtractRange(sections, lastApplied, currentVersion);

                if (range.Count == 0)
                {
                    _changelogBody.Add(WrappingLabel(row.IsDDrive
                        ? "表示できる更新節がありません(前回適用した版が現在の版と同じか、CHANGELOG に該当節がありません)。"
                        : "表示できる更新節がありません(前回確認した版が現在の版と同じか、CHANGELOG に該当節がありません)。"));
                }

                var hasBreaking = false;
                foreach (var section in range)
                {
                    if (ChangelogCompatibilityAnalyzer.HasBreakingChange(section.Body))
                    {
                        hasBreaking = true;
                    }
                }

                if (hasBreaking)
                {
                    _changelogBody.Add(ColoredLabel("⚠ 「破壊あり」の変更が含まれています。適用前に docs/migrations/ の移行ガイド(無ければ CHANGELOG の「互換性」)を確認してください([docs/42_distribution.md] §5.12)。", ErrorColor));
                }

                foreach (var section in range)
                {
                    _changelogBody.Add(new Label(section.HeaderLine) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
                    var compat = ChangelogCompatibilityAnalyzer.ExtractCompatibilityText(section.Body);
                    if (!string.IsNullOrEmpty(compat))
                    {
                        _changelogBody.Add(WrappingLabel("互換性: " + compat.Trim()));
                    }
                }
            }

            _versionsBody.Add(new Button(() =>
            {
                RefreshVersionsSection();
                if (row.IsDDrive)
                {
                    RefreshMigrationSection();
                }
            }) { text = "再検査" });
        }

        // ── 3. 更新後の確認(D-Drive 以外) ──

        private void RefreshPostCheckSection()
        {
            _postCheckBody.Clear();
            var row = SelectedRow();
            if (row == null || row.IsDDrive)
            {
                return;
            }

            _postCheckBody.Add(WrappingLabel("D-Drive 以外のパッケージの更新後処理(マイグレーション等)はこのウィンドウでは行いません。版を上げて再コンパイルした後は、依存の再確認と Validation > Run All を実行してください。"));
            _postCheckBody.Add(new Button(() =>
            {
                CI.RunAllMenuItem();
                var version = CurrentVersionOf(row);
                DDriveProjectSettings.instance.SetManagedPackageLastAppliedVersion(row.Id, version);
                Debug.Log($"[DDrive][Update] {row.Id} v{version} で Validation > Run All を実行しました。");
                RefreshAll();
            }) { text = "Validation > Run All を実行して確認済みにする" });
        }

        // ── 3. マイグレーション(プレビュー) ──

        private void RefreshMigrationSection()
        {
            _migrationBody.Clear();

            var plan = DDriveMigrationRunner.PlanProject();
            _migrationBody.Add(WrappingLabel(plan.TotalCount == 0
                ? "未適用のマイグレーションはありません。"
                : $"未適用のマイグレーションが {plan.TotalCount} 件あります(Data {plan.DataMigrations.Count} 件 / プロジェクト {plan.ProjectMigrations.Count} 件)。"));
            _migrationBody.Add(WrappingLabel("実際の適用は下の「更新を適用」に含まれます。ここでは件数の確認のみできます。"));
            _migrationBody.Add(new Button(RefreshMigrationSection) { text = "再検査" });
        }

        // ── 4. 更新を適用 ──

        private void RefreshApplySection()
        {
            _applyBody.Clear();
            _applyBody.Add(WrappingLabel("順番: (1) マイグレーション → (2) ID/Tuning 再生成 → (3) Addressables 同期 → (4) Validation → (5) 前回適用した版を更新。途中で失敗したら以降は実行しません。"));

            var log = new ScrollView(ScrollViewMode.Vertical) { style = { height = 140, borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1 } };
            _applyBody.Add(new Button(() => RunApply(log)) { text = "更新を適用" });
            _applyBody.Add(log);
        }

        private void RunApply(ScrollView log)
        {
            log.Clear();

            var packageInfo = ResolvePackageInfo();
            var currentVersion = ResolveCurrentVersion(packageInfo);
            var steps = UpdateStepsFactory.CreateRealSteps(currentVersion);
            var result = UpdateActions.Apply(steps);

            foreach (var outcome in result.StepOutcomes)
            {
                var line = (outcome.Success ? "✓ " : "✗ ") + outcome.Name + ": " + outcome.Message;
                log.Add(WrappingLabel(line));

                if (outcome.Success)
                {
                    Debug.Log($"[DDrive][Update] {line}");
                }
                else
                {
                    Debug.LogError($"[DDrive][Update] {line}");
                }
            }

            if (result.MarkAppliedCalled)
            {
                log.Add(WrappingLabel($"✓ LastAppliedVersion を {currentVersion} に更新しました。"));
                Debug.Log($"[DDrive][Update] LastAppliedVersion を {currentVersion} に更新しました。");
            }
            else if (result.StepOutcomes.Count > 0)
            {
                log.Add(ColoredLabel("途中で失敗したため、以降の手順は実行していません(LastAppliedVersion は更新されていません)。", ErrorColor));
            }

            RefreshVersionsSection();
            RefreshMigrationSection();
        }

        // ── 5. テストを有効化する ──

        private void RefreshTestablesSection()
        {
            _testablesBody.Clear();
            var enabled = ProjectSetupActions.IsTestablesEnabled();
            var toggle = new Toggle("D-Drive のテストを有効化する(Test Runner に表示)") { value = enabled };
            toggle.RegisterValueChangedCallback(evt =>
            {
                ProjectSetupActions.SetTestablesEnabled(evt.newValue);
                RefreshTestablesSection();
            });
            _testablesBody.Add(toggle);
            _testablesBody.Add(WrappingLabel("既定 OFF。ON にすると manifest.json の testables に com.ddrive.core が追加され、Test Runner にテストが表示されます([docs/42_distribution.md] §2.1)。"));
        }

        // ── 6. エージェント向けスキルを更新 ──

        private void RefreshSkillSection()
        {
            _skillBody.Clear();
            var packageInfo = ResolvePackageInfo();
            var bundled = packageInfo != null && Directory.Exists(
                Path.Combine(packageInfo.resolvedPath, "Documentation~", "skills", "ddrive-consumer"));

            _skillBody.Add(WrappingLabel(bundled
                ? "パッケージに消費側スキル(ddrive-consumer)が同梱されています。"
                : "未同梱(P-10 で追加予定)。現時点では何も行いません。"));

            _skillBody.Add(new Button(() =>
            {
                if (packageInfo == null)
                {
                    Debug.Log("[DDrive][Update] エージェント向けスキルの更新: パッケージ情報が取得できませんでした。");
                    return;
                }

                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var result = ProjectSetupActions.CopyConsumerSkillIfBundled(projectRoot, packageInfo.version);
                Debug.Log($"[DDrive][Update] エージェント向けスキルの更新: {result}");
                RefreshSkillSection();
            })
            { text = "エージェント向けスキルを更新" });
        }
    }
}
