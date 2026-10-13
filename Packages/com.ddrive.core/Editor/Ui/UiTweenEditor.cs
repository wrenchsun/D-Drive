using System;
using System.Collections.Generic;
using DDrive.Editor.Inspector;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ui
{
    // UI Tween(UiTweenData)のエディター(docs/1010 UX-12。旧 UiTweenEditorWindow は Archive メニューへ退避)。
    // 共通基盤(UxEditorWindow<UiTweenData> / FieldGuide / UxRow / UxCard)で組み、ここに書くのは UI Tween 固有の部分だけ:
    // 欄の表(UiTweenFieldGuide)・トラックの一覧(1 トラック = 1 行。UiTweenTrackList)・雛形(UiTweenPresetPicker)・実要素での再生(UiTweenManager)・経路の制御点ハンドル。
    // 再生は実 UiTweenManager を EditorApplication.update から駆動する(ADR-4)。ウィンドウ自身は何も動かして描かない(動くのは対象に選んだ実要素 / 確認用シーンの仮画像)。
    // プリセットの一覧を大きく見る場所は別ウィンドウ UiPresetGallery(雛形の「ギャラリーを開く」)。
    [DataEditor(typeof(UiTweenData), "UI Tween Editor で開く")]
    internal sealed partial class UiTweenEditor : UxEditorWindow<UiTweenData>, IUiTweenTrackHost
    {
        // 空状態の「サンプルを作る」(docs/1010 UX-19)
        protected override bool HasSample => true;

        protected override UiTweenData CreateSample() => UxSamples.UiTween();

        private const string Page = "ui-tween";

        // Canvas Editor / Preset Gallery と共有する(「(ルート)」の直書きが各所に散っていたため)。
        internal const string RootElementLabel = "(ルート)";

        private const string PreviewRootName = "[D-Drive] UI Tween Preview";
        private const string NoElementChoice = "(なし)";
        private const int SplinePreviewSamples = 32;

        [SerializeField] private GameObject _collectRoot;
        [SerializeField] private RectTransform _previewTarget;
        [SerializeField] private string _selectedElementLabel = NoElementChoice;

        private readonly List<(string label, RectTransform rt)> _elementChoices = new List<(string, RectTransform)>();
        private readonly UiTweenSnapshot _snapshot = new UiTweenSnapshot();
        private UiTweenManager _previewManager;
        private Handle<UiTweenMarker> _previewHandle = Handle<UiTweenMarker>.Invalid;
        private double _lastTime;
        private bool _active;
        private GameObject _ownedPlaceholderRoot;
        private int _sceneTrack = -1;

        private UiTweenTrackList _trackList;
        private UiTweenPresetPicker _picker;
        private ObjectField _collectField;
        private DropdownField _elementDropdown;
        private Label _status;
        private Label _sceneOwner;

        // ── 入口 ──

        [MenuItem(DDriveMenu.Editors + "UI Tween")]
        public static void OpenFromMenu() => Open(Selection.activeObject as UiTweenData);

        public static void Open(UiTweenData target)
        {
            var w = OpenWindow<UiTweenEditor>("UI Tween Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }
        }

        // Canvas Editor 等、確認したい実要素が既に分かっている呼び出し元向け。collectRoot(実 Prefab インスタンスのルート)を渡すと「要素を自動収集」を自動実行し、
        // assignedElement があればそれを最初からプレビュー対象として選択状態にする(旧 UiTweenEditorWindow.Open と同じ引数)。
        public static UiTweenEditor Open(UiTweenData target, GameObject collectRoot, RectTransform assignedElement, string assignedElementLabel)
        {
            var w = OpenWindow<UiTweenEditor>("UI Tween Editor");
            if (target != null)
            {
                w.SetTarget(target);
            }

            if (collectRoot != null)
            {
                w._collectRoot = collectRoot;
                w._collectField?.SetValueWithoutNotify(collectRoot);
                w.CollectElements();
            }

            if (assignedElement != null)
            {
                var label = string.IsNullOrEmpty(assignedElementLabel) ? RootElementLabel : assignedElementLabel;
                if (w._previewTarget != assignedElement)
                {
                    w.StopPlayback(); // 前の対象で再生中なら止めて元に戻してから切り替える
                }

                w._previewTarget = assignedElement;
                w._selectedElementLabel = label;
                w._elementDropdown?.SetValueWithoutNotify(label);
                w.Say($"プレビュー対象: {label}(Canvas Editor などから自動割り当て)");
            }

            return w;
        }

        // ── 土台への指定 ──

        protected override string PrefsKey => "UiTween";
        protected override FieldGuide<UiTweenData> Guide => UiTweenFieldGuide.Instance;
        protected override string ManualPage => Page;
        protected override Texture DefaultIcon => EditorGUIUtility.IconContent("d_RectTransform Icon").image;
        protected override string SearchHint => "ラベル・説明・検索語(トラックの中身も)に一致する設定だけ出す(例: 透明度 / ループ / OutBack / 経路)";
        protected override bool SupportsPlayback => true;

        private static readonly KeyValuePair<string, string>[] MessageTokens =
        {
            new KeyValuePair<string, string>("Motion", nameof(UiTweenData.Tracks)),
            new KeyValuePair<string, string>("PathMove", nameof(UiTweenData.Tracks)),
            new KeyValuePair<string, string>("FillAmount", nameof(UiTweenData.Tracks)),
            new KeyValuePair<string, string>("Property=Color", nameof(UiTweenData.Tracks)),
            new KeyValuePair<string, string>("Track", nameof(UiTweenData.Tracks)),
        };

        protected override IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => MessageTokens;

        protected override string ManualAnchor(string purpose)
        {
            switch (purpose)
            {
                case UiTweenFieldGuide.PurStart: return "presets";
                case UiTweenFieldGuide.PurTracks: return "tracks";
                case UiTweenFieldGuide.PurTotal: return "total";
                case UiTweenFieldGuide.PurCheck: return "check";
                case FieldGuide<UiTweenData>.AdminPurpose: return "admin";
                default: return null;
            }
        }

        protected override string[] ModeTips => new[]
        {
            "雛形とトラックの一覧だけ。雛形を選んで ▶ で動かす",
            "よく使う欄まで。全体の長さ・動かす要素の選び方",
            "すべての欄(動作の取り決め・イベント・管理情報)",
        };

        protected override string CardDescription(string purpose)
        {
            switch (purpose)
            {
                case UiTweenFieldGuide.PurStart: return "出現・消滅・常時・強調の定番プリセットから選んで Tracks に入れる。登録済みのカタログも";
                case UiTweenFieldGuide.PurTracks: return "並列に動く項目を 1 行ずつ。バーのドラッグで開始と長さ、⚙ ですべての欄";
                case UiTweenFieldGuide.PurTotal: return "0 = 自動(いちばん遅く終わるトラックまで)";
                case UiTweenFieldGuide.PurCheck: return "シーン上の UI 要素や確認用シーンの仮画像を、実際に動かして確かめる";
                default: return "一覧に出る名前・分類・メモ";
            }
        }

        // 8 本を超えたトラックは再生されない(UiTweenManager.MaxTracksPerTween)。Validator(Runtime)は変えずに、エディター側で警告を足す。
        protected override void AppendValidation(List<ValidationResult> results)
        {
            // 共通の ValueDef の検査(From と To が同じ)は、Tween では当てはまらない: TweenTrack.Motion の From / To は使わず 0..1 の形だけを使う
            // (実際の値は FromValue / ToValue。TweenTrack.Motion の Tooltip 参照)。トラックごとに出て雑音になるので、このエディターでは外す。
            results.RemoveAll(r => r.Message != null && r.Message.StartsWith("Tracks[", StringComparison.Ordinal) && r.Message.Contains(".Motion: From と To が同じで値が変化しません"));
            if (Target != null && Target.Tracks != null && Target.Tracks.Length > UiTweenManager.MaxTracksPerTween)
            {
                results.Add(ValidationResult.Warning($"Tracks が {Target.Tracks.Length} 本あります。同時に再生されるのは {UiTweenManager.MaxTracksPerTween} 本までで、それ以降は無視されます")
                    .WithField(nameof(UiTweenData.Tracks)));
            }
        }

        // ── ヒーロー ──

        protected override void AddLeadingHeroActions(VisualElement actions)
        {
            actions.Add(MakeButton("ギャラリー", null, () => UiPresetGallery.Open(Target),
                "プリセットのカード一覧(曲線の形・お気に入り・シーン上での再生)を別ウィンドウで開く。いま編集中の Tween は「独自プリセットとして登録」の元に入る"));
            actions.Add(MakeButton("確認用シーン", null, () => OpenPreviewScene(), "UI 確認用シーン(CanvasPreviewScene)を開く(無ければ生成)"));
        }

        private static void OpenPreviewScene() => CanvasPreviewSceneSetup.TryOpenOrCreate();

        // ── ライフサイクル ──

        protected override void OnWindowEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            // 前回閉じ損ねた・ドメインリロードで参照を失った仮画像の残骸を消す。
            EditorPreviewRoots.DestroyAll(PreviewRootName);
        }

        protected override void OnWindowDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            // 閉じる / ドメインリロードの前に自分の仮画像を片付ける(収集・自動割り当てで指している「よその実要素」は所有していないので触らない)。
            RemoveFromScene();
        }

        protected override void OnBuildBodyStart()
        {
            _trackList = null;
            _picker = null;
            _collectField = null;
            _elementDropdown = null;
            _status = null;
            _sceneOwner = null;
        }

        // 経路の制御点ハンドルを SceneView に描く権利がどのウィンドウにあるか(最後にフォーカスしたウィンドウだけが描く。SceneGuiOwner)。
        protected override void OnTick() => RefreshSceneOwner();

        private void RefreshSceneOwner()
        {
            if (_sceneOwner == null)
            {
                return;
            }

            var text = SceneGuiOwner.DescribeFor(this);
            if (_sceneOwner.text != text)
            {
                _sceneOwner.text = text;
            }
        }

        protected override void OnTargetChanged() => _sceneTrack = -1;

        protected override void OnFiltersApplied(string query) => _trackList?.SetExternalQuery(query);

        // ── 欄 ──

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case UiTweenFieldGuide.RowPresets:
                {
                    _picker = new UiTweenPresetPicker(() => Target, () => _previewTarget, Say, OnPresetApplied, source => UiPresetGallery.Open(source));
                    _picker.SetSource(Target);
                    return new UiTweenAuxRow(e, _picker, true, null);
                }

                case nameof(UiTweenData.Tracks):
                {
                    _trackList = new UiTweenTrackList(this);
                    var row = new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, _trackList, RowChanged) { NoFold = true };
                    row.Q<Label>(className: "ux-badge")?.Show(false);
                    row.ExtraMatch = q => _trackList != null && _trackList.AnyMatch(q);
                    return row;
                }

                case UiTweenFieldGuide.RowCheck:
                    return new UiTweenAuxRow(e, CreateCheckBox(), false, null);

                case nameof(UiTweenData.Flags):
                case nameof(UiTweenData.Events):
                    GuidedField.Collapse(So, e.Field);
                    return base.CreateRow(e, card, isAdmin);

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        private void OnPresetApplied()
        {
            So?.Update();
            _trackList?.ResetOpen(-1);
            _trackList?.Rebuild();
            RowChanged();
            RestartIfPlaying();
        }

        // ── 確認する(対象の要素) ──

        private VisualElement CreateCheckBox()
        {
            var box = new VisualElement();
            box.AddToClassList("ux-ut-check");

            var note = new Label("実際に動かしたい要素(ボタンやパネル)を選びます。「収集元」にシーン上のオブジェクトを入れて「要素を自動収集」→「要素」で選ぶと、固定バーの ▶ とトラックの ▶ がその要素を動かします(Canvas Editor の ✎ から開くと自動で入ります)。適当な仮物でよければ「確認用シーンに配置」。");
            note.AddToClassList("ux-dim");
            note.AddToClassList("ux-note");
            box.Add(note);

            var collect = new VisualElement();
            collect.AddToClassList("ux-toolsline");
            _collectField = new ObjectField("収集元") { objectType = typeof(GameObject), allowSceneObjects = true };
            _collectField.AddToClassList("ux-ut-field");
            _collectField.AddToClassList("ux-ut-collect");
            _collectField.RegisterValueChangedCallback(evt => _collectRoot = evt.newValue as GameObject);
            collect.Add(_collectField);
            collect.Add(MakeButton("要素を自動収集", null, CollectElements, "収集元の子の RectTransform を一覧にして、「要素」から選べるようにする"));
            box.Add(collect);

            _elementDropdown = new DropdownField("要素", new List<string> { NoElementChoice }, 0);
            _elementDropdown.AddToClassList("ux-ut-field");
            _elementDropdown.SetValueWithoutNotify(_selectedElementLabel);
            _elementDropdown.RegisterValueChangedCallback(evt => SelectElement(evt.newValue));
            // 行の入力の直下に置くと土台の規則(.ux-row__input > .unity-base-field のラベルを隠す)に当たるので、包んで置く。
            var dropdownBox = new VisualElement();
            dropdownBox.Add(_elementDropdown);
            box.Add(dropdownBox);

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            var place = PreviewPlacementButton.Create("確認用シーンに配置", "UI 確認用シーン(CanvasPreviewScene)を開き、仮画像 1 枚を置いてプレビュー対象にする", PlaceInScene);
            place.AddToClassList("ux-btn");
            line.Add(place);
            line.Add(MakeButton("撤去", null, RemoveFromScene, "「確認用シーンに配置」で置いた仮画像を片付ける(収集した実要素は触りません)"));
            box.Add(line);

            _status = new Label();
            _status.AddToClassList("ux-dim");
            _status.AddToClassList("ux-note");
            box.Add(_status);

            _sceneOwner = new Label();
            _sceneOwner.AddToClassList("ux-dim");
            _sceneOwner.AddToClassList("ux-note");
            box.Add(_sceneOwner);
            RefreshSceneOwner();

            if (_collectRoot != null)
            {
                _collectField.SetValueWithoutNotify(_collectRoot);
                CollectElements();
            }

            return box;
        }

        internal string StatusText => _status != null ? _status.text : string.Empty;

        internal RectTransform PreviewTarget => _previewTarget;

        internal IReadOnlyList<(string label, RectTransform rt)> ElementChoices => _elementChoices;

        internal UiTweenTrackList TrackList => _trackList;

        internal UiTweenPresetPicker Picker => _picker;

        internal void SelectElementForTest(string label) => SelectElement(label);

        internal void CollectElementsForTest() => CollectElements();

        internal void SetCollectRootForTest(GameObject root)
        {
            _collectRoot = root;
            _collectField?.SetValueWithoutNotify(root);
        }

        public void Say(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        // 「収集元」の子 RectTransform を一覧化し、そこから実際にプレビューしたい要素を選べるようにする。
        private void CollectElements()
        {
            if (_elementDropdown == null)
            {
                return;
            }

            _elementChoices.Clear();
            var choices = new List<string> { NoElementChoice };
            if (_collectRoot != null)
            {
                // 同名の兄弟があると同じラベルになり、どれを選んでも最初の要素になるので、2 つ目以降に " #2" のような番号を付けてラベルを一意にする。
                var used = new HashSet<string>(StringComparer.Ordinal) { NoElementChoice };
                if (_collectRoot.transform is RectTransform rootRect)
                {
                    used.Add(RootElementLabel);
                    _elementChoices.Add((RootElementLabel, rootRect));
                    choices.Add(RootElementLabel);
                }

                foreach (var rect in _collectRoot.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect.gameObject == _collectRoot)
                    {
                        continue;
                    }

                    var path = TransformPath.GetRelative(_collectRoot.transform, rect.transform);
                    var label = path;
                    for (var n = 2; !used.Add(label); n++)
                    {
                        label = $"{path} #{n}";
                    }

                    _elementChoices.Add((label, rect));
                    choices.Add(label);
                }
            }

            _elementDropdown.choices = choices;
            var keep = choices.Contains(_selectedElementLabel) ? _selectedElementLabel : NoElementChoice;
            _elementDropdown.SetValueWithoutNotify(keep);
            ApplyElementSelection(keep);
            Say(_collectRoot == null ? "収集元(シーン上のオブジェクト)を指定してください" : $"要素を {_elementChoices.Count} 件収集しました");
        }

        private void SelectElement(string label)
        {
            ApplyElementSelection(label);
            if (label != NoElementChoice)
            {
                Say($"プレビュー対象: {label}");
            }
        }

        private void ApplyElementSelection(string label)
        {
            _selectedElementLabel = label;
            if (label == NoElementChoice)
            {
                return;
            }

            var match = _elementChoices.Find(c => c.label == label);
            if (match.rt != null)
            {
                if (match.rt != _previewTarget)
                {
                    StopPlayback(); // 前の対象で再生中なら止めて元に戻してから切り替える
                }

                _previewTarget = match.rt;
            }
        }

        // 左クリック = UI 確認用シーンを開いてから配置 / 右クリック = このシーンに配置・本配置(PreviewPlacementButton)。EditorPreviewRoots で組み立てる(子まで DontSave)。
        private void PlaceInScene(PreviewPlaceMode mode)
        {
            RemoveFromScene();
            if (!PreviewPlacement.PrepareScene(mode, CanvasPreviewSceneSetup.TryOpenOrCreate))
            {
                return;
            }

            var canvasGo = EditorPreviewRoots.CreateOverlayCanvas(PreviewRootName);
            var imageGo = EditorPreviewRoots.CreateChild(canvasGo.transform, "PreviewImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            ((RectTransform)imageGo.transform).sizeDelta = new Vector2(200f, 80f);
            EditorPreviewRoots.MarkDontSaveRecursive(canvasGo);

            // 仮画像は「要素を自動収集」の候補ではないので、選択表示を明示的に外しておく。
            _selectedElementLabel = NoElementChoice;
            _elementDropdown?.SetValueWithoutNotify(NoElementChoice);

            if (PreviewPlacement.IsPersistent(mode))
            {
                // Canvas ごと本配置する。所有しない(撤去・再生の対象にしない)。
                PreviewPlacement.Persist(canvasGo, Target != null ? Target.DisplayName ?? Target.name : null);
                _ownedPlaceholderRoot = null;
                _previewTarget = null;
                Say("このシーンに本配置しました(プレビュー対象にはしていません)");
                return;
            }

            _ownedPlaceholderRoot = canvasGo;
            _previewTarget = (RectTransform)imageGo.transform;
            Selection.activeGameObject = imageGo;
            PreviewPlacement.Focus(imageGo);
            Say("確認用シーンに配置しました");
        }

        private void RemoveFromScene()
        {
            StopPlayback();
            if (_ownedPlaceholderRoot != null)
            {
                DestroyImmediate(_ownedPlaceholderRoot);
                _ownedPlaceholderRoot = null;
            }

            // 自分で置いたプレースホルダを指していた場合は Destroy で自動的に(Unity の)null になる。
            if (_previewTarget == null)
            {
                _selectedElementLabel = NoElementChoice;
                _elementDropdown?.SetValueWithoutNotify(NoElementChoice);
            }
        }

        // ── 再生(実 UiTweenManager。固定バーの ▶ ■ リピート 速度) ──

        protected override bool IsPlaying => _active && _previewManager != null && _previewManager.IsPlaying(_previewHandle);

        protected override void StartPlayback(bool focus)
        {
            if (Target == null)
            {
                return;
            }

            if (!BeginPlay(out var target))
            {
                StopMain();
                return;
            }

            _previewHandle = _previewManager.PlayData(Target, target);
            AfterStart();
        }

        // ▶(トラック 1 本だけ。固定バーのリピートの対象にはしない)。
        public void PlayTrack(int index)
        {
            if (Target == null || Target.Tracks == null || index < 0 || index >= Target.Tracks.Length)
            {
                return;
            }

            StopMain();
            if (!BeginPlay(out var target))
            {
                return;
            }

            _previewHandle = _previewManager.PlayTracks(new[] { Target.Tracks[index] }, 1, target);
            AfterStart();
        }

        private bool BeginPlay(out RectTransform target)
        {
            target = _previewTarget;
            if (target == null)
            {
                Say("プレビュー対象がありません(「実際の要素で確かめる」で要素を選ぶか、「確認用シーンに配置」してください)");
                return false;
            }

            // 収集元の ObjectField は Prefab アセットも受け付けるため、アセットを直接書き換えないよう弾く。
            if (EditorUtility.IsPersistent(target.gameObject))
            {
                Say("Prefab アセットは再生対象にできません(シーン上のオブジェクトを指定してください)");
                return false;
            }

            // ▶ の連打で前の Handle を止めずに上書きするとループが止められなくなるので、前の再生を止めて元に戻してから始める。
            StopPlayback();
            _previewManager ??= new UiTweenManager(EditorAnchorRegistry.Build());
            _snapshot.Take(target);
            return true;
        }

        private void AfterStart()
        {
            _lastTime = EditorApplication.timeSinceStartup;
            _active = _previewManager.IsPlaying(_previewHandle);
            if (!_active)
            {
                _snapshot.Restore(); // Tracks 0 本などで即完了した
                Say("完了");
                return;
            }

            Say("再生中");
        }

        // 途中で止め(最終状態には進めない)、再生前の状態へ戻す。
        protected override void StopPlayback()
        {
            if (_previewManager != null)
            {
                _previewManager.Stop(_previewHandle);
            }

            _previewHandle = Handle<UiTweenMarker>.Invalid;
            _active = false;
            _snapshot.Restore();
        }

        private void OnEditorUpdate()
        {
            if (_previewManager == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - _lastTime);
            _lastTime = now;
            if (!_active)
            {
                return;
            }

            _previewManager.Tick(Mathf.Min(dt, 0.1f) * Speed);

            // 「再生中 → 終了」に変わった 1 回だけ、元の状態へ戻してステータスを書く。
            if (!_previewManager.IsPlaying(_previewHandle))
            {
                _active = false;
                _snapshot.Restore();
                Say("完了(再生前の状態に戻しました)");
            }
        }

        // ── IUiTweenTrackHost ──

        UiTweenData IUiTweenTrackHost.Data => Target;

        SerializedObject IUiTweenTrackHost.So => So;

        float IUiTweenTrackHost.AxisMax => UiTweenTrackInfo.AxisMax(UiTweenTrackInfo.TimelineEnd(Target));

        public void EditTrack(int index, TrackEdit edit, string undoName, bool final)
        {
            var tracks = Target != null ? Target.Tracks : null;
            if (tracks == null || index < 0 || index >= tracks.Length)
            {
                return;
            }

            Undo.RecordObject(Target, undoName);
            var t = tracks[index];
            edit(ref t);
            tracks[index] = t;
            EditorUtility.SetDirty(Target);
            VisualEdited(final, false);
            _trackList?.Sync();
            if (final)
            {
                SceneView.RepaintAll();
            }
        }

        public void EditTracks(Func<TweenTrack[], TweenTrack[]> change, string undoName, int selectIndex)
        {
            if (Target == null)
            {
                return;
            }

            Undo.RecordObject(Target, undoName);
            var source = Target.Tracks != null ? (TweenTrack[])Target.Tracks.Clone() : Array.Empty<TweenTrack>();
            Target.Tracks = change(source);
            EditorUtility.SetDirty(Target);
            So?.Update();
            _trackList?.ResetOpen(selectIndex);
            RowChanged();
            _trackList?.Rebuild();
            SceneView.RepaintAll();
        }

        public void OpenedTrack(int index, bool open)
        {
            if (open)
            {
                _sceneTrack = index;
            }
            else if (_sceneTrack == index)
            {
                _sceneTrack = -1;
            }

            SceneView.RepaintAll();
        }

        // ── SceneView: 経路(PathMove)の制御点ハンドル ──
        // 対象はプレビュー対象のローカル座標系(world = _previewTarget.TransformPoint(local))。複数の D-Drive エディターが同時に開ける前提のため、
        // SceneGuiOwner が最後にフォーカスしたウィンドウだけに描画権を渡す(土台が調停)。

        protected override bool WantsSceneGui => true;

        protected override void DrawSceneGui()
        {
            if (_previewTarget == null || !SceneGuiOwner.IsOwner(this) || Target == null || Target.Tracks == null)
            {
                return;
            }

            if (_sceneTrack < 0 || _sceneTrack >= Target.Tracks.Length)
            {
                return;
            }

            var track = Target.Tracks[_sceneTrack];
            if (track.Property != TweenProperty.PathMove || track.Path.Points == null || track.Path.Points.Length == 0)
            {
                return;
            }

            var points = track.Path.Points;
            var xform = _previewTarget;
            var changed = false;
            for (var i = 0; i < points.Length; i++)
            {
                var world = SplineHandleMath.LocalToWorld(xform, points[i]);
                EditorGUI.BeginChangeCheck();
                var moved = Handles.PositionHandle(world, xform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!changed)
                    {
                        Undo.RecordObject(Target, "UiTweenData: スプライン点を移動");
                        changed = true;
                    }

                    points[i] = SplineHandleMath.WorldToLocal(xform, moved);
                }

                Handles.Label(world, $"P{i}");
            }

            if (points.Length >= 2)
            {
                var spline = new SplinePath(points, track.Path.Type);
                var poly = new Vector3[SplinePreviewSamples + 1];
                for (var i = 0; i <= SplinePreviewSamples; i++)
                {
                    poly[i] = SplineHandleMath.LocalToWorld(xform, spline.Evaluate(i / (float)SplinePreviewSamples));
                }

                var prev = Handles.color;
                Handles.color = new Color(0.3f, 0.8f, 1f);
                Handles.DrawAAPolyLine(3f, poly);
                Handles.color = prev;
            }

            if (changed)
            {
                Target.Tracks[_sceneTrack] = track;
                EditorUtility.SetDirty(Target);
                VisualEdited(false, false);
            }
        }
    }

    // Data の欄を持たない補助の行(雛形・確認する)。検索・表示モード・目的別カードの対象にはなるが、変更の件数・検証の結びつけには入らない。
    internal sealed class UiTweenAuxRow : UxRow
    {
        private readonly Action _sync;

        public UiTweenAuxRow(FieldGuideEntry entry, VisualElement input, bool hideBadge, Action sync)
            : base(null, null, new[] { entry }, entry.Label, entry.Hint, entry.Tier, true, input, null)
        {
            _sync = sync;
            NoFold = true;
            if (hideBadge)
            {
                this.Q<Label>(className: "ux-badge")?.Show(false);
            }
        }

        public override void Sync() => _sync?.Invoke();
    }
}
