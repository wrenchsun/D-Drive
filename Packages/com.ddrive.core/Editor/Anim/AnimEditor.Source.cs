using System;
using System.Collections.Generic;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Ux;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // 「Animator のステートから選ぶ」の欄(docs/1010 UX-6 / D-10)。
    // 対象の Animator の Controller のステート / クリップを 1 件 = 1 行で並べ(ステート名・クリップ名・対応する AnimData の有無・選ぶ / 作成)、
    // 選ぶと対応する AnimData(Clip 一致 > StateName + Layer 一致)を対象にする。無ければその場で作る(AssetBrowser の作成パイプライン)。
    // 既定は折りたたみ(見出しに「12 ステート・AnimData あり 5」)。一覧の上に絞り込み(検索・AnimData あり / なし)と件数。20 行を超えたら ListView。
    internal sealed partial class AnimEditor
    {
        private sealed class ClipChoice
        {
            public int Layer;
            public string LayerName;
            public string StateName;
            public AnimationClip Clip;
            public string Label;
        }

        private readonly List<ClipChoice> _clipChoices = new();
        private readonly Dictionary<AnimationClip, AnimData> _animByClip = new();
        private readonly Dictionary<(int, string), AnimData> _animByState = new();
        private readonly Dictionary<string, bool> _foldOpen = new();
        private CanvasItemList _stateList;
        private CanvasSectionView _stateSection;
        private Label _sourceStatusLabel;
        private string _sourceStatus = string.Empty;

        private UxRow CreateStatesRow(FieldGuideEntry e)
        {
            RefreshStateChoices();
            _stateList = new CanvasItemList("ステート", "AnimData あり", null, "対象の Animator が決まると、その Controller のステートがここに並びます。", "なし");
            _stateSection = new CanvasSectionView("states", _foldOpen, StatesTitle, StatesSignature, _ => _stateList.SetItems(CollectStateItems()));

            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            line.Add(MakeButton("再スキャン", null, RefreshSceneUi, "対象の Animator の Controller を読み直し、AnimData の有無も数え直す"));
            _stateSection.Static.Add(line);
            _sourceStatusLabel = new Label(_sourceStatus);
            _sourceStatusLabel.AddToClassList("ux-dim");
            _sourceStatusLabel.AddToClassList("ux-note");
            _stateSection.Static.Add(_sourceStatusLabel);
            _stateSection.Static.Add(_stateList);
            _stateSection.Rebuild();

            var row = new AnimAuxRow(e, e.Label, e.Hint, e.Tier, _stateSection, _stateSection.Sync);
            row.ExtraMatch = q => _stateList != null && _stateList.AnyMatch(q);
            return row;
        }

        private string StatesTitle()
        {
            var found = 0;
            for (var i = 0; i < _clipChoices.Count; i++)
            {
                if (FindAnimDataFor(_clipChoices[i]) != null)
                {
                    found++;
                }
            }

            return _clipChoices.Count == 0 ? "ステートなし(対象の Animator を決めると並びます)" : $"{_clipChoices.Count} ステート・AnimData あり {found}";
        }

        private int StatesSignature()
        {
            var h = CanvasSig.Add(17, _clipChoices.Count);
            h = CanvasSig.Add(h, Target != null && Target.Clip != null ? Target.Clip.GetInstanceID() : 0);
            h = CanvasSig.Add(h, _animByClip.Count);
            return CanvasSig.Add(h, _animByState.Count);
        }

        private List<CanvasListItem> CollectStateItems()
        {
            var items = new List<CanvasListItem>();
            for (var i = 0; i < _clipChoices.Count; i++)
            {
                var index = i;
                var c = _clipChoices[i];
                var found = FindAnimDataFor(c);
                items.Add(new CanvasListItem
                {
                    Key = "state:" + i,
                    Title = c.Label,
                    Configured = found != null,
                    Search = (c.Clip != null ? c.Clip.name : string.Empty) + " " + (found != null ? (found.DisplayName ?? found.name) : string.Empty),
                    Make = () => MakeStateRow(index),
                });
            }

            return items;
        }

        private VisualElement MakeStateRow(int index)
        {
            var c = _clipChoices[index];
            var found = FindAnimDataFor(c);
            var current = Target != null && found == Target;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--hl", current);

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var chip = new Label(found != null ? (current ? "対象" : "AnimData") : "なし") { tooltip = found != null ? "対応する AnimData があります" : "対応する AnimData がありません(「作成」で作れます)" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList(found != null ? "ux-kind--1" : "ux-kind--5");
            head.Add(chip);

            var name = new Label(c.Label) { tooltip = c.Label };
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);

            var summary = new Label((c.Clip != null ? c.Clip.name : "(Clip なし)") + (found != null ? " · " + (found.DisplayName ?? found.name) : string.Empty));
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);

            var pick = new Button(() => OnClipChoiceChosen(index)) { text = found != null ? "選ぶ" : "作成", tooltip = found != null ? "この AnimData を対象にする" : "このクリップの AnimData を作成する(ID 発行・カタログ・Addressables 登録)して対象にする" };
            pick.AddToClassList("ux-btn");
            pick.SetEnabled(!current);
            head.Add(pick);
            row.Add(head);
            return row;
        }

        // ── 対象 Animator の Controller を読む ──

        // 対象 Animator が変わったとき(RefreshSceneUi から)。Controller のステート → クリップを列挙し、AnimData の索引も取り直す。
        private void RefreshStateChoices()
        {
            var animator = CurrentAnimator;
            _clipChoices.Clear();
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

            BuildAnimDataIndex();
            _sourceStatus = animator == null
                ? "対象の Animator を決めると、その Controller のステートから選べます。"
                : controller == null
                    ? "Controller が無い(または OverrideController 以外に解決できない)ため一覧を出せません。"
                    : _clipChoices.Count == 0 ? "クリップを持つステートがありません。" : $"{_clipChoices.Count} クリップ。選ぶと対応する AnimData を探します。";
            if (_sourceStatusLabel != null)
            {
                _sourceStatusLabel.text = _sourceStatus;
            }

            _stateSection?.Rebuild();
        }

        // AnimData の索引(Clip → AnimData、(Layer, ステート名) → AnimData)。行ごとにプロジェクトを検索しないよう、1 回だけ作る。
        private void BuildAnimDataIndex()
        {
            _animByClip.Clear();
            _animByState.Clear();
            if (_clipChoices.Count == 0)
            {
                return;
            }

            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(AnimData)))
            {
                var data = AssetDatabase.LoadAssetAtPath<AnimData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data is Anim2DData)
                {
                    continue;
                }

                if (data.Clip != null && !_animByClip.ContainsKey(data.Clip))
                {
                    _animByClip[data.Clip] = data;
                }

                var key = (data.Layer, data.ResolvedStateName);
                if (!_animByState.ContainsKey(key))
                {
                    _animByState[key] = data;
                }
            }
        }

        // Clip 一致 > StateName + Layer 一致 の順で探す。
        private AnimData FindAnimDataFor(ClipChoice choice)
        {
            if (choice.Clip != null && _animByClip.TryGetValue(choice.Clip, out var byClip))
            {
                return byClip;
            }

            return _animByState.TryGetValue((choice.Layer, choice.StateName), out var byState) ? byState : null;
        }

        private static AnimatorController ResolveController(RuntimeAnimatorController runtime)
        {
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }

            return runtime as AnimatorController;
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

        // ── 選ぶ・作る ──

        private void OnClipChoiceChosen(int index)
        {
            if (index < 0 || index >= _clipChoices.Count)
            {
                return;
            }

            var choice = _clipChoices[index];
            var found = FindAnimDataFor(choice) ?? CreateAnimDataFor(choice);
            if (found == null)
            {
                return;
            }

            _sourceStatus = $"'{found.DisplayName ?? found.name}' を対象にしました({AssetDatabase.GetAssetPath(found)})。";
            if (found != Target)
            {
                SetTarget(found);
                if (_sourceStatusLabel != null)
                {
                    _sourceStatusLabel.text = _sourceStatus;
                }
            }
        }

        private AnimData CreateAnimDataFor(ClipChoice choice)
        {
            var animator = CurrentAnimator;
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
                if (choice.Clip != null)
                {
                    _animByClip[choice.Clip] = (AnimData)asset;
                }

                _animByState[(choice.Layer, choice.StateName)] = (AnimData)asset;
                _choicesDirty = true;
            }

            return asset as AnimData;
        }

        private static string ToIdentifier(string name, string fallback) => AssetNamingService.ToIdentifier(name, fallback);

        // 対象の AnimData に対応するステートの番号(無ければ -1)。
        private int CurrentChoiceIndex()
        {
            if (Target == null)
            {
                return -1;
            }

            for (var i = 0; i < _clipChoices.Count; i++)
            {
                if (FindAnimDataFor(_clipChoices[i]) == Target)
                {
                    return i;
                }
            }

            return -1;
        }

        // 同じステート(Layer + ステート名)にある他クリップの AnimData へイベントをコピーする(無ければ作成)。
        private void CopyEventsToStateClips()
        {
            var currentIndex = CurrentChoiceIndex();
            if (Target == null || currentIndex < 0)
            {
                AppendLog("⚠ 「Animator のステートから選ぶ」に対象のクリップが並んでいるときに使えます(対象の Animator を決めてください)");
                return;
            }

            var current = _clipChoices[currentIndex];
            var copied = 0;
            foreach (var choice in new List<ClipChoice>(_clipChoices))
            {
                if (choice == current || choice.Layer != current.Layer || choice.StateName != current.StateName || choice.Clip == current.Clip)
                {
                    continue;
                }

                var dest = FindAnimDataFor(choice) ?? CreateAnimDataFor(choice);
                if (dest == null || dest == Target)
                {
                    continue;
                }

                CopyEvents(Target, dest);
                copied++;
            }

            AppendLog(copied == 0 ? "⚠ 同じステートに他のクリップがありません" : $"同じステートの {copied} クリップへコピーしました");
            RefreshAssetChoices();
            RefreshStateChoices();
        }
    }
}
