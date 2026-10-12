using System;
using System.Collections.Generic;
using DDrive.Editor.Preview;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // 新デザインの専用エディターの土台(docs/1010 §2。試験版 E の PrototypeWindowBase + PrototypeEWindow を種別非依存にしたもの)。
    // 新しい専用エディターは、この UxEditorWindow<TData> を継承して「欄の表(FieldGuide<TData>)・行の作り方・再生のしかた」だけを書く。
    //
    // 土台が持つもの: 対象の追従 / 🔒 固定 / Data 切替 / ヒーロー(アイコン・表示名・ID・チップ・主操作列)/ セグメント(かんたん・標準・詳細)/
    // 検索 / 目的別カード / 未調整の折りたたみ / 固定バー(▶ ■ リピート 速度 変更 N 件 自動保存 保存)/ 保存(SaveDirty、自動保存、未保存の確認)/
    // 空状態 / 再コンパイル耐性([SerializeField])/ Undo 同期 / インライン検証(ValidationResult.Field で欄に結びつける)/ Light・Dark。
    // 見た目はすべて Theme/DDriveUx.uss(C# にインラインスタイルを書かない)。
    //
    // ファイル構成: 本ファイル = 対象・保存・再生・定期更新 / .Chrome.cs = ヒーロー・固定バー・空状態・カード / .Rows.cs = 行・検証・絞り込み。
    internal abstract partial class UxEditorWindow<TData> : EditorWindow where TData : AssetDataBase
    {
        private const float RepeatGapSec = 0.35f;
        private const float SpeedMin = 0.1f;
        private const float SpeedMax = 2f;
        private const float SpeedDetent = 0.07f; // ×1.0 の吸着幅(±)

        [SerializeField] private TData _target;
        [SerializeField] private bool _lock;
        [SerializeField] private bool _repeat;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private string _query = string.Empty;
        [SerializeField] private bool _modifiedOnly;

        private int _mode = 1;
        private bool _wantPlaying;
        private double _repeatWaitStart = -1;
        private TData _defaultInstance;
        private ScrollView _body;
        private VisualElement _root;
        private VisualElement _footer;
        private bool _lastPro;
        private int _autoUndoGroup = -1;
        private bool _autoWasDirty;
        private double _autoEditTime;
        private int _tick;
        private bool _wasPlaying;

        protected SerializedObject So { get; private set; }
        protected SerializedObject DefaultSo { get; private set; }
        protected TData Target => _target;

        // ── 種別が決めること ──

        // EditorPrefs のキーに入れる種別名(例: "Vfx")。"DDrive.Ux.<PrefsKey>.AutoSave" のように使う。
        protected abstract string PrefsKey { get; }

        protected abstract FieldGuide<TData> Guide { get; }

        // カードの「?」で開くマニュアルのページ名。節に飛ばすなら ManualAnchor を上書きする。
        protected abstract string ManualPage { get; }

        protected virtual string ManualAnchor(string purpose) => null;

        // 目的カードの一言説明。
        protected abstract string CardDescription(string purpose);

        // 1 欄の行を作る。null = 他の行に含める(欄を作らない)。既定は通常の欄(PropertyField)。
        // 視覚部品・つまみ・特別な欄は種別が override して作り、default 分岐では base.CreateRow を呼ぶ。
        protected virtual UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin) => CreateDefaultRow(e);

        // 指定の目的カードの直前に差し込む部品(例: Vfx の「複数同時再生」を管理情報の前に出す)。
        protected virtual void BeforeCard(string purpose, VisualElement page) { }

        // ヒーローの主操作列の先頭に足すボタン(例: 確認用シーン、Prefab で開く)。後ろに Project / ＋ 新規 / 🔒 固定が付く。
        protected virtual void AddLeadingHeroActions(VisualElement actions) { }

        // ヒーローの直下に出す案内(例: カメラ / ライトが無い警告)。
        protected virtual void AfterHero(VisualElement page) { }

        // 検証メッセージを欄に結びつける補助語(ValidationResult.Field が無い結果の救済。メッセージ中の語 → 欄名)。
        protected virtual IReadOnlyList<KeyValuePair<string, string>> ValidationTokens => null;

        protected virtual string[] ModeTips => DefaultModeTips;

        private static readonly string[] DefaultModeTips =
        {
            "必須の欄だけ",
            "よく使う欄まで",
            "すべての欄",
        };

        protected virtual string SearchHint => "ラベル・説明・検索語に一致する設定だけ出す";

        // 空状態の「サンプルを作る」。null を返す種別は何も出さない(HasSample が false ならボタン自体を作らない)。
        protected virtual bool HasSample => false;

        protected virtual string SampleLabel => "サンプルを作る";

        protected virtual TData CreateSample() => null;

        protected virtual Texture DefaultIcon => EditorGUIUtility.IconContent("ScriptableObject Icon").image;

        // 検索語が一致するときのヒント文に出す、Data の種類名(例: "VfxData")。
        protected string DataKindName => typeof(TData).Name;

        // 検索ボックスの現在の語(空 = 検索していない)と表示モード(0 かんたん / 1 標準 / 2 詳細)。種別が持つ一覧の絞り込みに使う。
        protected string SearchQuery => (_query ?? string.Empty).Trim();

        protected int ViewMode => _mode;

        // 本体(ScrollView)の中の要素までスクロールする。
        protected void ScrollToElement(VisualElement element)
        {
            if (element != null)
            {
                _body?.ScrollTo(element);
            }
        }

        // 再生(▶ ■ リピート 速度)を持つ種別だけ true。持たない種別(Tuning・Material など)は固定バーに再生を出さない。
        protected virtual bool SupportsPlayback => false;

        // リピート(終わったら自動で再生し直す)を固定バーに出すか。再生が「開きっぱなし」の種別(例: Canvas)は false。
        protected virtual bool SupportsRepeat => true;

        protected virtual bool IsPlaying => false;

        // 速度スライダーを固定バーに出すか。再生はあるが速度を変えられない種別(例: BGM)は false。
        protected virtual bool SupportsSpeed => true;

        protected virtual void StartPlayback(bool focus) { }

        protected virtual void StopPlayback() { }

        // 再生中に編集して撮り直すとき。既定は止めてから始め直す(状態は WantPlaying のまま)。
        protected virtual void RestartPlayback()
        {
            StopPlayback();
            StartPlayback(false);
        }

        protected virtual void OnSpeedChanged(float speed) { }

        // 画面(本体)を作り直す直前。種別が持つ UI 部品への参照を捨てるのに使う。
        protected virtual void OnBuildBodyStart() { }

        protected virtual void OnWindowEnable() { }

        protected virtual void OnWindowDisable() { }

        protected virtual void OnTargetChanged() { }

        // 窓の定期更新(150ms)ごと。
        protected virtual void OnTick() { }

        // 約 1 秒ごと(7 tick)。重い確認(シーンの状態など)に使う。
        protected virtual void OnSlowTick() { }

        // データが書き換わった後の副作用(例: Vfx の Anchor を再生中の実体へ反映し直す)。
        protected virtual void OnSideEffect() { }

        // 欄の編集が SerializedProperty 変更として届いたとき(path はプロパティパス)。再生中の撮り直しなどに使う。
        protected virtual void OnPropertyEdited(string path) { }

        // 再生の環境(シーン・プレハブモード)が変わったとき、再生の状態を捨てる。
        protected virtual void OnPlaybackReset() { }

        // SceneView に描くものがある種別が true にする。DrawSceneGui は SceneView.duringSceneGui のたび(対象と SerializedObject があるとき)に呼ぶ。
        protected virtual bool WantsSceneGui => false;

        protected virtual void DrawSceneGui() { }

        // ── 公開してよい状態 ──

        protected bool Locked
        {
            get => _lock;
            set => _lock = value;
        }

        protected bool Repeat
        {
            get => _repeat;
            set
            {
                _repeat = value;
                _repeatWaitStart = -1;
            }
        }

        protected float Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                OnSpeedChanged(value);
            }
        }

        // 再生ボタンを押してから停止を押すまで true(リピートの判定に使う)。
        protected bool WantPlaying => _wantPlaying;

        // 自動保存(固定バーのトグル。EditorPrefs、種別ごと)。
        protected bool AutoSave
        {
            get => EditorPrefs.GetBool(PrefKey("AutoSave"), false);
            set => EditorPrefs.SetBool(PrefKey("AutoSave"), value);
        }

        protected string PrefKey(string name) => "DDrive.Ux." + PrefsKey + "." + name;

        protected static T OpenWindow<T>(string title) where T : UxEditorWindow<TData>
        {
            var w = GetWindow<T>(title);
            w.minSize = new Vector2(500, 380); // [09] §7.1: 横幅 500px を minSize で回避しない
            if (w._target == null && Selection.activeObject is TData sel)
            {
                w.SetTarget(sel);
            }

            return w;
        }

        // ── ライフサイクル ──

        private void OnEnable()
        {
            _defaultInstance = CreateInstance<TData>();
            _defaultInstance.hideFlags = HideFlags.HideAndDontSave;
            DefaultSo = new SerializedObject(_defaultInstance);
            _mode = Mathf.Clamp(EditorPrefs.GetInt(PrefKey("Mode"), 1), 0, 2);
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGuiInternal;
            OnWindowEnable();
        }

        // SceneView の描画権: 最後にフォーカスしたウィンドウだけがハンドルを描く(SceneGuiOwner)。
        private void OnFocus() => SceneGuiOwner.Claim(this);

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGuiInternal;
            SceneGuiOwner.Release(this);
            _wantPlaying = false;
            OnWindowDisable();
            DefaultSo = null;
            if (_defaultInstance != null)
            {
                DestroyImmediate(_defaultInstance);
            }
        }

        // ウィンドウを閉じるとき: 未保存なら聞く(自動保存 ON なら聞かずに保存)。ドメインリロードでは呼ばれない。
        private void OnDestroy() => ConfirmSaveBeforeLeave(_target, "ウィンドウを閉じます。");

        private void OnSceneGuiInternal(SceneView view)
        {
            if (WantsSceneGui && _target != null && So != null)
            {
                DrawSceneGui();
            }
        }

        private void OnSelectionChange()
        {
            if (!_lock && Selection.activeObject is TData sel && sel != _target)
            {
                SetTarget(sel);
            }
        }

        private void OnUndoRedo()
        {
            _autoEditTime = EditorApplication.timeSinceStartup;
            EnsureSo();
            OnSideEffect();
            if (_target == null || _rows.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].UpdateState();
            }

            RunValidation();
            Reclassify();
            RefreshAll(true);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            _root = root;
            _body = new ScrollView(ScrollViewMode.Vertical);
            root.Add(_body);
            _footer = CreateFooter();
            root.Add(_footer);
            _body.RegisterCallback<SerializedPropertyChangeEvent>(OnPropertyChanged);
            root.schedule.Execute(Tick).Every(150);
            if (_target == null && Selection.activeObject is TData sel)
            {
                _target = sel;
            }

            EnsureSo();
            ApplyThemeSheet(root);
            RebuildBody();
        }

        // ── 対象 ──

        // confirmSave = false: 未保存の確認を出さずに切り替える(Data を手放さない切替。例: Canvas の「親の中の子 Canvas へ」)。
        protected void SetTarget(TData data, bool confirmSave = true)
        {
            if (confirmSave && _target != null && data != _target)
            {
                ConfirmSaveBeforeLeave(_target, "別の Data に切り替えます。");
            }

            StopMain();
            _target = data;
            EnsureSo();
            DataSwitcher.Record(typeof(TData), Target);
            _wasPlaying = false;
            _errors = _warnings = 0;
            OnTargetChanged();
            RebuildBody();
        }

        private void EnsureSo()
        {
            if (_target == null)
            {
                So = null;
                return;
            }

            if (So == null || So.targetObject == null || So.targetObject != _target)
            {
                So = new SerializedObject(_target);
            }
            else
            {
                So.Update();
            }
        }

        // ── 保存 ──
        // デザイナーの編集の保存は SaveDirty(対象 1 個だけ。版数・UpdatedAt が進む本来の経路。docs/1010 D-5)。

        protected void SaveTarget() => SaveAsset(_target);

        private static void SaveAsset(TData data)
        {
            if (data == null)
            {
                return;
            }

            EditorUtility.SetDirty(data);
            DDriveAssetSave.SaveDirty(data);
        }

        // 別の Data へ移る前・閉じる前: 未保存なら「保存する / 保存しない」を聞く(自動保存 ON なら聞かず保存)。
        protected void ConfirmSaveBeforeLeave(TData data, string reason)
        {
            // アセットとして保存されていない Data(メモリ上のもの)には聞かない。
            if (data == null || !AssetDatabase.Contains(data) || !EditorUtility.IsDirty(data))
            {
                return;
            }

            if (AutoSave || EditorUtility.DisplayDialog("未保存の変更があります", $"{reason}\n'{data.name}' に未保存の変更があります。保存しますか?", "保存する", "保存しない"))
            {
                SaveAsset(data);
            }
        }

        // 自動保存: 編集(Undo グループの増加 / dirty になった瞬間)から 1 秒何も起きなければ SaveDirty。
        private void AutoSaveTick()
        {
            if (_target == null)
            {
                return;
            }

            var dirty = EditorUtility.IsDirty(_target);
            var group = Undo.GetCurrentGroup();
            var now = EditorApplication.timeSinceStartup;
            if (group != _autoUndoGroup || (dirty && !_autoWasDirty))
            {
                _autoEditTime = now;
            }

            _autoUndoGroup = group;
            _autoWasDirty = dirty;
            if (dirty && AutoSave && now - _autoEditTime >= 1.0 && !EditorApplication.isCompiling)
            {
                SaveAsset(_target);
                _autoWasDirty = false;
            }
        }

        // ── 再生 ──

        protected void PlayMain(bool focus)
        {
            if (_target == null)
            {
                return;
            }

            StopPlayback();
            _wantPlaying = true;
            _repeatWaitStart = -1;
            StartPlayback(focus);
        }

        protected void StopMain()
        {
            _wantPlaying = false;
            _repeatWaitStart = -1;
            StopPlayback();
        }

        protected void RestartIfPlaying()
        {
            if (_wantPlaying && IsPlaying)
            {
                RestartPlayback();
            }
        }

        // 再生の環境が変わった(シーン切替・プレハブモードの開閉)。
        protected void ResetPlayback()
        {
            _wantPlaying = false;
            OnPlaybackReset();
        }

        // ── 定期更新(外部変更・再生状態・テーマ) ──

        private void Tick()
        {
            if (EditorGUIUtility.isProSkin != _lastPro)
            {
                // Light / Dark が切り替わった: クラスを付け替え、Painter の色を読み直すため作り直す。
                ApplyTheme();
                RebuildBody();
                return;
            }

            TickRepeat();
            AutoSaveTick();
            OnTick();

            if (_target == null || So == null)
            {
                return;
            }

            SyncFooter();
            So.Update();
            var sig = 17;
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].UpdateState();
                _rows[i].Sync();
                sig = sig * 31 + (_rows[i].IsModified ? 1 : 0) + (_rows[i].HasIssues ? 2 : 0);
            }

            if (sig != _signature)
            {
                _signature = sig;
                UpdateChips();
                if (_modifiedOnly)
                {
                    ApplyFilters();
                }
            }

            _tick++;
            if (_tick % 7 == 0)
            {
                RunValidation();
                OnSlowTick();
            }
        }

        private void TickRepeat()
        {
            if (!SupportsPlayback || _target == null)
            {
                return;
            }

            var playing = IsPlaying;
            if (_wantPlaying && _repeat && !playing)
            {
                var now = EditorApplication.timeSinceStartup;
                if (_repeatWaitStart < 0)
                {
                    _repeatWaitStart = now;
                }
                else if (now - _repeatWaitStart >= RepeatGapSec)
                {
                    _repeatWaitStart = -1;
                    PlayMain(false);
                }
            }
        }
    }
}
