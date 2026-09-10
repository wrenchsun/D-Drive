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

            var detectButton = new Button(() => RunDetectPreview()) { text = "検出プレビュー(Automatic)" };
            root.Add(detectButton);
            _resultLabel = new Label(string.Empty) { style = { whiteSpace = WhiteSpace.Normal } };
            root.Add(_resultLabel);

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

        private void RunDetectPreview()
        {
            if (_texture == null)
            {
                _resultLabel.text = "Texture が未設定です。";
                return;
            }

            if (!AutomaticSpriteSlicer.DetectRects(_texture, _autoMinSize, _autoExtrude, out var rects, out var rows, out var cols, out var regular))
            {
                _resultLabel.text = "検出に失敗しました(Console 参照)。";
                return;
            }

            _resultLabel.text = $"検出: {rects.Length} 枚 / 推定 {rows} 行 x {cols} 列(regular={regular})";
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
