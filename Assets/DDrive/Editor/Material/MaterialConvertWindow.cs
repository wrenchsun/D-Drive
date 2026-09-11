using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2「相互変換機能」— 変換エディタ(チケット 3-6、2026-09-10)。
    // MaterialData(ShaderA) を別シェーダーへ変換する。共通データは維持し、固有パラメータは ShaderConversionTable で付け替え、
    // 結果(維持 / 対応 / 破棄)を差分プレビューとして表示してから「新規 Data として作成」か「この Data を変換」(Undo 対応)を行う。
    // 2026-09-11: 見た目の比較はこのウィンドウ内で行う([09] §2 の Material 例外)。変換元(A)と、変換結果をメモリ上の
    // 一時 MaterialData に適用したもの(B = 変換後)を MaterialThumbnailRenderer で描き、「左右」(2 分割)/「切替」で見比べる。
    // アセットを作る前から比較できるので、変換先シェーダーやテーブルを変えながら確認できる。
    [DDrive.Editor.Inspector.DataEditor(typeof(MaterialData), "シェーダー変換", Order = 10)]
    public sealed class MaterialConvertWindow : EditorWindow
    {
        private const int ThumbnailHeight = 200;
        private const float TurntableSpeedDegPerSec = 45f;

        private enum CompareMode { SideBySide, Toggle }

        private MaterialData _source;
        private Shader _target;
        private MaterialConverter.Result _result;
        private readonly List<ShaderConversionTable> _tables = new();
        private MaterialData _lastCreated; // 直近で「新規 MaterialData として作成」した変換結果

        // 比較プレビュー
        private AssetRegistry _registry;
        private MaterialManager _manager;
        private MaterialThumbnailRenderer _rendererA;
        private MaterialThumbnailRenderer _rendererB;
        private MaterialData _previewData; // 変換後(メモリ上、HideAndDontSave)
        private MaterialPreviewShape _shape = MaterialPreviewShape.Sphere;
        private CompareMode _compareMode = CompareMode.SideBySide;
        private bool _showB;
        private bool _turntable;
        private float _angle;
        private float _pitch;
        private float _lightDeg;
        private bool _dirty;
        private bool _materialDirty;
        private double _lastTickTime;
        private double _lastRebuildTime;

        private ScrollView _root;
        private ObjectField _sourceField;
        private ObjectField _shaderField;
        private Label _summary;
        private VisualElement _diff;
        private Button _createButton;
        private Button _applyButton;

        private VisualElement _previewArea;
        private VisualElement _columnB;
        private Image _imageA;
        private Image _imageB;
        private Label _captionA;
        private Label _captionB;
        private Button _modeButton;
        private Button _abButton;
        private Label _previewNote;

        [MenuItem(DDriveMenu.Editors + "Material 変換")]
        public static void OpenFromMenu() => Open(Selection.activeObject as MaterialData);

        public static void Open(MaterialData target)
        {
            var window = GetWindow<MaterialConvertWindow>("Material 変換");
            window.minSize = new Vector2(480, 420);
            if (target != null)
            {
                window.SetSource(target);
            }
        }

        private void OnEnable()
        {
            _registry = EditorAnchorRegistry.Build();
            _manager = new MaterialManager(_registry);
            _lastTickTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
        }

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorApplication.update -= OnEditorUpdate;
            _rendererA?.Dispose();
            _rendererA = null;
            _rendererB?.Dispose();
            _rendererB = null;
            _manager?.Clear();
            _manager = null;
            if (_previewData != null)
            {
                DestroyImmediate(_previewData);
                _previewData = null;
            }
        }

        public void CreateGUI()
        {
            _root = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            rootVisualElement.Add(_root);

            _sourceField = new ObjectField("変換元 MaterialData") { objectType = typeof(MaterialData), allowSceneObjects = false };
            _sourceField.RegisterValueChangedCallback(evt => SetSource(evt.newValue as MaterialData));
            _root.Add(_sourceField);

            _shaderField = new ObjectField("変換先 Shader") { objectType = typeof(Shader), allowSceneObjects = false };
            _shaderField.RegisterValueChangedCallback(evt =>
            {
                _target = evt.newValue as Shader;
                Refresh();
            });
            _root.Add(_shaderField);

            _root.Add(new Button(ReloadTables) { text = "変換テーブルを再読み込み", tooltip = "プロジェクト内の ShaderConversionTable を集め直す" });

            BuildPreview(_root);

            _summary = new Label { style = { marginTop = 6, marginBottom = 6, whiteSpace = WhiteSpace.Normal } };
            _root.Add(_summary);

            _diff = new VisualElement();
            _root.Add(_diff);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            _createButton = new Button(CreateConverted) { text = "新規 MaterialData として作成", tooltip = "変換元は変えずに、同じカテゴリへ変換後の Data を作る" };
            _applyButton = new Button(ApplyInPlace) { text = "この Data を変換(Undo 可)", tooltip = "変換元の Shader と Specific を書き換える" };
            buttons.Add(_createButton);
            buttons.Add(_applyButton);
            _root.Add(buttons);

            ReloadTables();
            if (_source != null)
            {
                SetSource(_source);
            }
            else if (Selection.activeObject is MaterialData selected)
            {
                SetSource(selected);
            }
        }

        // 変換前(A)/ 変換後(B)のサムネイル(2026-09-11)。
        private void BuildPreview(VisualElement root)
        {
            var foldout = new Foldout { text = "見た目の比較(A = 変換前 / B = 変換後)", value = true };

            _previewArea = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
            var columnA = new VisualElement { style = { flexGrow = 1, flexBasis = 0, flexShrink = 1 } };
            _imageA = CreateImage();
            columnA.Add(_imageA);
            _captionA = new Label { style = { fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter } };
            columnA.Add(_captionA);
            _previewArea.Add(columnA);

            _columnB = new VisualElement { style = { flexGrow = 1, flexBasis = 0, flexShrink = 1, marginLeft = 4 } };
            _imageB = CreateImage();
            _columnB.Add(_imageB);
            _captionB = new Label { style = { fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter } };
            _columnB.Add(_captionB);
            _previewArea.Add(_columnB);
            foldout.Add(_previewArea);

            var controls = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap, marginTop = 2 } };
            _modeButton = new Button(() =>
            {
                _compareMode = _compareMode == CompareMode.SideBySide ? CompareMode.Toggle : CompareMode.SideBySide;
                UpdateCompareUi();
            }) { tooltip = "比較の見せ方: 左右(2 分割) ⇄ 切替(1 枚を A/B で切替)" };
            controls.Add(_modeButton);
            _abButton = new Button(() =>
            {
                _showB = !_showB;
                UpdateCompareUi();
            }) { tooltip = "表示する方を切り替える(A = 変換前、B = 変換後)" };
            controls.Add(_abButton);

            var shapeField = new EnumField(_shape) { style = { width = 80, marginLeft = 6 } };
            shapeField.RegisterValueChangedCallback(evt =>
            {
                var shape = (MaterialPreviewShape)evt.newValue;
                if (shape == MaterialPreviewShape.Model)
                {
                    shapeField.SetValueWithoutNotify(_shape);
                    return;
                }

                _shape = shape;
                _dirty = true;
            });
            controls.Add(shapeField);

            var turntable = new Toggle { text = "回転", value = _turntable, style = { marginLeft = 6 } };
            turntable.RegisterValueChangedCallback(evt => _turntable = evt.newValue);
            controls.Add(turntable);

            var light = new Slider("ライト", 0f, 360f) { value = _lightDeg, style = { flexGrow = 1, minWidth = 140, marginLeft = 6 } };
            light.labelElement.style.minWidth = 40;
            light.RegisterValueChangedCallback(evt =>
            {
                _lightDeg = evt.newValue;
                _dirty = true;
            });
            controls.Add(light);

            var invert = MaterialThumbnailRenderer.CreateInvertToggles();
            invert.style.marginLeft = 6;
            controls.Add(invert);
            foldout.Add(controls);

            _previewNote = new Label { style = { fontSize = 10, whiteSpace = WhiteSpace.Normal, marginLeft = 4 } };
            foldout.Add(_previewNote);

            root.Add(foldout);
            UpdateCompareUi();
        }

        private Image CreateImage()
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, style = { height = ThumbnailHeight } };
            image.RegisterCallback<GeometryChangedEvent>(_ => _dirty = true);
            image.tooltip = "ドラッグで回転(横 = Y 軸、縦 = 傾き)";
            MaterialThumbnailRenderer.AttachDrag(image, (yaw, pitch) =>
            {
                _angle = (_angle + yaw) % 360f;
                _pitch = Mathf.Clamp(_pitch + pitch, -MaterialThumbnailRenderer.MaxPitchDeg, MaterialThumbnailRenderer.MaxPitchDeg);
                _dirty = true;
            });
            return image;
        }

        private void UpdateCompareUi()
        {
            if (_modeButton == null)
            {
                return;
            }

            var hasB = _previewData != null;
            _columnB.style.display = hasB && _compareMode == CompareMode.SideBySide ? DisplayStyle.Flex : DisplayStyle.None;
            _modeButton.text = _compareMode == CompareMode.SideBySide ? "左右" : "切替";
            _modeButton.SetEnabled(hasB);
            _abButton.style.display = hasB && _compareMode == CompareMode.Toggle ? DisplayStyle.Flex : DisplayStyle.None;
            _abButton.text = _showB ? "B → A" : "A → B";
            _dirty = true;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is MaterialData selected && selected != _source)
            {
                SetSource(selected);
            }
        }

        private void SetSource(MaterialData source)
        {
            _source = source;
            if (_root == null)
            {
                return;
            }

            _sourceField.SetValueWithoutNotify(source);
            Refresh();
        }

        private void ReloadTables()
        {
            _tables.Clear();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(ShaderConversionTable)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var table = AssetDatabase.LoadAssetAtPath<ShaderConversionTable>(path);
                if (table != null)
                {
                    _tables.Add(table);
                }
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_root == null)
            {
                return;
            }

            _diff.Clear();
            _result = null;
            var ready = _source != null && _target != null;
            _createButton.SetEnabled(ready);
            _applyButton.SetEnabled(ready && _source.Shader != _target);
            if (!ready)
            {
                _summary.text = _source == null ? "変換元の MaterialData を選択してください" : "変換先の Shader を選択してください";
                RebuildPreviewData();
                return;
            }

            _result = MaterialConverter.Convert(_source, _target, _tables);
            RebuildPreviewData();
            var fromName = _source.Shader != null ? _source.Shader.name : "(既定 Lit)";
            _summary.text = $"{fromName} → {_target.name}\n" +
                            $"共通データ(Albedo / Normal / Mask / Emission / Blend)はそのまま維持。固有: 対応 {_result.MappedCount} / 維持 {_result.KeptCount} / " +
                            $"破棄 {_result.DroppedCount} / 意図的に破棄 {_result.DiscardedCount}" +
                            (_result.UsedTable ? "  (変換テーブル使用)" : $"  (この組の変換テーブル無し。{_tables.Count} 件の Table を確認)");

            BuildCompatibilityTable();
        }

        // ── 互換表(2026-09-11): 名前 / 型 / 値 / 互換(〇×) / 備考。共通チャンネルと固有パラメータの両方を載せる ──

        private static readonly Color OkColor = new(0.55f, 0.9f, 0.55f);
        private static readonly Color NgColor = new(1f, 0.5f, 0.5f);
        private static readonly Color UnsetColor = new(0.6f, 0.6f, 0.6f);

        private void BuildCompatibilityTable()
        {
            _diff.Add(TableRow("名前", "型", "値", "互換", "備考", header: true));

            _diff.Add(SectionRow("共通チャンネル(MaterialCommon。そのまま引き継ぐ。× は変換先に受け口が無く無視される)"));
            var c = _source.Common;
            AddCommonRow("Albedo", "Texture", DescribeId(c.Albedo), c.Albedo.IsValid, MaterialCommonBinding.CommonChannel.Albedo, "_BaseMap / _MainTex");
            AddCommonRow("AlbedoTint", "Color", DescribeColor(c.AlbedoTint), true, MaterialCommonBinding.CommonChannel.AlbedoTint, "_BaseColor / _Color");
            AddCommonRow("Normal", "Texture", DescribeId(c.Normal), c.Normal.IsValid, MaterialCommonBinding.CommonChannel.Normal, "_BumpMap");
            AddCommonRow("NormalScale", "Float", c.NormalScale.ToString("0.###"), c.Normal.IsValid, MaterialCommonBinding.CommonChannel.NormalScale, "_BumpScale / _NormalScale");
            AddCommonRow("Mask", "Texture", DescribeId(c.Mask), c.Mask.IsValid, MaterialCommonBinding.CommonChannel.Mask, "_MetallicGlossMap + _OcclusionMap / _MaskMap");
            AddCommonRow("Metallic", "Float", c.Metallic.ToString("0.###"), true, MaterialCommonBinding.CommonChannel.Metallic, "_Metallic");
            AddCommonRow("Smoothness", "Float", c.Smoothness.ToString("0.###"), true, MaterialCommonBinding.CommonChannel.Smoothness, "_Smoothness / _Glossiness");
            AddCommonRow("Emission", "Texture", DescribeId(c.Emission), c.Emission.IsValid, MaterialCommonBinding.CommonChannel.Emission, "_EmissionMap");
            var emissionOn = c.EmissionIntensity > 0f && (c.Emission.IsValid || c.EmissionColor.maxColorComponent > 0f);
            AddCommonRow("EmissionColor × Intensity", "Color × Float", DescribeColor(c.EmissionColor) + " × " + c.EmissionIntensity.ToString("0.###"), emissionOn, MaterialCommonBinding.CommonChannel.EmissionColor, "_EmissionColor");
            AddCommonRow("Blend", "Enum", c.Blend.ToString(), true, MaterialCommonBinding.CommonChannel.Blend, "_Surface / _SrcBlend / _Mode");
            AddCommonRow("Cutoff", "Float", c.Cutoff.ToString("0.###"), c.Blend == BlendType.Cutout, MaterialCommonBinding.CommonChannel.Cutoff, "_Cutoff");
            AddCommonRow("DoubleSided", "Bool", c.DoubleSided ? "true" : "false", c.DoubleSided, MaterialCommonBinding.CommonChannel.DoubleSided, "_Cull");

            _diff.Add(SectionRow("固有パラメータ(Specific。→ 対応 / = 同名維持 / × 破棄)"));
            if (_result.Entries.Count == 0)
            {
                _diff.Add(TableRow("(なし)", "", "", "", "変換元に固有パラメータがありません"));
            }

            foreach (var entry in _result.Entries)
            {
                var (ok, note) = entry.Outcome switch
                {
                    MaterialConverter.Outcome.Mapped => (true, $"→ {entry.ToProperty}(テーブルで対応)"),
                    MaterialConverter.Outcome.Kept => (true, "同名・同型で維持"),
                    MaterialConverter.Outcome.Discarded => (false, "テーブルで意図的に破棄"),
                    _ => (false, "変換先に無いため破棄"),
                };
                _diff.Add(TableRow(entry.FromProperty, TypeName(entry.Value), Describe(entry.Value), ok ? "〇" : "×", note, ok ? OkColor : NgColor));
            }

            // 変換先にあって変換元に無い固有(既定値で補完される)
            if (_previewData != null && _previewData.Specific != null)
            {
                var added = new List<ShaderParam>();
                foreach (var param in _previewData.Specific)
                {
                    var fromResult = false;
                    foreach (var p in _result.Specific)
                    {
                        if (p.Property == param.Property)
                        {
                            fromResult = true;
                            break;
                        }
                    }

                    if (!fromResult)
                    {
                        added.Add(param);
                    }
                }

                _diff.Add(SectionRow("変換先で新しく追加される固有(変換元に無いもの。シェーダーの既定値で登録される)"));
                if (added.Count == 0)
                {
                    _diff.Add(TableRow("(なし)", "", "", "", "変換先シェーダーに追加の固有パラメータはありません"));
                }

                foreach (var param in added)
                {
                    _diff.Add(TableRow(param.Property, TypeName(param.Value), Describe(param.Value), "〇", "既定値で追加", OkColor));
                }
            }
        }

        // 共通チャンネル 1 行。set=false(未設定・無効)なら「－」で灰色、set かつ変換先に受け口があれば 〇、無ければ ×。
        private void AddCommonRow(string name, string type, string value, bool set, MaterialCommonBinding.CommonChannel channel, string targetProps)
        {
            var supported = MaterialCommonBinding.IsSupported(_target, channel);
            string mark;
            string note;
            Color color;
            if (!set)
            {
                mark = "－";
                note = supported ? "未設定(変換先: " + targetProps + ")" : "未設定(変換先に受け口無し)";
                color = UnsetColor;
            }
            else if (supported)
            {
                mark = "〇";
                note = "→ " + targetProps;
                color = OkColor;
            }
            else
            {
                mark = "×";
                note = "変換先に " + targetProps + " が無い(無視される)";
                color = NgColor;
            }

            _diff.Add(TableRow(name, type, value, mark, note, color));
        }

        private static VisualElement SectionRow(string title)
            => new Label(title) { style = { marginTop = 6, marginBottom = 2, marginLeft = 4, marginRight = 4, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11, whiteSpace = WhiteSpace.Normal } };

        // 列はウィンドウ幅に対する比率(flexBasis 0 + flexGrow)。固定幅だとウィンドウより広くなり、余った備考列が
        // 幅 0 で 1 文字ずつ折り返して行が縦に伸びる(2026-09-11 修正)。
        private static VisualElement TableRow(string name, string type, string value, string mark, string note, Color? color = null, bool header = false)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.FlexStart, marginLeft = 4, marginRight = 4,
                    borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f),
                },
            };
            row.Add(Cell(name, 2.4f, header, color));
            row.Add(Cell(type, 1.0f, header, color));
            row.Add(Cell(value, 2.0f, header, color));
            row.Add(Cell(mark, 0.5f, header, color, TextAnchor.MiddleCenter));
            row.Add(Cell(note, 3.0f, header, color));
            return row;
        }

        private static Label Cell(string text, float grow, bool header, Color? color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var label = new Label(text)
            {
                style =
                {
                    flexBasis = 0, flexGrow = grow, flexShrink = 1, minWidth = 24,
                    paddingLeft = 2, paddingRight = 4, paddingTop = 1, paddingBottom = 1,
                    whiteSpace = WhiteSpace.Normal, unityTextAlign = align, fontSize = 11,
                },
            };

            if (header)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            else if (color.HasValue)
            {
                label.style.color = color.Value;
            }

            return label;
        }

        // 表の「型」列。Object はテクスチャ用途なので Texture と表示する。
        private static string TypeName(ParamValue value) => value.Type == ParamValueType.Object ? "Texture" : value.Type.ToString();

        private static string DescribeId(DDrive.Foundation.Identity.AssetId<TextureMarker> id)
            => id.IsValid ? $"TEX 0x{id.Value:X}" : "未設定";

        private static string DescribeColor(Color color) => $"#{ColorUtility.ToHtmlStringRGBA(color)}";

        // 変換結果をメモリ上の一時 MaterialData(B)に写す。結果が無ければ B を消す。
        private void RebuildPreviewData()
        {
            if (_source == null || _result == null)
            {
                if (_previewData != null)
                {
                    DestroyImmediate(_previewData);
                    _previewData = null;
                }
            }
            else
            {
                if (_previewData == null)
                {
                    _previewData = CreateInstance<MaterialData>();
                    _previewData.hideFlags = HideFlags.HideAndDontSave;
                }

                MaterialConverter.ApplyTo(_previewData, _source, _result);
                _previewData.Specific = MaterialSpecificResolver.Merge(_previewData.Specific, _previewData.Shader);
                _previewData.DisplayName = "変換後";
            }

            _materialDirty = true;
            UpdateCompareUi();
        }

        private static string Describe(ParamValue value) => value.Type switch
        {
            ParamValueType.Float => value.FloatValue.ToString("0.###"),
            ParamValueType.Int => value.IntValue.ToString(),
            ParamValueType.Bool => value.BoolValue ? "true" : "false",
            ParamValueType.Color => $"#{ColorUtility.ToHtmlStringRGBA(value.ColorValue)}",
            ParamValueType.Vector => value.VectorValue.ToString(),
            ParamValueType.Object => value.ObjectValue != null ? value.ObjectValue.name : "(null)",
            _ => value.Type.ToString(),
        };

        private void CreateConverted()
        {
            if (_source == null || _result == null)
            {
                return;
            }

            var baseName = string.IsNullOrEmpty(_source.DisplayName) ? _source.name : _source.DisplayName;
            var identifier = AssetNamingService.ToIdentifier(baseName + " " + _target.name.Substring(_target.name.LastIndexOf('/') + 1), "Mat");
            var source = _source;
            var result = _result;
            var created = AssetCreationService.Create(typeof(MaterialData), AssetType.Material, baseName + " (" + _target.name + ")", _source.Category, identifier,
                data =>
                {
                    var mat = (MaterialData)data;
                    MaterialConverter.ApplyTo(mat, source, result);
                    // 変換先シェーダーにあって表・同名維持で埋まらなかった固有を既定値で補完する(2026-09-11)。
                    mat.Specific = MaterialSpecificResolver.Merge(mat.Specific, mat.Shader);
                });
            if (created != null)
            {
                _lastCreated = created as MaterialData;
                EditorGUIUtility.PingObject(created);
                Selection.activeObject = created;
            }
        }

        private void ApplyInPlace()
        {
            if (_source == null || _result == null)
            {
                return;
            }

            Undo.RecordObject(_source, "Convert Material Shader");
            MaterialConverter.ApplyTo(_source, null, _result);
            // 変換先シェーダーの残りの固有を既定値で補完(同じ Undo グループに入る。2026-09-11)。
            _source.Specific = MaterialSpecificResolver.Merge(_source.Specific, _source.Shader);
            EditorUtility.SetDirty(_source);
            Refresh();
        }

        // ── プレビュー描画 ──

        // 変換元(A)が Inspector / Undo で変わったら結果と B を作り直す。
        private void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if (_source == null)
            {
                return;
            }

            var sourceId = _source.GetInstanceID();
            for (var i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(i, out var evt);
                if (evt.instanceId == sourceId)
                {
                    EditorApplication.delayCall += Refresh;
                    return;
                }
            }
        }

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.25f);
            _lastTickTime = now;
            if (_manager == null || _imageA == null)
            {
                return;
            }

            _manager.Tick(dt);

            if (_materialDirty && now - _lastRebuildTime >= 0.1)
            {
                _materialDirty = false;
                _lastRebuildTime = now;
                _manager.Clear();
                _dirty = true;
            }

            if (_turntable)
            {
                _angle = (_angle + TurntableSpeedDegPerSec * dt) % 360f;
                _dirty = true;
            }

            if ((_source != null && _source.HasAnims) || (_previewData != null && _previewData.HasAnims))
            {
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                Render();
            }
        }

        private void Render()
        {
            if (_source == null)
            {
                _imageA.image = null;
                _imageB.image = null;
                _captionA.text = _captionB.text = "";
                _previewNote.text = "変換元の MaterialData を選択してください";
                return;
            }

            var hasB = _previewData != null;
            var sideBySide = hasB && _compareMode == CompareMode.SideBySide;
            var showB = hasB && _compareMode == CompareMode.Toggle && _showB;

            _rendererA ??= new MaterialThumbnailRenderer();
            var primary = showB ? _previewData : _source;
            RenderInto(_rendererA, _imageA, primary);
            _captionA.text = (showB ? "B: 変換後 " : "A: 変換前 ") + ShaderNameOf(primary);

            if (sideBySide)
            {
                _rendererB ??= new MaterialThumbnailRenderer();
                RenderInto(_rendererB, _imageB, _previewData);
                _captionB.text = "B: 変換後 " + ShaderNameOf(_previewData);
            }

            _previewNote.text = hasB
                ? "B はアセットを作る前のメモリ上の変換結果。回転・ライトは A/B 共通。既定ライトのみ(実シーン照明は Material Editor のシーン配置で)"
                : "変換先 Shader を選ぶと B(変換後)が出ます";
        }

        private void RenderInto(MaterialThumbnailRenderer renderer, Image image, MaterialData data)
        {
            var width = Mathf.RoundToInt(image.resolvedStyle.width);
            if (float.IsNaN(image.resolvedStyle.width) || width < 16)
            {
                width = 300;
            }

            image.image = renderer.Render(_manager.GetData(data), _shape, _angle, _pitch, _lightDeg, width, ThumbnailHeight);
            image.MarkDirtyRepaint();
        }

        private static string ShaderNameOf(MaterialData data)
            => data == null ? "" : "(" + (data.Shader != null ? data.Shader.name : "既定 Lit") + ")";
    }
}
