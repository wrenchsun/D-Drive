using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.Audio;
using DDrive.Foundation.Data;
using DDrive.Foundation.Event;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // [05_model_animation.md] B-4 — ITAMI の「SE タブ」から取り込んだ使い勝手(2026-09-10):
    //  1. Animator から選択: 対象 Animator の Controller をスキャンしてステート / クリップを選ぶと、対応する AnimData を
    //     自動で探す(Clip 一致 > StateName+Layer 一致)。無ければその場で作る(AssetBrowser の作成パイプライン)
    //  2. SE 波形: タイムラインの SE マーカーから SE の長さぶん波形を重ねる(DrawSeWaveforms、本体の DrawTimeline から呼ぶ)
    //  3. SE / VFX イベント一覧: 「＋ 現在位置に…」、行ごとのアセット選択 / ▶ 試聴 / ✕ / 秒とフレーム
    //  4. イベントのコピー: 別の AnimData へ、または同じステートの他クリップの AnimData へ一括
    // イベントの保存先は従来どおり AnimData.Events(クリップに AnimationEvent は書かない)。
    public sealed partial class AnimEditorWindow
    {
        private sealed class ClipChoice
        {
            public int Layer;
            public string LayerName;
            public string StateName;
            public AnimationClip Clip;
            public string Label;
        }

        private sealed class AssetChoice
        {
            public AssetType Type;
            public AssetDataBase Asset;
            public string Label;
        }

        private readonly List<ClipChoice> _clipChoices = new();
        private readonly List<AssetChoice> _assetChoices = new();
        private readonly Dictionary<ulong, SeData> _seById = new();
        private int _selectedClipChoice = -1;

        private VisualElement _sourceSection;
        private DropdownField _stateDropdown;
        private Label _sourceStatusLabel;
        private Button _createFromStateButton;
        private VisualElement _eventRowsContainer;
        private Label _eventHint;
        private ObjectField _copyTargetField;
        private Button _copyToStateClipsButton;

        // ── 1. Animator から選択 ──

        private void BuildSourceSection(VisualElement root)
        {
            _sourceSection = new Foldout { text = "Animator から選択(ステートを選ぶと AnimData を探す / 作る)", value = true };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            _stateDropdown = new DropdownField("ステート / クリップ") { style = { flexGrow = 1f } };
            _stateDropdown.RegisterValueChangedCallback(evt => OnClipChoiceChanged(_stateDropdown.index));
            row.Add(_stateDropdown);
            row.Add(new Button(() => RefreshSourceStates(_scene?.Current != null ? _scene.Current : _sceneTarget)) { text = "再スキャン" });
            _sourceSection.Add(row);
            _sourceStatusLabel = new Label { style = { opacity = 0.75f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            _sourceSection.Add(_sourceStatusLabel);
            _createFromStateButton = new Button(CreateAnimDataFromChoice) { text = "このクリップの AnimData を作成(ID 発行・カタログ・Addressables 登録)" };
            _createFromStateButton.style.display = DisplayStyle.None;
            _sourceSection.Add(_createFromStateButton);
            root.Add(_sourceSection);
        }

        // 対象 Animator が変わったとき(RefreshModelInfo から)。Controller のステート → クリップを列挙する。
        private void RefreshSourceStates(Animator animator)
        {
            if (_stateDropdown == null)
            {
                return;
            }

            _clipChoices.Clear();
            _selectedClipChoice = -1;
            var controller = animator != null ? ResolveController(animator.runtimeAnimatorController) : null;
            if (controller != null)
            {
                for (var li = 0; li < controller.layers.Length; li++)
                {
                    var layer = controller.layers[li];
                    if (layer.stateMachine == null)
                    {
                        continue;
                    }

                    foreach (var child in layer.stateMachine.states)
                    {
                        var clips = new List<AnimationClip>();
                        CollectClips(child.state.motion, clips);
                        foreach (var clip in clips)
                        {
                            var label = controller.layers.Length > 1 ? $"{layer.name} / {child.state.name}" : child.state.name;
                            if (clips.Count > 1)
                            {
                                label += $"  [{clip.name}]";
                            }

                            _clipChoices.Add(new ClipChoice { Layer = li, LayerName = layer.name, StateName = child.state.name, Clip = clip, Label = label });
                        }
                    }
                }
            }

            var labels = new List<string>();
            foreach (var c in _clipChoices)
            {
                labels.Add(c.Label);
            }

            _stateDropdown.choices = labels;
            _stateDropdown.SetValueWithoutNotify(string.Empty);
            _stateDropdown.SetEnabled(labels.Count > 0);
            _createFromStateButton.style.display = DisplayStyle.None;
            _sourceStatusLabel.text = animator == null
                ? "対象の Animator を決めると、その Controller のステートから選べます。"
                : controller == null
                    ? "Controller が無い(または OverrideController 以外に解決できない)ため一覧を出せません。"
                    : labels.Count == 0 ? "クリップを持つステートがありません。" : $"{labels.Count} クリップ。選ぶと対応する AnimData を探します。";

            // 現在の対象 AnimData に対応する項目があれば選択状態にする(表示だけ)。
            if (_target != null && _target.Clip != null)
            {
                for (var i = 0; i < _clipChoices.Count; i++)
                {
                    if (_clipChoices[i].Clip == _target.Clip)
                    {
                        _selectedClipChoice = i;
                        _stateDropdown.SetValueWithoutNotify(_clipChoices[i].Label);
                        break;
                    }
                }
            }
        }

        private static void CollectClips(Motion motion, List<AnimationClip> into)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    if (!into.Contains(clip))
                    {
                        into.Add(clip);
                    }

                    break;
                case BlendTree tree:
                    foreach (var child in tree.children)
                    {
                        CollectClips(child.motion, into);
                    }

                    break;
            }
        }

        private void OnClipChoiceChanged(int index)
        {
            if (index < 0 || index >= _clipChoices.Count)
            {
                return;
            }

            _selectedClipChoice = index;
            var choice = _clipChoices[index];
            var found = FindAnimDataFor(choice);
            if (found != null)
            {
                _createFromStateButton.style.display = DisplayStyle.None;
                _sourceStatusLabel.text = $"'{found.DisplayName ?? found.name}' を対象にしました({AssetDatabase.GetAssetPath(found)})。";
                if (found != _target)
                {
                    SetTarget(found);
                }
            }
            else
            {
                _createFromStateButton.style.display = DisplayStyle.Flex;
                _sourceStatusLabel.text = $"クリップ '{choice.Clip.name}'(ステート '{choice.StateName}')に対応する AnimData がありません。下のボタンで作れます。";
            }
        }

        // Clip 一致 > StateName + Layer 一致 の順で探す。
        private static AnimData FindAnimDataFor(ClipChoice choice)
        {
            AnimData byState = null;
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AnimData)))
            {
                var data = AssetDatabase.LoadAssetAtPath<AnimData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null)
                {
                    continue;
                }

                if (data.Clip == choice.Clip)
                {
                    return data;
                }

                if (byState == null && data.Layer == choice.Layer && data.ResolvedStateName == choice.StateName)
                {
                    byState = data;
                }
            }

            return byState;
        }

        private void CreateAnimDataFromChoice()
        {
            if (_selectedClipChoice < 0 || _selectedClipChoice >= _clipChoices.Count)
            {
                return;
            }

            var created = CreateAnimDataFor(_clipChoices[_selectedClipChoice]);
            if (created != null)
            {
                _createFromStateButton.style.display = DisplayStyle.None;
                _sourceStatusLabel.text = $"作成しました: {AssetDatabase.GetAssetPath(created)}";
                SetTarget(created);
                RefreshAssetChoices();
            }
        }

        private AnimData CreateAnimDataFor(ClipChoice choice)
        {
            var animator = _scene?.Current != null ? _scene.Current : _sceneTarget;
            var category = ToIdentifier(animator != null ? animator.gameObject.name : "Anim", "Model");
            var identifier = ToIdentifier(choice.StateName, "State");
            if (choice.Clip != null && !string.Equals(choice.Clip.name, choice.StateName, StringComparison.OrdinalIgnoreCase))
            {
                identifier += ToIdentifier(choice.Clip.name, string.Empty);
            }

            var asset = AssetCreationService.Create(typeof(AnimData), AssetType.Anim, choice.StateName, category, identifier, data =>
            {
                var anim = (AnimData)data;
                anim.Clip = choice.Clip;
                anim.StateName = choice.StateName;
                anim.Layer = choice.Layer;
                anim.Loop = choice.Clip != null && choice.Clip.isLooping;
            });
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                AppendLog($"AnimData を作成: {asset.name}");
            }

            return asset as AnimData;
        }

        private static string ToIdentifier(string name, string fallback) => AssetNamingService.ToIdentifier(name, fallback);

        // ── 2. SE 波形(本体の DrawTimeline から) ──

        // 各 SE マーカーの位置から、その SE の長さぶん波形を半透明で重ねる(どこまで鳴るか・重なりが見える)。
        private void DrawSeWaveforms(Rect bar, float length)
        {
            if (_target?.Events == null || length <= 0f)
            {
                return;
            }

            var frameRate = _target.FrameRate;
            foreach (var e in _target.Events)
            {
                if (e.Action != EventAction.PlayAsset || !e.Target.IsAssigned || e.Target.Type != AssetType.Se)
                {
                    continue;
                }

                float sec;
                switch (e.Trigger)
                {
                    case EventTrigger.Frame: sec = e.Time / frameRate; break;
                    case EventTrigger.Time: sec = e.Time; break;
                    default: continue;
                }

                if (!_seById.TryGetValue(e.Target.Id, out var se) || se == null)
                {
                    continue;
                }

                var clip = FirstClip(se);
                var wave = WaveformTextureCache.Get(clip);
                if (clip == null || wave == null)
                {
                    continue;
                }

                var startU = Mathf.Clamp01(sec / length);
                var endU = Mathf.Clamp01((sec + clip.length) / length);
                var rect = new Rect(bar.x + bar.width * startU, bar.y - 10f, Mathf.Max(2f, bar.width * (endU - startU)), 28f);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(rect, wave, ScaleMode.StretchToFill, true);
                GUI.color = prev;
            }
        }

        private static AudioClip FirstClip(SeData se)
        {
            if (se.Clips != null)
            {
                foreach (var c in se.Clips)
                {
                    if (c != null)
                    {
                        return c;
                    }
                }
            }

            if (se.Sources != null)
            {
                foreach (var s in se.Sources)
                {
                    if (s.Source != null)
                    {
                        return s.Source;
                    }
                }
            }

            return null;
        }

        // ── 3. SE / VFX イベント一覧 ──

        private void BuildEventListSection(VisualElement root)
        {
            var foldout = new Foldout { text = "SE / VFX イベント(PlayAsset)", value = true };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            row.Add(new Button(() => AddEventAtPlayhead(AssetType.Se)) { text = "＋ 現在位置に SE" });
            row.Add(new Button(() => AddEventAtPlayhead(AssetType.Vfx)) { text = "＋ 現在位置に VFX" });
            row.Add(new Button(() => AddEventAtPlayhead(AssetType.AnchorGroup)) { text = "＋ 現在位置に配置セット" });
            row.Add(new Button(() => { RefreshAssetChoices(); RefreshEventList(); }) { text = "一覧を更新", tooltip = "SE / VFX アセットを追加・改名したあとに押す" });
            foldout.Add(row);
            _eventHint = new Label { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } };
            foldout.Add(_eventHint);
            _eventRowsContainer = new VisualElement();
            foldout.Add(_eventRowsContainer);

            // 4. コピー
            var copyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 6 } };
            _copyTargetField = new ObjectField("イベントを別の AnimData へコピー") { objectType = typeof(AnimData), style = { flexGrow = 1f } };
            copyRow.Add(_copyTargetField);
            copyRow.Add(new Button(CopyEventsToField) { text = "コピー" });
            foldout.Add(copyRow);
            _copyToStateClipsButton = new Button(CopyEventsToStateClips) { text = "同じステートの他クリップの AnimData へ一括コピー(無ければ作成)" };
            foldout.Add(_copyToStateClipsButton);
            root.Add(foldout);
        }

        // プロジェクト内の SE / VFX / 配置セットを列挙(ドロップダウン用)。
        private void RefreshAssetChoices()
        {
            _assetChoices.Clear();
            _seById.Clear();
            Collect<SeData>(AssetType.Se, "SE");
            Collect<VfxData>(AssetType.Vfx, "VFX");
            Collect<AnchorGroupData>(AssetType.AnchorGroup, "配置セット");
        }

        private void Collect<T>(AssetType type, string prefix) where T : AssetDataBase
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null || asset.Id == 0)
                {
                    continue;
                }

                _assetChoices.Add(new AssetChoice { Type = type, Asset = asset, Label = $"{prefix}: {asset.DisplayName ?? asset.name}" });
                if (asset is SeData se)
                {
                    _seById[se.Id] = se;
                }
            }

            _assetChoices.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        }

        private void RefreshEventList()
        {
            if (_eventRowsContainer == null)
            {
                return;
            }

            _eventRowsContainer.Clear();
            if (_target == null)
            {
                _eventHint.text = "対象アセットを選ぶと、SE / VFX のイベントをここで編集できます。";
                return;
            }

            if (_assetChoices.Count == 0)
            {
                RefreshAssetChoices();
            }

            var events = _target.Events ?? Array.Empty<AssetEvent>();
            var shown = 0;
            var labels = new List<string> { "(未設定)" };
            foreach (var c in _assetChoices)
            {
                labels.Add(c.Label);
            }

            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Action != EventAction.PlayAsset)
                {
                    continue;
                }

                shown++;
                _eventRowsContainer.Add(BuildEventRow(i, events[i], labels));
            }

            _eventHint.text = shown == 0
                ? "「＋ 現在位置に SE / VFX」で追加。再生ヘッドの位置(フレーム)にイベントが入ります。PlayAsset 以外のイベントは下の「設定」の Events で編集します。"
                : "行の時刻はタイムラインのマーカーをドラッグしても変えられます。▶ で単体試聴(SE / VFX はシーン上の対象の位置に出ます)。";
        }

        private VisualElement BuildEventRow(int index, AssetEvent e, List<string> labels)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            row.Add(new Label($"#{index + 1}") { style = { width = 32 } });

            var dropdown = new DropdownField { choices = labels, style = { flexGrow = 1f, minWidth = 140 } };
            dropdown.SetValueWithoutNotify(LabelFor(e.Target, labels));
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choiceIndex = dropdown.index - 1;
                ModifyEvent(index, "Set Event Target", ref e, (ref AssetEvent ev) =>
                {
                    ev.Target = choiceIndex >= 0 && choiceIndex < _assetChoices.Count ? ToRef(_assetChoices[choiceIndex]) : default;
                });
            });
            row.Add(dropdown);

            var trigger = new EnumField(e.Trigger == EventTrigger.Time ? EventTrigger.Time : EventTrigger.Frame) { style = { width = 70 } };
            trigger.RegisterValueChangedCallback(evt =>
            {
                var next = (EventTrigger)evt.newValue;
                ModifyEvent(index, "Set Event Trigger", ref e, (ref AssetEvent ev) =>
                {
                    var frameRate = _target.FrameRate;
                    if (ev.Trigger == EventTrigger.Frame && next == EventTrigger.Time) ev.Time = ev.Time / frameRate;
                    else if (ev.Trigger == EventTrigger.Time && next == EventTrigger.Frame) ev.Time = Mathf.Round(ev.Time * frameRate);
                    ev.Trigger = next;
                });
                RefreshEventList();
            });
            row.Add(trigger);

            if (e.Trigger == EventTrigger.Time)
            {
                var sec = new FloatField { value = e.Time, style = { width = 60 } };
                sec.RegisterValueChangedCallback(evt => ModifyEvent(index, "Set Event Time", ref e, (ref AssetEvent ev) => ev.Time = Mathf.Max(0f, evt.newValue)));
                row.Add(sec);
                row.Add(new Label("秒") { style = { marginRight = 4 } });
            }
            else
            {
                var frame = new IntegerField { value = Mathf.RoundToInt(e.Time), style = { width = 50 } };
                frame.RegisterValueChangedCallback(evt => ModifyEvent(index, "Set Event Frame", ref e, (ref AssetEvent ev) => ev.Time = Mathf.Max(0, evt.newValue)));
                row.Add(frame);
                var secLabel = new Label($"F = {e.Time / _target.FrameRate:0.00}s") { style = { width = 70, opacity = 0.7f } };
                row.Add(secLabel);
            }

            var repeat = new DropdownField { choices = RepeatLabels, style = { width = 92 }, tooltip = "繰り返し: 毎周回 = ループのたびに出す / 1 回 = 再生ごとに 1 回 / 再生中は維持 = 1 回出して終了・中断で止める(ダッシュの土煙などループする追従エフェクト向け)" };
            repeat.SetValueWithoutNotify(RepeatLabels[Mathf.Clamp((int)e.Repeat, 0, RepeatLabels.Count - 1)]);
            repeat.RegisterValueChangedCallback(evt =>
            {
                var next = (EventRepeat)Mathf.Max(0, repeat.index);
                ModifyEvent(index, "Set Event Repeat", ref e, (ref AssetEvent ev) => ev.Repeat = next);
            });
            row.Add(repeat);

            row.Add(new Button(() => PreviewEvent(e)) { text = "▶", tooltip = "この SE / VFX だけ試聴・試し出し" });
            var open = new Button(() => OpenEventAssetEditor(e)) { text = "↗" };
            open.tooltip = DescribeEditorFor(e);
            open.SetEnabled(e.Target.IsAssigned);
            row.Add(open);
            row.Add(new Button(() => RemoveEvent(index)) { text = "✕", tooltip = "このイベントを削除" });
            return row;
        }

        private static readonly List<string> RepeatLabels = new() { "毎周回", "1 回", "再生中は維持" };

        // ↗: そのアセットを専用エディタで開く(DataEditorRegistry の対応表)。
        private void OpenEventAssetEditor(AssetEvent e)
        {
            var asset = FindAsset(e.Target);
            if (asset == null)
            {
                AppendLog("⚠ 対象のアセットが見つかりません(「一覧を更新」を押してください)");
                return;
            }

            var entries = DDrive.Editor.Inspector.DataEditorRegistry.GetEntries(asset.GetType());
            if (entries.Count == 0)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
                return;
            }

            entries[0].Open(asset);
        }

        private string DescribeEditorFor(AssetEvent e)
        {
            var asset = FindAsset(e.Target);
            if (asset == null)
            {
                return "アセット未設定";
            }

            var entries = DDrive.Editor.Inspector.DataEditorRegistry.GetEntries(asset.GetType());
            return entries.Count > 0 ? $"'{asset.DisplayName ?? asset.name}' を {entries[0].WindowType.Name} で開く" : $"'{asset.name}' を Project で選択";
        }

        private AssetDataBase FindAsset(AssetRef target)
        {
            if (!target.IsAssigned)
            {
                return null;
            }

            foreach (var c in _assetChoices)
            {
                if (c.Type == target.Type && c.Asset.Id == target.Id)
                {
                    return c.Asset;
                }
            }

            return null;
        }

        private delegate void EventMutator(ref AssetEvent ev);

        private void ModifyEvent(int index, string undoName, ref AssetEvent local, EventMutator mutate)
        {
            if (_target?.Events == null || index < 0 || index >= _target.Events.Length)
            {
                return;
            }

            Undo.RecordObject(_target, undoName);
            var ev = _target.Events[index];
            mutate(ref ev);
            _target.Events[index] = ev;
            local = ev;
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            _timelineContainer?.MarkDirtyRepaint();
            RefreshValidation();
        }

        private void AddEventAtPlayhead(AssetType type)
        {
            if (_target == null)
            {
                return;
            }

            if (_assetChoices.Count == 0)
            {
                RefreshAssetChoices();
            }

            var normalized = _scene != null ? _scene.Manager.GetNormalizedTime(_animHandle) : -1f;
            if (normalized < 0f)
            {
                normalized = 0f;
            }

            AssetChoice first = null;
            foreach (var c in _assetChoices)
            {
                if (c.Type == type)
                {
                    first = c;
                    break;
                }
            }

            var ev = new AssetEvent
            {
                Trigger = EventTrigger.Frame,
                Time = Mathf.Round(normalized * _target.LengthSec * _target.FrameRate),
                Action = EventAction.PlayAsset,
                Target = first != null ? ToRef(first) : default,
            };

            Undo.RecordObject(_target, "Add Anim Event");
            var list = new List<AssetEvent>(_target.Events ?? Array.Empty<AssetEvent>()) { ev };
            _target.Events = list.ToArray();
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            _timelineContainer?.MarkDirtyRepaint();
            RefreshEventList();
            RefreshValidation();
        }

        private void RemoveEvent(int index)
        {
            if (_target?.Events == null || index < 0 || index >= _target.Events.Length)
            {
                return;
            }

            Undo.RecordObject(_target, "Remove Anim Event");
            var list = new List<AssetEvent>(_target.Events);
            list.RemoveAt(index);
            _target.Events = list.ToArray();
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            _timelineContainer?.MarkDirtyRepaint();
            RefreshEventList();
            RefreshValidation();
        }

        private void PreviewEvent(AssetEvent e)
        {
            if (_scene == null || !e.Target.IsAssigned)
            {
                return;
            }

            foreach (var c in _assetChoices)
            {
                if (c.Type != e.Target.Type || c.Asset.Id != e.Target.Id)
                {
                    continue;
                }

                switch (c.Asset)
                {
                    case SeData se:
                        _scene.PreviewSe(se);
                        break;
                    case VfxData vfx:
                        _scene.PreviewVfx(vfx);
                        break;
                    case AnchorGroupData group:
                        _scene.PreviewGroup(group);
                        break;
                }

                AppendLog($"▶ 試聴: {c.Label}");
                return;
            }

            AppendLog("⚠ 対象のアセットが見つかりません(「一覧を更新」を押してください)");
        }

        private static AssetRef ToRef(AssetChoice choice) => choice.Type switch
        {
            AssetType.Se => AssetRef.From(new AssetId<SeMarker>(choice.Asset.Id, AssetType.Se)),
            AssetType.Vfx => AssetRef.From(new AssetId<VfxMarker>(choice.Asset.Id, AssetType.Vfx)),
            AssetType.AnchorGroup => AssetRef.From(new AssetId<AnchorGroupMarker>(choice.Asset.Id, AssetType.AnchorGroup)),
            _ => default,
        };

        private string LabelFor(AssetRef target, List<string> labels)
        {
            if (!target.IsAssigned)
            {
                return labels[0];
            }

            for (var i = 0; i < _assetChoices.Count; i++)
            {
                if (_assetChoices[i].Type == target.Type && _assetChoices[i].Asset.Id == target.Id)
                {
                    return labels[i + 1];
                }
            }

            return labels[0];
        }

        // ── 4. コピー ──

        private void CopyEventsToField()
        {
            var dest = _copyTargetField?.value as AnimData;
            if (_target == null || dest == null || dest == _target)
            {
                AppendLog("⚠ コピー先の AnimData を選んでください(対象自身は不可)");
                return;
            }

            CopyEvents(_target, dest);
            AppendLog($"イベントを '{dest.DisplayName ?? dest.name}' へコピー({_target.Events?.Length ?? 0} 件)");
        }

        private void CopyEventsToStateClips()
        {
            if (_target == null || _selectedClipChoice < 0 || _selectedClipChoice >= _clipChoices.Count)
            {
                AppendLog("⚠ 「Animator から選択」でステートを選んでから使ってください");
                return;
            }

            var current = _clipChoices[_selectedClipChoice];
            var copied = 0;
            foreach (var choice in _clipChoices)
            {
                if (choice == current || choice.Layer != current.Layer || choice.StateName != current.StateName || choice.Clip == current.Clip)
                {
                    continue;
                }

                var dest = FindAnimDataFor(choice) ?? CreateAnimDataFor(choice);
                if (dest == null || dest == _target)
                {
                    continue;
                }

                CopyEvents(_target, dest);
                copied++;
            }

            AppendLog(copied == 0 ? "⚠ 同じステートに他のクリップがありません" : $"同じステートの {copied} クリップへコピーしました");
            RefreshAssetChoices();
        }

        private static void CopyEvents(AnimData from, AnimData to)
        {
            Undo.RecordObject(to, "Copy Anim Events");
            to.Events = from.Events != null ? (AssetEvent[])from.Events.Clone() : Array.Empty<AssetEvent>();
            EditorUtility.SetDirty(to);
            AssetDatabase.SaveAssetIfDirty(to);
        }
    }
}
