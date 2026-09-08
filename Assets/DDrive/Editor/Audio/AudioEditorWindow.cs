using DDrive.Editor.Menu;
using DDrive.Editor.Preview;
using DDrive.Foundation.Data;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Audio;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Audio
{
    // [03_audio.md] §5 — Audio 専用エディタ(1-7)。
    // 波形表示 + 範囲ドラッグ(SE: トリム範囲 / BGM: ループ範囲)、実 AudioManager 試聴、
    // 音量/ピッチ/開始位置のライブ調整、3D サウンド確認パッド(ブレンドスペース風 2D UI)、Mixer 表示。
    // ランダム試聴は ▶ が実 Manager の SelectMode 経路(Random/RoundRobin)をそのまま通ることで実現。
    public sealed class AudioEditorWindow : EditorWindow
    {
        private const float WaveformHeight = 96f;
        private const float HandleHitWidth = 8f;
        private const float PadHeight = 200f;

        // ドメインリロード(再コンパイル/PlayMode遷移)後も編集対象を保持する。
        [SerializeField] private AssetDataBase _target;

        private PreviewService _preview;
        private Handle<SeMarker> _lastHandle;

        private ObjectField _targetField;
        private IMGUIContainer _waveformContainer;
        private DropdownField _sourceIndexField;
        private Label _infoLabel;
        private Slider _volumeSlider;
        private Slider _pitchSlider;
        private Slider _startOffsetSlider;
        private Toggle _loopToggle;

        private Texture2D _waveformTexture;
        private AudioClip _waveformClip;
        private int _sourceIndex;
        private int _draggingEdge = -1; // 0=start, 1=end

        // ── 3D サウンド確認パッドの状態 ──
        private VisualElement _padSection;
        private IMGUIContainer _padContainer;
        private Slider _listenerAngleSlider;
        private FloatField _rangeXField;
        private FloatField _rangeYField;
        private Vector2 _listenerPadPos = new(0f, -3f); // メートル。音源(中央)の手前 3m
        private float _listenerAngleDeg;
        private float _rangeX = 10f;
        private float _rangeY = 10f;
        private bool _draggingListener;

        [MenuItem(DDriveMenu.Editors + "Audio")]
        public static void OpenFromMenu() => Open(Selection.activeObject as AssetDataBase);

        public static void Open(AssetDataBase target)
        {
            var window = GetWindow<AudioEditorWindow>("Audio Editor");
            window.minSize = new Vector2(520, 380);
            if (target is SeData or BgmData)
            {
                window.SetTarget(target);
            }
        }

        private void OnEnable()
        {
            _preview = new PreviewService();
        }

        private void OnDisable()
        {
            _preview?.Dispose();
            _preview = null;
            DestroyWaveformTexture();
        }

        private void CreateGUI()
        {
            // ウィンドウが小さい/セクションが増えても内容が見切れないよう、ルートをスクロール可能にする
            // ([09_editor_tools.md] §7 拡縮前提のUI規約)。
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1f } };
            rootVisualElement.Add(scrollView);

            var root = scrollView;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            _targetField = new ObjectField("対象アセット") { objectType = typeof(AssetDataBase) };
            _targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as AssetDataBase));
            root.Add(_targetField);

            _infoLabel = new Label();
            _infoLabel.style.opacity = 0.75f;
            _infoLabel.style.marginBottom = 4;
            root.Add(_infoLabel);

            _sourceIndexField = new DropdownField("編集対象 Source");
            _sourceIndexField.RegisterValueChangedCallback(_ =>
            {
                _sourceIndex = Mathf.Max(0, _sourceIndexField.choices.IndexOf(_sourceIndexField.value));
                InvalidateWaveform();
            });
            root.Add(_sourceIndexField);

            _waveformContainer = new IMGUIContainer(DrawWaveformGui);
            _waveformContainer.style.height = WaveformHeight + 22f;
            _waveformContainer.style.marginTop = 4;
            _waveformContainer.style.marginBottom = 4;
            root.Add(_waveformContainer);

            var trimRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            trimRow.Add(new Button(DetectSilence) { text = "無音自動検出" });
            trimRow.Add(new Button(ApplyTrim) { text = "トリミングを適用(Clips 再生成)" });
            root.Add(trimRow);

            var playRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            playRow.Add(new Button(Play) { text = "▶ 再生", tooltip = "実 AudioManager 経由。Clips 複数時は SelectMode(Random/RoundRobin)通りに選曲される" });
            playRow.Add(new Button(Stop) { text = "■ 停止" });
            _loopToggle = new Toggle("ループ試聴") { tooltip = "アセット本体は変更しない" };
            _loopToggle.style.marginLeft = 8;
            playRow.Add(_loopToggle);
            root.Add(playRow);

            _volumeSlider = new Slider("音量", 0f, 1f) { value = 1f };
            _volumeSlider.RegisterValueChangedCallback(evt =>
            {
                _preview?.AudioManager?.SetVolume(_lastHandle, evt.newValue);
            });
            root.Add(_volumeSlider);

            _pitchSlider = new Slider("ピッチ", 0.1f, 2f) { value = 1f };
            _pitchSlider.RegisterValueChangedCallback(evt =>
            {
                _preview?.AudioManager?.SetPitch(_lastHandle, evt.newValue);
            });
            root.Add(_pitchSlider);

            _startOffsetSlider = new Slider("再生開始位置(秒)", 0f, 1f) { tooltip = "SeData.StartOffsetSec。次の再生から反映" };
            _startOffsetSlider.RegisterValueChangedCallback(evt =>
            {
                if (_target is SeData se)
                {
                    Undo.RecordObject(se, "Change StartOffsetSec");
                    se.StartOffsetSec = evt.newValue;
                    EditorUtility.SetDirty(se);
                }
            });
            root.Add(_startOffsetSlider);

            BuildListenerPadSection(root);

            if (_target == null && Selection.activeObject is AssetDataBase selected && selected is SeData or BgmData)
            {
                SetTarget(selected);
            }
            else
            {
                RefreshTargetUi();
            }
        }

        private void SetTarget(AssetDataBase target)
        {
            _target = target is SeData or BgmData ? target : null;
            _sourceIndex = 0;
            InvalidateWaveform();
            RefreshTargetUi();
        }

        private void RefreshTargetUi()
        {
            if (_targetField != null)
            {
                _targetField.SetValueWithoutNotify(_target);
            }

            var mixer = _target switch
            {
                SeData se => se.Mixer,
                BgmData bgm => bgm.Mixer,
                _ => null,
            };

            _infoLabel.text = _target == null
                ? "SeData または BgmData を選択してください"
                : $"Mixer: {(mixer != null ? mixer.name : "(未割当 — Validation Warning)")}";

            var isSe = _target is SeData;
            _sourceIndexField.style.display = isSe ? DisplayStyle.Flex : DisplayStyle.None;
            _startOffsetSlider.style.display = isSe ? DisplayStyle.Flex : DisplayStyle.None;

            // 3D パッドが無効な対象では、縮尺・角度の入力も無効化する(理由はパッド内に表示)。
            var padValid = PadValidity().valid;
            _rangeXField?.SetEnabled(padValid);
            _rangeYField?.SetEnabled(padValid);
            _listenerAngleSlider?.SetEnabled(padValid);
            _padContainer?.MarkDirtyRepaint();

            if (_target is SeData seData)
            {
                var choices = new System.Collections.Generic.List<string>();
                var count = seData.Sources?.Length ?? 0;
                for (var i = 0; i < count; i++)
                {
                    var src = seData.Sources[i].Source;
                    choices.Add($"[{i}] {(src != null ? src.name : "(未設定)")}");
                }

                if (choices.Count == 0)
                {
                    choices.Add("(Sources 未設定 — Clips[0] の波形を表示)");
                }

                _sourceIndexField.choices = choices;
                _sourceIndexField.SetValueWithoutNotify(choices[Mathf.Clamp(_sourceIndex, 0, choices.Count - 1)]);

                var clipLength = seData.Clips is { Length: > 0 } && seData.Clips[0] != null ? seData.Clips[0].length : 1f;
                _startOffsetSlider.highValue = Mathf.Max(0.01f, clipLength);
                _startOffsetSlider.SetValueWithoutNotify(seData.StartOffsetSec);
            }
        }

        // ── 波形 + 範囲ドラッグ ──────────────────────────────────────────────

        private AudioClip CurrentWaveformClip()
        {
            switch (_target)
            {
                case SeData se when se.Sources is { Length: > 0 } && _sourceIndex < se.Sources.Length && se.Sources[_sourceIndex].Source != null:
                    return se.Sources[_sourceIndex].Source;
                case SeData se when se.Clips is { Length: > 0 }:
                    return se.Clips[0];
                case BgmData bgm when bgm.LoopBody != null:
                    return bgm.LoopBody;
                default:
                    return null;
            }
        }

        private (float start, float end, float length) CurrentRange()
        {
            var clip = CurrentWaveformClip();
            var length = clip != null ? clip.length : 0f;

            switch (_target)
            {
                case SeData se when se.Sources is { Length: > 0 } && _sourceIndex < se.Sources.Length:
                {
                    var entry = se.Sources[_sourceIndex];
                    var end = entry.TrimEndSec > entry.TrimStartSec ? entry.TrimEndSec : length;
                    return (entry.TrimStartSec, end, length);
                }

                case BgmData bgm:
                {
                    var end = bgm.LoopEndSec > bgm.LoopStartSec ? (float)bgm.LoopEndSec : length;
                    return ((float)bgm.LoopStartSec, end, length);
                }

                default:
                    return (0f, length, length);
            }
        }

        private void SetRange(float start, float end)
        {
            switch (_target)
            {
                case SeData se when se.Sources is { Length: > 0 } && _sourceIndex < se.Sources.Length:
                {
                    Undo.RecordObject(se, "Edit Trim Range");
                    var entry = se.Sources[_sourceIndex];
                    entry.TrimStartSec = start;
                    entry.TrimEndSec = end;
                    se.Sources[_sourceIndex] = entry;
                    EditorUtility.SetDirty(se);
                    break;
                }

                case BgmData bgm:
                    Undo.RecordObject(bgm, "Edit Loop Range");
                    bgm.LoopStartSec = start;
                    bgm.LoopEndSec = end;
                    EditorUtility.SetDirty(bgm);
                    break;
            }
        }

        private void DrawWaveformGui()
        {
            var rect = GUILayoutUtility.GetRect(100, WaveformHeight, GUILayout.ExpandWidth(true));
            var clip = CurrentWaveformClip();

            if (clip == null)
            {
                EditorGUI.HelpBox(rect, "波形を表示する AudioClip がありません", MessageType.Info);
                return;
            }

            // Layout イベントでは GetRect がダミー矩形(幅1)を返すため、そのままテクスチャを作ると
            // 「幅8で作り直し→Repaintで実寸で作り直し」を毎サイクル繰り返してしまう(全PCM読み×2/フレーム)。
            // 実寸が確定している Repaint のときだけ生成・更新する。
            if (Event.current.type == EventType.Repaint)
            {
                EnsureWaveformTexture(clip, (int)rect.width);
            }

            if (_waveformTexture != null)
            {
                GUI.DrawTexture(rect, _waveformTexture, ScaleMode.StretchToFill);
            }

            var (start, end, length) = CurrentRange();
            if (length <= 0f)
            {
                return;
            }

            var startX = rect.x + rect.width * (start / length);
            var endX = rect.x + rect.width * (end / length);

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, startX - rect.x, rect.height), new Color(0f, 0f, 0f, 0.45f));
            EditorGUI.DrawRect(new Rect(endX, rect.y, rect.xMax - endX, rect.height), new Color(0f, 0f, 0f, 0.45f));
            EditorGUI.DrawRect(new Rect(startX - 1, rect.y, 2, rect.height), Color.cyan);
            EditorGUI.DrawRect(new Rect(endX - 1, rect.y, 2, rect.height), Color.magenta);

            var rangeLabel = _target is BgmData ? "ループ範囲" : "トリム範囲";
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, 300, 16), $"{rangeLabel}: {start:F3}s – {end:F3}s / {length:F3}s", EditorStyles.miniLabel);

            HandleWaveformMouse(rect, startX, endX, length);
        }

        private void HandleWaveformMouse(Rect rect, float startX, float endX, float length)
        {
            var evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition):
                    if (Mathf.Abs(evt.mousePosition.x - startX) <= HandleHitWidth)
                    {
                        _draggingEdge = 0;
                    }
                    else if (Mathf.Abs(evt.mousePosition.x - endX) <= HandleHitWidth)
                    {
                        _draggingEdge = 1;
                    }
                    else
                    {
                        // 範囲外クリック: 近い方のエッジを掴んでそこへ移動
                        _draggingEdge = Mathf.Abs(evt.mousePosition.x - startX) < Mathf.Abs(evt.mousePosition.x - endX) ? 0 : 1;
                        DragEdgeTo(rect, evt.mousePosition.x, length);
                    }

                    evt.Use();
                    break;

                case EventType.MouseDrag when _draggingEdge >= 0:
                    DragEdgeTo(rect, evt.mousePosition.x, length);
                    evt.Use();
                    break;

                case EventType.MouseUp when _draggingEdge >= 0:
                    _draggingEdge = -1;
                    evt.Use();
                    break;
            }
        }

        private void DragEdgeTo(Rect rect, float mouseX, float length)
        {
            var t = Mathf.Clamp01((mouseX - rect.x) / rect.width) * length;
            var (start, end, _) = CurrentRange();

            if (_draggingEdge == 0)
            {
                SetRange(Mathf.Min(t, end - 0.001f), end);
            }
            else
            {
                SetRange(start, Mathf.Max(t, start + 0.001f));
            }

            _waveformContainer.MarkDirtyRepaint();
        }

        private void EnsureWaveformTexture(AudioClip clip, int width)
        {
            if (_waveformTexture != null && _waveformClip == clip && _waveformTexture.width == Mathf.Max(8, width))
            {
                return;
            }

            DestroyWaveformTexture();
            _waveformTexture = WaveformRenderer.BuildTexture(
                clip, width, (int)WaveformHeight,
                new Color(0.13f, 0.13f, 0.13f), new Color(0.4f, 0.85f, 0.5f));
            _waveformClip = clip;
        }

        private void InvalidateWaveform()
        {
            DestroyWaveformTexture();
            _waveformClip = null;
            _waveformContainer?.MarkDirtyRepaint();
        }

        private void DestroyWaveformTexture()
        {
            if (_waveformTexture != null)
            {
                DestroyImmediate(_waveformTexture);
                _waveformTexture = null;
            }
        }

        // ── 試聴・トリム操作 ────────────────────────────────────────────────

        private void Play()
        {
            if (_target == null)
            {
                return;
            }

            _preview.Initialize();

            switch (_target)
            {
                case SeData se:
                    _lastHandle = _preview.PlaySe(se, forceLoop: _loopToggle.value);
                    _preview.AudioManager.SetVolume(_lastHandle, _volumeSlider.value);
                    _preview.AudioManager.SetPitch(_lastHandle, _pitchSlider.value);
                    ApplyListenerPadToPlayback();
                    break;

                case BgmData bgm:
                    _preview.PlayBgm(bgm);
                    break;
            }
        }

        private void Stop() => _preview?.StopAll();

        private void DetectSilence()
        {
            if (_target is not SeData se || se.Sources is not { Length: > 0 } || _sourceIndex >= se.Sources.Length)
            {
                return;
            }

            var entry = se.Sources[_sourceIndex];
            if (entry.Source == null)
            {
                return;
            }

            if (AudioClipTrimUtility.TryDetectSilenceTrim(entry.Source, 0.01f, out var start, out var end))
            {
                Undo.RecordObject(se, "Detect Silence Trim");
                entry.TrimStartSec = start;
                entry.TrimEndSec = end;
                se.Sources[_sourceIndex] = entry;
                EditorUtility.SetDirty(se);
                _waveformContainer.MarkDirtyRepaint();
            }
        }

        private void ApplyTrim()
        {
            if (_target is SeData se && SeTrimApplier.Apply(se))
            {
                InvalidateWaveform();
                RefreshTargetUi();
            }
        }

        // ── 3D サウンド確認パッド(ブレンドスペース風) ────────────────────────
        // 中央=音源(赤)、リスナー(青)をドラッグで移動。角度スライダで向きを回転。
        // 実際の適用は「リスナーから見た相対位置へ音源を動かす」ことで行う(ListenerPadMath 参照)。

        private void BuildListenerPadSection(VisualElement root)
        {
            _padSection = new VisualElement();
            _padSection.style.marginTop = 8;

            var header = new Label("3D サウンド確認");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            _padSection.Add(header);

            var rangeRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _rangeXField = new FloatField("横範囲(±m)") { value = _rangeX, tooltip = "パッドの横幅が表す距離。矩形の縮尺(横)" };
            _rangeXField.style.flexGrow = 1f;
            _rangeXField.RegisterValueChangedCallback(evt =>
            {
                _rangeX = Mathf.Max(0.5f, evt.newValue);
                _listenerPadPos = ListenerPadMath.ClampToRange(_listenerPadPos, _rangeX, _rangeY);
                _padContainer.MarkDirtyRepaint();
                ApplyListenerPadToPlayback();
            });
            rangeRow.Add(_rangeXField);

            _rangeYField = new FloatField("縦範囲(±m)") { value = _rangeY, tooltip = "パッドの縦幅が表す距離。矩形の縮尺(縦)" };
            _rangeYField.style.flexGrow = 1f;
            _rangeYField.RegisterValueChangedCallback(evt =>
            {
                _rangeY = Mathf.Max(0.5f, evt.newValue);
                _listenerPadPos = ListenerPadMath.ClampToRange(_listenerPadPos, _rangeX, _rangeY);
                _padContainer.MarkDirtyRepaint();
                ApplyListenerPadToPlayback();
            });
            rangeRow.Add(_rangeYField);
            _padSection.Add(rangeRow);

            _listenerAngleSlider = new Slider("リスナー角度", 0f, 360f)
            {
                value = _listenerAngleDeg,
                tooltip = "リスナーの向き(度)。0=奥向き、時計回り。ステレオの左右定位に反映される",
            };
            _listenerAngleSlider.RegisterValueChangedCallback(evt =>
            {
                _listenerAngleDeg = evt.newValue;
                _padContainer.MarkDirtyRepaint();
                ApplyListenerPadToPlayback();
            });
            _padSection.Add(_listenerAngleSlider);

            _padContainer = new IMGUIContainer(DrawListenerPad);
            _padContainer.style.height = PadHeight;
            _padContainer.style.marginTop = 2;
            _padSection.Add(_padContainer);

            root.Add(_padSection);
        }

        // パッドが有効か + 無効な場合の理由。設定不備でプレビューが「効いていないだけ」に
        // 見える事故を防ぐため、無効時は理由を明示する。
        private (bool valid, string reason) PadValidity()
        {
            switch (_target)
            {
                case null:
                    return (false, "対象アセットが選択されていません。");

                case BgmData:
                    return (false, "BGM は常に 2D 再生(位置の影響なし)のため、\nこのパッドでの確認対象外です。");

                case SeData { Spatial: SpatialMode.None }:
                    return (false, "この SE は Spatial = None(2D 設定)のため、\n位置・距離・角度は音に影響しません。\n3D で確認するには Spatial を Anchor か AtPosition に\n変更してください。");

                case SeData se when se.Clips == null || se.Clips.Length == 0 || se.Clips[0] == null:
                    return (false, "Clips が未設定のため再生できません。\nAudioClip を設定(またはトリミングを適用)してください。");

                default:
                    return (true, null);
            }
        }

        private void DrawListenerPad()
        {
            var outerRect = GUILayoutUtility.GetRect(100, PadHeight, GUILayout.ExpandWidth(true));
            var (valid, reason) = PadValidity();

            EditorGUI.DrawRect(outerRect, new Color(0.16f, 0.16f, 0.16f));

            // MaxDistance が現在の縮尺(Range)より大きいと円がパッドからはみ出す。以降の描画は
            // パッド矩形でクリップする(BeginGroup はマウス座標もローカル原点へ変換してくれる)。
            GUI.BeginGroup(outerRect);
            var rect = new Rect(0f, 0f, outerRect.width, outerRect.height);

            // 十字グリッド
            EditorGUI.DrawRect(new Rect(rect.x, rect.center.y - 0.5f, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            EditorGUI.DrawRect(new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.08f));

            if (!valid)
            {
                // 無効: パッドを暗転し、理由を中央に表示。操作も受け付けない。
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.55f));

                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    normal = { textColor = new Color(1f, 0.75f, 0.4f) },
                };
                GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 20f, rect.height), $"⚠ 3D プレビュー無効\n\n{reason}", style);
                GUI.EndGroup();
                return;
            }

            var se = (SeData)_target;

            // Min/MaxDistance の円(縮尺が縦横で違う場合は楕円になる)
            DrawPadEllipse(rect, se.MinDistance, new Color(0.3f, 0.9f, 0.4f, 0.55f));
            DrawPadEllipse(rect, se.MaxDistance, new Color(0.9f, 0.5f, 0.2f, 0.55f));

            // 音源(赤点、中央固定)
            var sourcePx = rect.center;
            EditorGUI.DrawRect(new Rect(sourcePx.x - 4f, sourcePx.y - 4f, 8f, 8f), new Color(0.95f, 0.25f, 0.2f));

            // リスナー(青点 + 向きの線)
            var listenerPx = ListenerPadMath.MetersToPixel(_listenerPadPos, rect, _rangeX, _rangeY);
            var rad = _listenerAngleDeg * Mathf.Deg2Rad;
            var dirPx = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * 14f; // GUI 座標は下+なので Y 反転
            Handles.color = new Color(0.35f, 0.65f, 1f);
            Handles.DrawAAPolyLine(3f, new Vector3(listenerPx.x, listenerPx.y, 0f), new Vector3(listenerPx.x + dirPx.x, listenerPx.y + dirPx.y, 0f));
            EditorGUI.DrawRect(new Rect(listenerPx.x - 5f, listenerPx.y - 5f, 10f, 10f), new Color(0.35f, 0.65f, 1f));

            // 情報表示
            var distance = ListenerPadMath.DistanceToSource(_listenerPadPos);
            GUI.Label(
                new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f),
                $"距離: {distance:F2}m   角度: {_listenerAngleDeg:F0}°   位置: ({_listenerPadPos.x:F1}, {_listenerPadPos.y:F1})m",
                EditorStyles.miniLabel);

            HandlePadMouse(rect);

            GUI.EndGroup();
        }

        private void DrawPadEllipse(Rect rect, float radiusMeters, Color color)
        {
            var rx = radiusMeters / _rangeX * (rect.width * 0.5f);
            var ry = radiusMeters / _rangeY * (rect.height * 0.5f);

            const int segments = 48;
            var points = new Vector3[segments + 1];
            for (var i = 0; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                points[i] = new Vector3(rect.center.x + Mathf.Cos(a) * rx, rect.center.y + Mathf.Sin(a) * ry, 0f);
            }

            Handles.color = color;
            Handles.DrawAAPolyLine(1.5f, points);
        }

        private void HandlePadMouse(Rect rect)
        {
            var evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition):
                    _draggingListener = true;
                    MoveListenerTo(rect, evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseDrag when _draggingListener:
                    MoveListenerTo(rect, evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseUp when _draggingListener:
                    _draggingListener = false;
                    evt.Use();
                    break;
            }
        }

        private void MoveListenerTo(Rect rect, Vector2 mousePx)
        {
            var meters = ListenerPadMath.PixelToMeters(mousePx, rect, _rangeX, _rangeY);
            _listenerPadPos = ListenerPadMath.ClampToRange(meters, _rangeX, _rangeY);
            _padContainer.MarkDirtyRepaint();
            ApplyListenerPadToPlayback();
        }

        // 再生中の音源をパッドの相対位置へ即時追従させる(距離減衰・パンをその場で試聴)。
        private void ApplyListenerPadToPlayback()
        {
            if (_target is not SeData { Spatial: not SpatialMode.None } ||
                _preview?.AudioManager == null || !_preview.AudioManager.IsPlaying(_lastHandle) ||
                _preview.ListenerTransform == null)
            {
                return;
            }

            var localOffset = ListenerPadMath.SourceOffsetInListenerLocal(_listenerPadPos, _listenerAngleDeg);
            var worldPos = _preview.ListenerTransform.TransformPoint(localOffset);
            _preview.AudioManager.Move(_lastHandle, worldPos);
        }
    }
}
