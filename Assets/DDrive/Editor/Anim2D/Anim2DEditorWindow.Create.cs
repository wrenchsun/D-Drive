using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim2D
{
    public sealed partial class Anim2DEditorWindow
    {
        // 入力モード(前段の Sprite 取得方法)。移植元: Katsuya.Tools.SpriteAnimation.EditorTools.InputMode。
        private enum SliceInputMode
        {
            Grid,
            Automatic,
            Existing,
        }

        private sealed class DirectionEntry
        {
            public int Angle;
            public Texture2D Texture;
        }

        // Create モードの入力(EditorWindow 再オープンをまたいで消えても実害が薄いのでフィールドで保持)。
        private SliceInputMode _inputMode = SliceInputMode.Grid;
        private Texture2D _texture;
        private int _gridColumns = 4;
        private int _gridRows = 1;
        private int _gridSpriteCount = 4;
        private int _autoMinSize = 16;
        private int _autoExtrude;

        private string _name = string.Empty;
        private string _state = string.Empty;
        private DirectionMode _directionMode = DirectionMode.None;

        private int _frameRate = 12;
        private bool _loop = true;
        private ClipLengthMode _lengthMode = ClipLengthMode.Frames;
        private float _length = 8f;

        private AnimatorController _controller;
        private int _layerIndex;

        private DirectionSet _directionSet = DirectionSet.None;
        private readonly List<DirectionEntry> _directionEntries = new();
        private VisualElement _directionListContainer;
        private Label _resultLabel;

        private void BuildCreateSection(VisualElement root)
        {
            root.Add(new Label("入力") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });

            var inputModeField = new EnumField("入力モード", _inputMode);
            inputModeField.RegisterValueChangedCallback(evt => _inputMode = (SliceInputMode)evt.newValue);
            root.Add(inputModeField);

            var gridColumnsField = new IntegerField("Columns") { value = _gridColumns };
            gridColumnsField.RegisterValueChangedCallback(evt => _gridColumns = Mathf.Max(1, evt.newValue));
            var gridRowsField = new IntegerField("Rows") { value = _gridRows };
            gridRowsField.RegisterValueChangedCallback(evt => _gridRows = Mathf.Max(1, evt.newValue));
            var gridCountField = new IntegerField("Sprite Count") { value = _gridSpriteCount };
            gridCountField.RegisterValueChangedCallback(evt => _gridSpriteCount = Mathf.Max(1, evt.newValue));
            root.Add(gridColumnsField);
            root.Add(gridRowsField);
            root.Add(gridCountField);

            var autoMinSizeField = new IntegerField("Min Sprite Size") { value = _autoMinSize };
            autoMinSizeField.RegisterValueChangedCallback(evt => _autoMinSize = Mathf.Max(1, evt.newValue));
            var autoExtrudeField = new IntegerField("Extrude") { value = _autoExtrude };
            autoExtrudeField.RegisterValueChangedCallback(evt => _autoExtrude = Mathf.Max(0, evt.newValue));
            root.Add(autoMinSizeField);
            root.Add(autoExtrudeField);

            var directionSetField = new EnumField("方向セット(BlendTree)", _directionSet);
            directionSetField.RegisterValueChangedCallback(evt =>
            {
                _directionSet = (DirectionSet)evt.newValue;
                RebuildDirectionEntries();
            });
            root.Add(directionSetField);

            var textureField = new ObjectField("Texture(単一)") { objectType = typeof(Texture2D), value = _texture };
            textureField.RegisterValueChangedCallback(evt => _texture = evt.newValue as Texture2D);
            root.Add(textureField);

            var detectRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            detectRow.Add(new Button(() => RunDetectPreview()) { text = "検出プレビュー(Automatic)" });
            _spriteEditorButton = new Button(OpenInSpriteEditor)
            {
                text = "Sprite Editor で手動補正",
                tooltip = "検出した矩形を Importer に書き込んでから Sprite Editor を開く。直したら入力モードを「既存スプライト」にして生成する",
            };
            _spriteEditorButton.SetEnabled(false);
            detectRow.Add(_spriteEditorButton);
            root.Add(detectRow);
            _resultLabel = new Label(string.Empty) { style = { whiteSpace = WhiteSpace.Normal } };
            root.Add(_resultLabel);

            // 検出結果をテクスチャの上に重ねて描く(OH_CASE2026_ITAMI の Sprite Animation Tool から取り込み、2026-09-11)。
            _detectPreview = new IMGUIContainer(DrawDetectPreview) { style = { height = 0, marginTop = 2, marginBottom = 4 } };
            root.Add(_detectPreview);

            _directionListContainer = new VisualElement();
            root.Add(_directionListContainer);
            RebuildDirectionEntries();

            root.Add(new Label("命名") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });

            var profileEntryDropdown = new DropdownField("プロファイルから選択", new List<string>(BuildEntryNames()), 0);
            profileEntryDropdown.RegisterValueChangedCallback(evt => ApplyProfileEntry(evt.newValue));
            root.Add(profileEntryDropdown);

            var nameField = new TextField("Name") { value = _name };
            nameField.RegisterValueChangedCallback(evt => _name = evt.newValue);
            root.Add(nameField);

            var stateField = new TextField("State") { value = _state };
            stateField.RegisterValueChangedCallback(evt => _state = evt.newValue);
            root.Add(stateField);

            var directionModeField = new EnumField("方向(単一クリップの命名/登録用)", _directionMode);
            directionModeField.RegisterValueChangedCallback(evt => _directionMode = (DirectionMode)evt.newValue);
            root.Add(directionModeField);

            root.Add(new Label("アニメーション") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });

            var frameRateField = new IntegerField("Frame Rate") { value = _frameRate };
            frameRateField.RegisterValueChangedCallback(evt => _frameRate = Mathf.Max(1, evt.newValue));
            root.Add(frameRateField);

            var loopField = new Toggle("Loop") { value = _loop };
            loopField.RegisterValueChangedCallback(evt => _loop = evt.newValue);
            root.Add(loopField);

            var lengthModeField = new EnumField("Length Mode", _lengthMode);
            lengthModeField.RegisterValueChangedCallback(evt => _lengthMode = (ClipLengthMode)evt.newValue);
            root.Add(lengthModeField);

            var lengthField = new FloatField("Length") { value = _length };
            lengthField.RegisterValueChangedCallback(evt => _length = Mathf.Max(0.01f, evt.newValue));
            root.Add(lengthField);

            root.Add(new Label("Animator(任意)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });

            var controllerField = new ObjectField("AnimatorController") { objectType = typeof(AnimatorController), value = _controller };
            controllerField.RegisterValueChangedCallback(evt => _controller = evt.newValue as AnimatorController);
            root.Add(controllerField);

            var layerField = new IntegerField("Layer") { value = _layerIndex };
            layerField.RegisterValueChangedCallback(evt => _layerIndex = Mathf.Max(0, evt.newValue));
            root.Add(layerField);

            var generateButton = new Button(RunGenerate) { text = "生成", style = { marginTop = 10, height = 28 } };
            root.Add(generateButton);
        }

        private string[] BuildEntryNames()
        {
            var entries = _profile != null ? _profile.Entries : null;
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

        private void ApplyProfileEntry(string name)
        {
            if (_profile == null || _profile.Entries == null)
            {
                return;
            }

            foreach (var entry in _profile.Entries)
            {
                if (entry.Name == name)
                {
                    _name = entry.Name;
                    if (entry.States != null && entry.States.Length > 0)
                    {
                        _state = entry.States[0];
                    }

                    return;
                }
            }
        }

        private void RebuildDirectionEntries()
        {
            _directionListContainer.Clear();
            _directionEntries.Clear();

            if (_directionSet == DirectionSet.None)
            {
                return;
            }

            var angles = _directionSet == DirectionSet.Four ? DirectionAngle.FourAngles : DirectionAngle.EightAngles;
            _directionListContainer.Add(new Label($"方向ごとの Texture({angles.Length} 方向。空欄は無視されます)"));
            foreach (var angle in angles)
            {
                var entry = new DirectionEntry { Angle = angle };
                _directionEntries.Add(entry);

                var field = new ObjectField($"{angle}°") { objectType = typeof(Texture2D) };
                field.RegisterValueChangedCallback(evt => entry.Texture = evt.newValue as Texture2D);
                _directionListContainer.Add(field);
            }
        }

        private Rect[] _detectedRects;
        private Texture2D _detectedTexture;
        private Vector2Int _detectedSourceSize; // 元画像のピクセルサイズ(矩形の座標空間)
        private Button _spriteEditorButton;
        private IMGUIContainer _detectPreview;
        private const float DetectPreviewMaxHeight = 260f;

        private void RunDetectPreview()
        {
            _detectedRects = null;
            _detectedTexture = null;
            _spriteEditorButton?.SetEnabled(false);
            if (_texture == null)
            {
                _resultLabel.text = "Texture が未設定です。";
                UpdateDetectPreviewHeight();
                return;
            }

            if (!AutomaticSpriteSlicer.DetectRects(_texture, _autoMinSize, _autoExtrude, out var rects, out var rows, out var cols, out var regular))
            {
                _resultLabel.text = "検出に失敗しました(Console 参照)。";
                UpdateDetectPreviewHeight();
                return;
            }

            _detectedRects = rects;
            _detectedTexture = _texture;
            // 検出矩形は元画像のピクセル座標(DetectRects が Max Size を 16384 にして検出する)。表示用テクスチャは
            // Max Size で縮小されている(例: 2500x2000 → 2048x1638)ので、縮尺と Y 反転は元画像サイズで行う(2026-09-11 修正)。
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
            _resultLabel.text = $"検出: {rects.Length} 枚 / 推定 {rows} 行 x {cols} 列({(regular ? "規則的グリッド" : "不規則")})。緑 = 生成に使う矩形(番号 = フレーム順)";
            UpdateDetectPreviewHeight();
        }

        private void UpdateDetectPreviewHeight()
        {
            if (_detectPreview == null)
            {
                return;
            }

            if (_detectedTexture == null)
            {
                _detectPreview.style.height = 0;
                return;
            }

            var resolved = _detectPreview.resolvedStyle.width;
            var width = float.IsNaN(resolved) || resolved < 64f ? 320f : resolved; // レイアウト前は NaN
            var scale = Mathf.Min(width / _detectedSourceSize.x, DetectPreviewMaxHeight / _detectedSourceSize.y);
            _detectPreview.style.height = Mathf.Ceil(_detectedSourceSize.y * scale) + 4f;
            _detectPreview.MarkDirtyRepaint();
        }

        // テクスチャを縮小表示し、検出矩形を緑枠 + 番号で重ねる(テクスチャは左下原点、IMGUI は左上原点なので Y を反転)。
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

            // 縮尺・Y 反転は元画像サイズ基準(矩形の座標空間)。テクスチャは同じ比率なので StretchToFill で重ねる。
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
                var x = drawn.x + r.x * scale;
                var y = drawn.y + (srcH - r.y - r.height) * scale;
                var rect = new Rect(x, y, r.width * scale, r.height * scale);
                EditorGUI.DrawRect(rect, fill);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), line);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), line);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), line);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), line);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 1f, 40f, 14f), i.ToString(), EditorStyles.whiteBoldLabel);
            }
        }

        // 検出矩形を Importer に確定させて Sprite Editor を開く(2D Sprite パッケージが無いと Sprite Editor は開けないので案内する)。
        private void OpenInSpriteEditor()
        {
            if (_detectedTexture == null || _detectedRects == null)
            {
                _resultLabel.text = "先に「検出プレビュー」を実行してください。";
                return;
            }

            if (!AutomaticSpriteSlicer.ApplyRectsAndCollect(_detectedTexture, _detectedRects, out var sprites))
            {
                _resultLabel.text = "矩形の書き込みに失敗しました(Console 参照)。";
                return;
            }

            _inputMode = SliceInputMode.Existing;
            var opened = EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor");
            Selection.activeObject = _detectedTexture;
            _resultLabel.text = opened
                ? $"{sprites.Length} 枚を Importer に書き込みました。Sprite Editor で直したら Apply → 入力モードは「既存スプライト」に切り替えてあります"
                : $"{sprites.Length} 枚を Importer に書き込みました。Sprite Editor は 2D Sprite パッケージ(com.unity.2d.sprite)が無いため開けません。Package Manager で導入してください。入力モードは「既存スプライト」に切り替えてあります";
        }

        private bool TrySlice(Texture2D texture, out Sprite[] sprites)
        {
            sprites = null;
            if (texture == null)
            {
                Debug.LogError("[Anim2DEditorWindow] Texture が未設定です。");
                return false;
            }

            switch (_inputMode)
            {
                case SliceInputMode.Grid:
                    return SpriteSlicer.SliceAndCollect(texture, _gridColumns, _gridRows, _gridSpriteCount, out sprites);
                case SliceInputMode.Automatic:
                    if (!AutomaticSpriteSlicer.DetectRects(texture, _autoMinSize, _autoExtrude, out var rects, out _, out _, out _))
                    {
                        return false;
                    }

                    return AutomaticSpriteSlicer.ApplyRectsAndCollect(texture, rects, out sprites);
                default:
                    return ExistingSpriteCollector.Collect(texture, out sprites);
            }
        }

        private void RunGenerate()
        {
            if (string.IsNullOrEmpty(_name) || string.IsNullOrEmpty(_state))
            {
                Debug.LogError("[Anim2DEditorWindow] Name / State を入力してください。");
                return;
            }

            var profile = _profile ?? Anim2DImportProfile.FindOrDefault();
            var folder = string.IsNullOrEmpty(profile.DefaultClipFolder) ? "Assets/GameData/Anim2D/Clips" : profile.DefaultClipFolder;
            AssetCreationService.EnsureFolder(folder);

            if (_directionSet == DirectionSet.None)
            {
                GenerateSingle(folder);
            }
            else
            {
                GenerateDirectionSet(folder);
            }
        }

        private void GenerateSingle(string folder)
        {
            var profile = _profile ?? Anim2DImportProfile.FindOrDefault();

            if (!TrySlice(_texture, out var sprites))
            {
                Debug.LogError("[Anim2DEditorWindow] スプライト取得に失敗しました。");
                return;
            }

            var angle = DirectionAngle.HasAngle(_directionMode) ? DirectionAngle.ToAngle(_directionMode) : -1;
            if (_directionMode == DirectionMode.FromName && _texture != null &&
                NamingRuleResolver.TryExtractAngleFromName(_texture.name, out var extracted))
            {
                angle = extracted;
            }

            var clipName = NamingRuleResolver.BuildClipName(_name, _state, angle);
            if (!AnimationClipBuilder.Build(sprites, folder, clipName, _frameRate, _lengthMode, _length, _loop, out var clip))
            {
                Debug.LogError("[Anim2DEditorWindow] AnimationClip 生成に失敗しました。");
                return;
            }

            if (_controller != null && angle >= 0)
            {
                BlendTreeRegistrar.Register(_controller, _layerIndex, _state, clip, angle, ConfirmOverwrite);
            }

            var identifier = AssetNamingService.ToIdentifier(_name + _state, "Anim2D");
            var displayName = $"{_name} {_state}";
            var asset = AssetCreationService.Create(
                typeof(Anim2DData),
                AssetType.Anim2D,
                displayName,
                profile.ResolveCategory(_name),
                identifier,
                configure: d =>
                {
                    var a = (Anim2DData)d;
                    a.Clip = clip;
                    a.StateName = _state;
                    a.Layer = _layerIndex;
                    a.Loop = _loop;
                    a.Directions = DirectionSet.None;
                    a.Retiming = ValueDef.Constant01(1f);
                });

            Debug.Log(asset != null
                ? $"[Anim2DEditorWindow] 生成完了: {clip.name}({sprites.Length} 枚) → {AssetDatabase.GetAssetPath(asset)}"
                : "[Anim2DEditorWindow] Anim2DData の作成に失敗しました。");

            if (asset is Anim2DData created)
            {
                _editTarget = created;
                _editTargetField?.SetValueWithoutNotify(created);
                RefreshEventSummary();
                RefreshValidation();
            }
        }

        private void GenerateDirectionSet(string folder)
        {
            var required = _directionSet == DirectionSet.Four ? Anim2DData.FourDirections : Anim2DData.EightDirections;
            var ordered = new List<(int angle, AnimationClip clip)>();

            foreach (var entry in _directionEntries)
            {
                if (entry.Texture == null)
                {
                    continue;
                }

                if (!TrySlice(entry.Texture, out var sprites))
                {
                    Debug.LogError($"[Anim2DEditorWindow] 角度 {entry.Angle} のスプライト取得に失敗しました。スキップします。");
                    continue;
                }

                var clipName = NamingRuleResolver.BuildClipName(_name, _state, entry.Angle);
                if (!AnimationClipBuilder.Build(sprites, folder, clipName, _frameRate, _lengthMode, _length, _loop, out var clip))
                {
                    Debug.LogError($"[Anim2DEditorWindow] 角度 {entry.Angle} の AnimationClip 生成に失敗しました。スキップします。");
                    continue;
                }

                if (_controller != null)
                {
                    BlendTreeRegistrar.Register(_controller, _layerIndex, _state, clip, entry.Angle, ConfirmOverwrite);
                }

                ordered.Add((entry.Angle, clip));
            }

            if (ordered.Count < required)
            {
                Debug.LogWarning($"[Anim2DEditorWindow] 方向クリップが不足しています({ordered.Count}/{required})。Anim2DData は生成しますが Validation で検出されます。");
            }

            ordered.Sort((a, b) => a.angle.CompareTo(b.angle));
            var directionClips = new AnimationClip[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
            {
                directionClips[i] = ordered[i].clip;
            }

            var primaryClip = ordered.Count > 0 ? ordered[0].clip : null;
            if (primaryClip == null)
            {
                Debug.LogError("[Anim2DEditorWindow] 有効な方向クリップが 1 枚もありません。Anim2DData を作成しませんでした。");
                return;
            }

            var identifier = AssetNamingService.ToIdentifier(_name + _state, "Anim2D");
            var displayName = $"{_name} {_state}";
            var asset = AssetCreationService.Create(
                typeof(Anim2DData),
                AssetType.Anim2D,
                displayName,
                (_profile ?? Anim2DImportProfile.FindOrDefault()).ResolveCategory(_name),
                identifier,
                configure: d =>
                {
                    var a = (Anim2DData)d;
                    a.Clip = primaryClip;
                    a.StateName = _state;
                    a.Layer = _layerIndex;
                    a.Loop = _loop;
                    a.Directions = _directionSet;
                    a.DirectionClips = directionClips;
                    a.ParamXName = BlendTreeRegistrar.ParamXName;
                    a.ParamYName = BlendTreeRegistrar.ParamYName;
                    a.Retiming = ValueDef.Constant01(1f);
                });

            Debug.Log(asset != null
                ? $"[Anim2DEditorWindow] 方向セット生成完了: {ordered.Count} 方向 → {AssetDatabase.GetAssetPath(asset)}"
                : "[Anim2DEditorWindow] Anim2DData の作成に失敗しました。");

            if (asset is Anim2DData created)
            {
                _editTarget = created;
                _editTargetField?.SetValueWithoutNotify(created);
                RefreshEventSummary();
                RefreshValidation();
            }
        }

        private static BlendTreeConflictResolution ConfirmOverwrite(int angle)
        {
            if (!EditorUtility.DisplayDialog(
                    "BlendTree 登録",
                    $"角度 {angle} には既にクリップが登録されています。上書きしますか?",
                    "上書き", "スキップ"))
            {
                return BlendTreeConflictResolution.Skip;
            }

            return BlendTreeConflictResolution.Overwrite;
        }
    }
}
