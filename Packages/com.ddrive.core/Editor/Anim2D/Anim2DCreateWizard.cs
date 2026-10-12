using System.Collections.Generic;
using DDrive.Editor.Anim;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Ux;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    // 「スプライトから作成」ウィザード(docs/1010 UX-7、1008 §3 B の雛形方式)。ページ送り: 1 雛形 → 2 素材 → 3 名前と動き → 4 確認と登録。
    // 旧 Anim2DCreateWindow の全入力(入力モード・Grid / Automatic / 既存・検出プレビュー・Sprite Editor で手動補正・方向セット・命名・FrameRate / Loop / 長さ・Animator)を
    // 雛形ごとに必要な物だけ出す形に並べ替えた。生成は Anim2DCreateService(旧の生成ロジックそのまま)。作ったら Anim2DEditor で開く。
    internal sealed class Anim2DCreateWizard : EditorWindow
    {
        internal enum Template
        {
            Grid,       // 均等に並んだ 1 枚のシート
            Automatic,  // バラバラに並んだ 1 枚のシート(自動で検出)
            Existing,   // Importer で切ってある画像
            Four,       // 4 方向(方向ごとの画像)
            Eight,      // 8 方向
        }

        private static readonly string[] StepNames = { "1 雛形", "2 素材", "3 名前と動き", "4 確認と登録" };

        [SerializeField] private int _page;
        [SerializeField] private Template _template = Template.Grid;
        [SerializeField] private Anim2DSliceMode _dirSlice = Anim2DSliceMode.Grid;
        [SerializeField] private Anim2DImportProfile _profile;
        [SerializeField] private Texture2D _texture;
        [SerializeField] private int _gridColumns = 4;
        [SerializeField] private int _gridRows = 1;
        [SerializeField] private int _gridSpriteCount = 4;
        [SerializeField] private int _autoMinSize = 16;
        [SerializeField] private int _autoExtrude;
        [SerializeField] private Texture2D[] _dirTextures = new Texture2D[8];
        [SerializeField] private string _name = string.Empty;
        [SerializeField] private string _state = string.Empty;
        [SerializeField] private DirectionMode _directionMode = DirectionMode.None;
        [SerializeField] private int _frameRate = 12;
        [SerializeField] private bool _loop = true;
        [SerializeField] private ClipLengthMode _lengthMode = ClipLengthMode.Frames;
        [SerializeField] private float _length = 8f;
        [SerializeField] private AnimatorController _controller;
        [SerializeField] private int _layer;

        private ScrollView _body;
        private VisualElement _nav;
        private Button _back;
        private Button _next;
        private VisualElement _issueHost;
        private Label _resultLabel;
        private string _resultMessage = string.Empty;

        public static void Open()
        {
            var w = GetWindow<Anim2DCreateWizard>("Anim2D を作る");
            w.minSize = new Vector2(500, 560);
            w.Show();
        }

        // テスト・他から使う(ページを指定して開く)。
        internal static Anim2DCreateWizard OpenAt(int page)
        {
            var w = GetWindow<Anim2DCreateWizard>("Anim2D を作る");
            w.minSize = new Vector2(500, 560);
            w._page = Mathf.Clamp(page, 0, StepNames.Length - 1);
            w.Rebuild();
            return w;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            UxTheme.Apply(root);
            root.AddToClassList("ux-wz");
            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("ux-scroll");
            root.Add(_body);
            _nav = new VisualElement();
            _nav.AddToClassList("ux-wz__nav");
            _back = MakeButton("戻る", null, () => Go(_page - 1), "前のページへ");
            _next = MakeButton("次へ", "ux-btn--primary", OnNext, "次のページへ");
            var space = new VisualElement();
            space.AddToClassList("ux-wz__navspace");
            _nav.Add(space);
            _nav.Add(_back);
            _nav.Add(_next);
            root.Add(_nav);
            root.schedule.Execute(() =>
            {
                if (UxTheme.IsStale(root))
                {
                    UxTheme.Apply(root);
                    Rebuild();
                }
            }).Every(500);
            Rebuild();
        }

        // ── 状態 → 依頼 ──

        private bool IsDirection => _template == Template.Four || _template == Template.Eight;

        private Anim2DSliceMode CurrentSlice
        {
            get
            {
                switch (_template)
                {
                    case Template.Grid: return Anim2DSliceMode.Grid;
                    case Template.Automatic: return Anim2DSliceMode.Automatic;
                    case Template.Existing: return Anim2DSliceMode.Existing;
                    default: return _dirSlice;
                }
            }
        }

        private Anim2DCreateRequest BuildRequest()
        {
            var dirs = _template == Template.Four ? DirectionSet.Four : _template == Template.Eight ? DirectionSet.Eight : DirectionSet.None;
            var count = dirs == DirectionSet.Four ? 4 : dirs == DirectionSet.Eight ? 8 : 0;
            var textures = new Texture2D[count];
            for (var i = 0; i < count && i < _dirTextures.Length; i++)
            {
                textures[i] = _dirTextures[i];
            }

            return new Anim2DCreateRequest
            {
                Profile = _profile ?? Anim2DImportProfile.FindOrDefault(),
                Slice = CurrentSlice,
                Texture = _texture,
                GridColumns = _gridColumns,
                GridRows = _gridRows,
                GridSpriteCount = _gridSpriteCount,
                AutoMinSize = _autoMinSize,
                AutoExtrude = _autoExtrude,
                Directions = dirs,
                DirectionTextures = textures,
                Name = _name,
                State = _state,
                DirectionMode = IsDirection ? DirectionMode.None : _directionMode,
                FrameRate = _frameRate,
                Loop = _loop,
                LengthMode = _lengthMode,
                Length = _length,
                Controller = _controller,
                Layer = _layer,
                ConfirmOverwrite = Anim2DCreateService.AskOverwrite,
            };
        }

        // ── ページ送り ──

        private void Go(int page)
        {
            _page = Mathf.Clamp(page, 0, StepNames.Length - 1);
            Rebuild();
        }

        private void OnNext()
        {
            if (_page < StepNames.Length - 1)
            {
                Go(_page + 1);
                return;
            }

            Create();
        }

        private string PageProblem()
        {
            var r = BuildRequest();
            switch (_page)
            {
                case 1: return Anim2DCreateService.CheckInputs(r);
                case 2: return Anim2DCreateService.CheckNaming(r);
                case 3: return Anim2DCreateService.CheckInputs(r) ?? Anim2DCreateService.CheckNaming(r);
                default: return null;
            }
        }

        private void UpdateNav()
        {
            if (_next == null)
            {
                return;
            }

            var problem = PageProblem();
            _next.text = _page == StepNames.Length - 1 ? "作成" : "次へ";
            _next.SetEnabled(problem == null);
            _back.SetEnabled(_page > 0);
            if (_issueHost != null)
            {
                _issueHost.Clear();
                if (problem != null && _page > 0)
                {
                    _issueHost.Add(UxRow.MakeIssue(ValidationResult.Warning(problem), null));
                }
            }
        }

        private void Rebuild()
        {
            if (_body == null)
            {
                return;
            }

            _body.Clear();
            var page = new VisualElement();
            page.AddToClassList("ux-page");
            _body.Add(page);

            var steps = new VisualElement();
            steps.AddToClassList("ux-wz__steps");
            for (var i = 0; i < StepNames.Length; i++)
            {
                var s = new Label(StepNames[i]);
                s.AddToClassList("ux-wz__step");
                s.EnableInClassList("ux-wz__step--on", i == _page);
                s.EnableInClassList("ux-wz__step--done", i < _page);
                steps.Add(s);
            }

            page.Add(steps);
            _issueHost = new VisualElement();
            _issueHost.AddToClassList("ux-issues");
            _issueHost.AddToClassList("ux-wz__issues");
            page.Add(_issueHost);

            switch (_page)
            {
                case 0: BuildTemplatePage(page); break;
                case 1: BuildSourcePage(page); break;
                case 2: BuildNamingPage(page); break;
                default: BuildConfirmPage(page); break;
            }

            UpdateNav();
        }

        // ── 部品 ──

        private static Button MakeButton(string text, string cls, System.Action click, string tooltip)
        {
            var b = new Button(click) { text = text, tooltip = tooltip };
            b.AddToClassList("ux-btn");
            if (cls != null)
            {
                b.AddToClassList(cls);
            }

            return b;
        }

        private static UxCard NewCard(VisualElement page, string title, string desc)
        {
            var card = new UxCard(title, desc, UxCard.Help("anim2d-editor", "create"));
            page.Add(card);
            return card;
        }

        private static void Row(UxCard card, string label, string hint, FieldTier tier, VisualElement input)
        {
            var entry = new FieldGuideEntry { Field = label, Label = label, Hint = hint, Tier = tier };
            card.AddRow(new AnimAuxRow(entry, label, hint, tier, input, null));
        }

        private static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("ux-dim");
            l.AddToClassList("ux-note");
            return l;
        }

        private IntegerField IntField(int value, int min, System.Action<int> set)
        {
            var f = new IntegerField { value = value };
            f.RegisterValueChangedCallback(evt =>
            {
                set(Mathf.Max(min, evt.newValue));
                UpdateNav();
            });
            return f;
        }

        // ── ページ 1: 雛形 ──

        private static readonly (Template template, string title, string desc)[] Templates =
        {
            (Template.Grid, "均等に並んだシート", "コマが同じ大きさで、縦横にきれいに並んだ 1 枚の画像。列・行・枚数を指定して切ります。"),
            (Template.Automatic, "バラバラに並んだシート", "コマの大きさや位置が不規則な 1 枚の画像。透明部分で区切って自動で検出します(手動で補正もできます)。"),
            (Template.Existing, "切ってある画像", "Sprite Editor などで既にスプライトに切ってある画像。そのスプライトをそのまま使います。"),
            (Template.Four, "4 方向", "上下左右など 4 方向ぶんの画像(0 / 90 / 180 / 270 度)。方向ごとに Clip を作り、BlendTree に入れる前提の Anim2DData を作ります。"),
            (Template.Eight, "8 方向", "45 度刻みの 8 方向ぶんの画像。方向ごとの Clip と、方向付きの Anim2DData を作ります。"),
        };

        private void BuildTemplatePage(VisualElement page)
        {
            var card = NewCard(page, "雛形を選ぶ", "素材の形に近い物を選びます。細かい設定はあとのページです");
            var tiles = new VisualElement();
            tiles.AddToClassList("ux-wz__tiles");
            foreach (var t in Templates)
            {
                var template = t.template;
                var tile = new Button(() =>
                {
                    _template = template;
                    Rebuild();
                }) { tooltip = t.desc };
                tile.AddToClassList("ux-wz__tile");
                tile.EnableInClassList("ux-wz__tile--on", template == _template);
                var title = new Label(t.title) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("ux-wz__tile-title");
                tile.Add(title);
                var desc = new Label(t.desc) { pickingMode = PickingMode.Ignore };
                desc.AddToClassList("ux-wz__tile-desc");
                tile.Add(desc);
                tiles.Add(tile);
            }

            card.Body.Add(tiles);

            var profileCard = NewCard(page, "命名のプロファイル(任意)", "名前の候補・保存先フォルダ・既定のフレームレートを決める設定。無ければ組み込みの既定を使います");
            var field = new ObjectField { objectType = typeof(Anim2DImportProfile), value = _profile, allowSceneObjects = false };
            field.RegisterValueChangedCallback(evt => _profile = evt.newValue as Anim2DImportProfile);
            Row(profileCard, "Import Profile", "プロジェクトの Anim2DImportProfile。空なら見つかった物 / 組み込みの既定。", FieldTier.Advanced, field);
        }

        // ── ページ 2: 素材 ──

        private Rect[] _detectedRects;
        private Texture2D _detectedTexture;
        private Vector2Int _detectedSourceSize;
        private IMGUIContainer _detectPreview;
        private Label _detectLabel;
        private Button _spriteEditorButton;
        private const float DetectPreviewMaxHeight = 260f;

        private void BuildSourcePage(VisualElement page)
        {
            _detectPreview = null;
            if (!IsDirection)
            {
                var card = NewCard(page, "素材の画像", "切る前の画像(Texture)を選びます");
                var tex = new ObjectField { objectType = typeof(Texture2D), value = _texture, allowSceneObjects = false };
                tex.RegisterValueChangedCallback(evt =>
                {
                    _texture = evt.newValue as Texture2D;
                    _detectedRects = null;
                    _detectedTexture = null;
                    UpdateNav();
                });
                Row(card, "Texture", "スプライトシート(または切ってある画像)。", FieldTier.Required, tex);
            }
            else
            {
                var way = NewCard(page, "画像の切り方", "方向ごとの画像すべてに同じ切り方を使います");
                var mode = new EnumField(_dirSlice);
                mode.RegisterValueChangedCallback(evt =>
                {
                    _dirSlice = (Anim2DSliceMode)evt.newValue;
                    Rebuild();
                });
                Row(way, "切り方", "Grid = 均等に切る / Automatic = 自動で検出 / Existing = 切ってあるスプライトを使う。", FieldTier.Common, mode);
                AddSliceParams(page, _dirSlice);

                var angles = _template == Template.Four ? DirectionAngle.FourAngles : DirectionAngle.EightAngles;
                var dirs = NewCard(page, "方向ごとの画像", $"{angles.Length} 方向。空欄の方向は作られません(Validation で知らせます)");
                for (var i = 0; i < angles.Length; i++)
                {
                    var index = i;
                    var f = new ObjectField { objectType = typeof(Texture2D), value = _dirTextures[i], allowSceneObjects = false };
                    f.RegisterValueChangedCallback(evt =>
                    {
                        _dirTextures[index] = evt.newValue as Texture2D;
                        UpdateNav();
                    });
                    Row(dirs, angles[i] + "°", "この方向の画像。画像名の末尾が _" + angles[i] + " のものを選ぶと分かりやすい。", FieldTier.Common, f);
                }

                return;
            }

            AddSliceParams(page, CurrentSlice);
        }

        private void AddSliceParams(VisualElement page, Anim2DSliceMode mode)
        {
            switch (mode)
            {
                case Anim2DSliceMode.Grid:
                {
                    var card = NewCard(page, "グリッドで切る", "左上から行優先で、指定の枚数だけ使います");
                    Row(card, "Columns", "横の分割数。", FieldTier.Required, IntField(_gridColumns, 1, v => _gridColumns = v));
                    Row(card, "Rows", "縦の分割数。", FieldTier.Required, IntField(_gridRows, 1, v => _gridRows = v));
                    Row(card, "Sprite Count", "使う枚数(Columns × Rows 以下)。", FieldTier.Required, IntField(_gridSpriteCount, 1, v => _gridSpriteCount = v));
                    break;
                }

                case Anim2DSliceMode.Automatic:
                {
                    var card = NewCard(page, "自動で検出", "透明部分で区切ってコマを見つけます");
                    Row(card, "Min Sprite Size", "これより小さい塊はノイズとして無視(px)。", FieldTier.Common, IntField(_autoMinSize, 1, v => _autoMinSize = v));
                    Row(card, "Extrude", "矩形を外へ広げる量(px)。", FieldTier.Advanced, IntField(_autoExtrude, 0, v => _autoExtrude = v));
                    var line = new VisualElement();
                    line.AddToClassList("ux-toolsline");
                    line.Add(MakeButton("検出プレビュー", null, RunDetectPreview, "検出した矩形を画像の上に重ねて見せる(まだ何も書き換えません)"));
                    _spriteEditorButton = MakeButton("Sprite Editor で手動補正", null, OpenInSpriteEditor, "検出した矩形を Importer に書き込んでから Sprite Editor を開く。直したら「切ってある画像」で作る");
                    _spriteEditorButton.SetEnabled(_detectedRects != null);
                    line.Add(_spriteEditorButton);
                    card.Body.Add(line);
                    _detectLabel = Note(string.Empty);
                    card.Body.Add(_detectLabel);
                    _detectPreview = new IMGUIContainer(DrawDetectPreview);
                    _detectPreview.AddToClassList("ux-wz__detect");
                    _detectPreview.RegisterCallback<GeometryChangedEvent>(_ => UpdateDetectPreviewHeight());
                    card.Body.Add(_detectPreview);
                    UpdateDetectPreviewHeight();
                    break;
                }

                default:
                {
                    var card = NewCard(page, "切ってある画像", "Importer で切ってあるスプライトをそのまま使います");
                    card.Body.Add(Note("画像の Sprite Mode が Multiple で、スプライトが切ってあることを確認してください(空なら作成時にエラーになります)。"));
                    break;
                }
            }
        }

        private void RunDetectPreview()
        {
            _detectedRects = null;
            _detectedTexture = null;
            _spriteEditorButton?.SetEnabled(false);
            if (_texture == null)
            {
                _detectLabel.text = "Texture が未設定です。";
                UpdateDetectPreviewHeight();
                return;
            }

            if (!AutomaticSpriteSlicer.DetectRects(_texture, _autoMinSize, _autoExtrude, out var rects, out var rows, out var cols, out var regular))
            {
                _detectLabel.text = "検出に失敗しました(Console 参照)。";
                UpdateDetectPreviewHeight();
                return;
            }

            _detectedRects = rects;
            _detectedTexture = _texture;
            // 検出矩形は元画像のピクセル座標。表示用テクスチャは Max Size で縮小されていることがあるので、縮尺と Y 反転は元画像サイズで行う。
            _detectedSourceSize = new Vector2Int(_texture.width, _texture.height);
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(_texture)) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out var sw, out var sh);
                if (sw > 0 && sh > 0)
                {
                    _detectedSourceSize = new Vector2Int(sw, sh);
                }
            }

            _spriteEditorButton?.SetEnabled(true);
            _detectLabel.text = $"検出: {rects.Length} 枚 / 推定 {rows} 行 x {cols} 列({(regular ? "規則的グリッド" : "不規則")})。緑 = 生成に使う矩形(番号 = フレーム順)";
            UpdateDetectPreviewHeight();
        }

        private void UpdateDetectPreviewHeight()
        {
            if (_detectPreview == null)
            {
                return;
            }

            float height = 0f;
            if (_detectedTexture != null)
            {
                var resolved = _detectPreview.resolvedStyle.width;
                var width = float.IsNaN(resolved) || resolved < 64f ? 320f : resolved;
                var scale = Mathf.Min(width / _detectedSourceSize.x, DetectPreviewMaxHeight / _detectedSourceSize.y);
                height = Mathf.Ceil(_detectedSourceSize.y * scale) + 4f;
            }

            var current = _detectPreview.resolvedStyle.height;
            if (!float.IsNaN(current) && Mathf.Abs(current - height) < 0.5f)
            {
                return;
            }

            _detectPreview.style.height = height; // 高さは画像の縦横比で決まる(USS では表せない)
            _detectPreview.MarkDirtyRepaint();
        }

        // 画像を縮小表示し、検出矩形を緑枠 + 番号で重ねる(テクスチャは左下原点、IMGUI は左上原点なので Y を反転)。
        private void DrawDetectPreview()
        {
            if (_detectedTexture == null || _detectedRects == null)
            {
                return;
            }

            var area = _detectPreview.contentRect;
            if (area.width <= 1f)
            {
                return;
            }

            var srcW = (float)_detectedSourceSize.x;
            var srcH = (float)_detectedSourceSize.y;
            var scale = Mathf.Min(area.width / srcW, DetectPreviewMaxHeight / srcH);
            var drawn = new Rect(area.x, area.y, srcW * scale, srcH * scale);
            EditorGUI.DrawRect(drawn, new Color(0.12f, 0.12f, 0.12f));
            GUI.DrawTexture(drawn, _detectedTexture, ScaleMode.StretchToFill, true);

            var fill = new Color(0.3f, 1f, 0.3f, 0.12f);
            var line = new Color(0.3f, 1f, 0.3f, 0.9f);
            for (var i = 0; i < _detectedRects.Length; i++)
            {
                var r = _detectedRects[i];
                var rect = new Rect(drawn.x + r.x * scale, drawn.y + (srcH - r.y - r.height) * scale, r.width * scale, r.height * scale);
                EditorGUI.DrawRect(rect, fill);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), line);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), line);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), line);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), line);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 1f, 40f, 14f), i.ToString(), EditorStyles.whiteBoldLabel);
            }
        }

        // 検出矩形を Importer に確定させて Sprite Editor を開く(2D Sprite パッケージが無いと開けないので案内する)。
        private void OpenInSpriteEditor()
        {
            if (_detectedTexture == null || _detectedRects == null)
            {
                _detectLabel.text = "先に「検出プレビュー」を実行してください。";
                return;
            }

            if (!AutomaticSpriteSlicer.ApplyRectsAndCollect(_detectedTexture, _detectedRects, out var sprites))
            {
                _detectLabel.text = "矩形の書き込みに失敗しました(Console 参照)。";
                return;
            }

            _template = Template.Existing; // 直したら、そのスプライトをそのまま使う
            var opened = EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor");
            Selection.activeObject = _detectedTexture;
            _detectLabel.text = opened
                ? $"{sprites.Length} 枚を Importer に書き込みました。Sprite Editor で直して Apply したら「次へ」。雛形は「切ってある画像」に切り替えました"
                : $"{sprites.Length} 枚を Importer に書き込みました。Sprite Editor は 2D Sprite パッケージ(com.unity.2d.sprite)が無いため開けません。Package Manager で導入してください。雛形は「切ってある画像」に切り替えました";
        }

        // ── ページ 3: 名前と動き ──

        private string[] EntryNames()
        {
            var profile = _profile ?? Anim2DImportProfile.FindOrDefault();
            var entries = profile != null ? profile.Entries : null;
            if (entries == null || entries.Length == 0)
            {
                return new[] { "(プロファイルに Entry がありません)" };
            }

            var names = new string[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                names[i] = entries[i].Name;
            }

            return names;
        }

        private void BuildNamingPage(VisualElement page)
        {
            var naming = NewCard(page, "名前", "Clip のファイル名(Anim_名前_ステート)と Anim2DData の名前・カテゴリになります");
            var drop = new DropdownField(new List<string>(EntryNames()), 0);
            drop.RegisterValueChangedCallback(evt => ApplyProfileEntry(evt.newValue));
            Row(naming, "プロファイルから選ぶ", "プロファイルの Entry を選ぶと Name / State に入ります(任意)。", FieldTier.Advanced, drop);

            var name = new TextField { value = _name };
            name.RegisterValueChangedCallback(evt =>
            {
                _name = evt.newValue;
                UpdateNav();
            });
            Row(naming, "Name", "キャラクターや物の名前(例: Player)。", FieldTier.Required, name);

            var state = new TextField { value = _state };
            state.RegisterValueChangedCallback(evt =>
            {
                _state = evt.newValue;
                UpdateNav();
            });
            Row(naming, "State", "動きの名前(例: Idle / Run / Attack)。Animator のステート名にもなります。", FieldTier.Required, state);

            if (!IsDirection)
            {
                var mode = new EnumField(_directionMode);
                mode.RegisterValueChangedCallback(evt => _directionMode = (DirectionMode)evt.newValue);
                Row(naming, "方向(単一クリップ)", "Clip 名に角度の接尾辞を付ける / BlendTree に登録する向き。None = 方向なし。", FieldTier.Advanced, mode);
            }

            var anim = NewCard(page, "アニメーション", "コマの速さ・ループ・長さ");
            Row(anim, "Frame Rate", "1 秒あたりのコマ数の基準(sampleRate)。", FieldTier.Common, IntField(_frameRate, 1, v => _frameRate = v));
            var loop = new Toggle { value = _loop };
            loop.RegisterValueChangedCallback(evt => _loop = evt.newValue);
            Row(anim, "Loop", "ON なら繰り返し再生する Clip にします。", FieldTier.Common, loop);
            var lengthMode = new EnumField(_lengthMode);
            lengthMode.RegisterValueChangedCallback(evt => _lengthMode = (ClipLengthMode)evt.newValue);
            Row(anim, "Length Mode", "長さの単位。Frames = フレーム数 / Seconds = 秒数。", FieldTier.Advanced, lengthMode);
            var length = new FloatField { value = _length };
            length.RegisterValueChangedCallback(evt => _length = Mathf.Max(0.01f, evt.newValue));
            Row(anim, "Length", "Clip の長さ(上の単位)。", FieldTier.Common, length);

            var ctrl = NewCard(page, "Animator(任意)", "指定すると、方向付きのクリップを BlendTree(2D Freeform Directional)に登録します");
            var controller = new ObjectField { objectType = typeof(AnimatorController), value = _controller, allowSceneObjects = false };
            controller.RegisterValueChangedCallback(evt => _controller = evt.newValue as AnimatorController);
            Row(ctrl, "AnimatorController", "登録先の Controller。空なら登録しません。", FieldTier.Advanced, controller);
            Row(ctrl, "Layer", "登録先のレイヤー番号。", FieldTier.Advanced, IntField(_layer, 0, v => _layer = v));
        }

        private void ApplyProfileEntry(string entryName)
        {
            var profile = _profile ?? Anim2DImportProfile.FindOrDefault();
            if (profile == null || profile.Entries == null)
            {
                return;
            }

            foreach (var entry in profile.Entries)
            {
                if (entry.Name == entryName)
                {
                    _name = entry.Name;
                    if (entry.States != null && entry.States.Length > 0)
                    {
                        _state = entry.States[0];
                    }

                    Rebuild();
                    return;
                }
            }
        }

        // ── ページ 4: 確認と登録 ──

        private void BuildConfirmPage(VisualElement page)
        {
            var r = BuildRequest();
            var card = NewCard(page, "作る物", "「作成」を押すと、Clip(.anim)と Anim2DData を作り、このあと Anim2D Editor で開きます");
            AddLine(card, "雛形", TemplateTitle(_template));
            AddLine(card, "素材", IsDirection ? DirectionSummary(r) : (_texture != null ? _texture.name : "(未選択)"));
            AddLine(card, "切り方", SliceSummary(r));
            var angle = !IsDirection && DirectionAngle.HasAngle(r.DirectionMode) ? DirectionAngle.ToAngle(r.DirectionMode) : -1;
            AddLine(card, "Clip", IsDirection ? $"{NamingRuleResolver.BuildClipName(r.Name, r.State, -1)} + 角度(例 _0)の {r.DirectionTextures.Length} 本" : NamingRuleResolver.BuildClipName(r.Name, r.State, angle) + ".anim");
            AddLine(card, "保存先", Anim2DCreateService.ResolveClipFolder(r));
            AddLine(card, "Anim2DData", $"{r.Name} {r.State}(カテゴリ {(r.Profile ?? Anim2DImportProfile.FindOrDefault()).ResolveCategory(r.Name)})");
            AddLine(card, "動き", $"{r.FrameRate} fps · {(r.LengthMode == ClipLengthMode.Frames ? r.Length + " フレーム" : r.Length + " 秒")} · {(r.Loop ? "ループ" : "1 回")}");
            AddLine(card, "Animator", r.Controller != null ? $"{r.Controller.name}(Layer {r.Layer})へ BlendTree 登録" : "登録しない");

            _resultLabel = new Label(_resultMessage);
            _resultLabel.AddToClassList("ux-wz__line");
            page.Add(_resultLabel);
        }

        private static void AddLine(UxCard card, string label, string value)
        {
            var l = new Label($"{label}: {value}");
            l.AddToClassList("ux-wz__line");
            card.Body.Add(l);
        }

        private static string TemplateTitle(Template t)
        {
            foreach (var x in Templates)
            {
                if (x.template == t)
                {
                    return x.title;
                }
            }

            return t.ToString();
        }

        private static string SliceSummary(Anim2DCreateRequest r)
        {
            switch (r.Slice)
            {
                case Anim2DSliceMode.Grid: return $"グリッド {r.GridColumns} 列 × {r.GridRows} 行 · {r.GridSpriteCount} 枚";
                case Anim2DSliceMode.Automatic: return $"自動検出(最小 {r.AutoMinSize}px · Extrude {r.AutoExtrude})";
                default: return "切ってあるスプライトをそのまま使う";
            }
        }

        private static string DirectionSummary(Anim2DCreateRequest r)
        {
            var n = 0;
            foreach (var t in r.DirectionTextures)
            {
                n += t != null ? 1 : 0;
            }

            return $"{n} / {r.DirectionTextures.Length} 方向に画像あり";
        }

        private void Create()
        {
            var created = Anim2DCreateService.Create(BuildRequest(), out var message);
            _resultMessage = created != null ? "作成しました: " + message : "作れませんでした: " + message;
            if (_resultLabel != null)
            {
                _resultLabel.text = _resultMessage;
            }

            if (created != null)
            {
                Anim2DEditor.Open(created);
                Close();
            }
        }
    }
}
