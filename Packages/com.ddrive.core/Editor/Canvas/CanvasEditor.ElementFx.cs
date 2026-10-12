using System;
using System.Collections.Generic;
using DDrive.Editor.Ui;
using DDrive.Editor.Ux;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // 「要素の動き(ElementFx)」の欄(docs/1010 UX-4 / D-10)。
    // 1 要素 = 1 行(要素名・種類のチップ・「Appear: Pop / Idle: なし / Disappear: Fade」の 1 行要約・▶・↺・⚙)。展開(⚙)はその行だけで、
    // 3 つの動きの選び方(プリセット / [Catalog] / UiTweenData 直接指定)・出現の遅れ・SE・選択・コピーが出る。
    // 一覧の上に絞り込み(検索・設定済み / 未設定・種類のチップ)と件数、一括操作は 1 行。埋め込みの子の行は展開せず「1 行の要約 + 開く」。
    internal sealed partial class CanvasEditor
    {
        private const string NoneChoice = "なし";
        private static readonly string[] FxKinds = { "ボタン", "スライダー", "テキスト", "画像", "パネル", "埋め込み" };
        private static readonly string[] Phases = { "Appear", "Idle", "Disappear" };

        private CanvasItemList _fxList;
        private CanvasSectionView _fxSection;
        private readonly HashSet<string> _fxOpen = new();
        private readonly Dictionary<string, VisualElement> _fxLineByPath = new();

        private UxRow CreateFxRows(FieldGuideEntry e, UxCard card)
        {
            _fxList = new CanvasItemList("要素", "設定済み", FxKinds, "ElementFx がありません(上の「要素を自動収集」で追加してください)");
            _fxSection = new CanvasSectionView("fx", _foldOpen, FxTitle, FxSignature, _ => _fxList.SetItems(CollectFxItems()));

            var bulk = new VisualElement();
            bulk.AddToClassList("ux-toolsline");
            bulk.Add(MakeButton("要素を自動収集", null, () => CollectElementFx(), "Prefab 内の Graphic / UiInteractable から ElementFx の行を作る(既存の行は保持する。埋め込みの配下は子の担当)"));
            var presetField = new EnumField(_bulkPreset);
            presetField.AddToClassList("ux-pathmenu");
            presetField.RegisterValueChangedCallback(evt => _bulkPreset = (UiPreset)evt.newValue);
            bulk.Add(presetField);
            bulk.Add(MakeButton("全ボタンに反映", null, ApplyBulkPresetToButtons, "Prefab 内の全 UiButton の Appear にこのプリセットを設定する"));
            bulk.Add(MakeButton("▶ 全 Appear", null, () => PlayAllPhasePreview("Appear"), "登録済みの全要素の Appear を、それぞれに割り当てられた動きでまとめて再生する"));
            bulk.Add(MakeButton("▶ 全 Idle", null, () => PlayAllPhasePreview("Idle"), "登録済みの全要素の Idle をまとめて再生する"));
            bulk.Add(MakeButton("▶ 全 Disappear", null, () => PlayAllPhasePreview("Disappear"), "登録済みの全要素の Disappear をまとめて再生する"));
            bulk.Add(MakeButton("■ 全て停止", null, StopAllPhasePreview, "再生中の ElementFx をまとめて止め、再生前の状態(初期位置など)へ戻す"));
            _fxSection.Static.Add(bulk);

            var toggles = new VisualElement();
            toggles.AddToClassList("ux-toolsline");
            _idleFlowToggle = new Toggle("Idle を流す(プレハブモード)")
            {
                value = _idleFlowOn,
                tooltip = "プレハブモードで、Idle が割り当てられた全要素(埋め込みの子の分を含む)の Idle を流し続ける。止める・保存の直前・プレハブモードを閉じる・対象の切り替え・Play Mode に入るときは、流す前の値へ戻す。設定は保存されない(開き直すとオフ)",
            };
            _idleFlowToggle.AddToClassList("ux-bar__repeat");
            _idleFlowToggle.RegisterValueChangedCallback(evt => SetIdleFlow(evt.newValue));
            toggles.Add(_idleFlowToggle);
            var selectOnPlay = new Toggle("▶ 再生時にその要素を選択")
            {
                value = EditorPrefs.GetBool(SelectOnPlayPrefKey, true),
                tooltip = "行ごとの ▶ を押したとき、再生する要素(プレビュー実体 / プレハブモードの実体)を選択状態にする。設定はエディタに保存される",
            };
            selectOnPlay.AddToClassList("ux-bar__repeat");
            selectOnPlay.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(SelectOnPlayPrefKey, evt.newValue));
            toggles.Add(selectOnPlay);
            _fxSection.Static.Add(toggles);
            _idleFlowHint = new Label();
            _idleFlowHint.AddToClassList("ux-dim");
            _idleFlowHint.AddToClassList("ux-note");
            _fxSection.Static.Add(_idleFlowHint);
            UpdateIdleFlowUi();

            _fxSection.Static.Add(_fxList);
            _fxSection.Rebuild();

            AddRow(card, SectionRow(e, _fxSection, q => _fxList.AnyMatch(q)), false);
            return RawListRow(e, "要素ごとの動き(一覧そのもの)");
        }

        private string FxTitle()
        {
            var rows = Target?.ElementEffects;
            var n = rows?.Length ?? 0;
            var done = 0;
            for (var i = 0; i < n; i++)
            {
                if (FxConfigured(rows[i]))
                {
                    done++;
                }
            }

            var embeds = Target?.EmbeddedCanvases?.Length ?? 0;
            return $"{n} 要素・設定済み {done}" + (embeds > 0 ? $"・埋め込み {embeds}" : string.Empty);
        }

        private static bool FxConfigured(in ElementFx fx) =>
            fx.Appear.IsValid || fx.Idle.IsValid || fx.Disappear.IsValid
            || fx.AppearPreset.Preset != UiPreset.None || fx.IdlePreset.Preset != UiPreset.None || fx.DisappearPreset.Preset != UiPreset.None;

        private int FxSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, t.Prefab != null ? t.Prefab.GetInstanceID() : 0);
            var rows = t.ElementEffects;
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var r = rows[i];
                h = CanvasSig.Add(h, r.ElementPath);
                h = CanvasSig.Add(h, (int)r.AppearPreset.Preset);
                h = CanvasSig.Add(h, (int)r.IdlePreset.Preset);
                h = CanvasSig.Add(h, (int)r.DisappearPreset.Preset);
                h = CanvasSig.Add(h, r.Appear.Value);
                h = CanvasSig.Add(h, r.Idle.Value);
                h = CanvasSig.Add(h, r.Disappear.Value);
            }

            var embeds = t.EmbeddedCanvases;
            for (var i = 0; embeds != null && i < embeds.Length; i++)
            {
                h = CanvasSig.Add(h, embeds[i].RootPath);
                h = CanvasSig.Add(h, embeds[i].Canvas.Value);
            }

            return h;
        }

        // ── 一覧の項目 ──

        private List<CanvasListItem> CollectFxItems()
        {
            var items = new List<CanvasListItem>();
            _fxLineByPath.Clear();
            if (Target == null)
            {
                return items;
            }

            _catalogChoices.Clear();
            foreach (var (name, tween) in UiPresetCatalogUtility.Collect())
            {
                _catalogChoices.Add(($"[Catalog] {name}", tween));
            }

            var owner = Target;
            var groups = CanvasEmbeddedEditing.BuildGroups(owner, Lookup, null);
            foreach (var index in groups.ParentRows)
            {
                items.Add(FxItem(owner, index, null));
            }

            foreach (var g in groups.Embeds)
            {
                foreach (var index in g.OverrideRows)
                {
                    items.Add(FxItem(owner, index, "[親での上書き] "));
                }
            }

            foreach (var g in groups.Embeds)
            {
                var group = g;
                items.Add(new CanvasListItem
                {
                    Key = "embed:" + g.RootPath,
                    Title = g.RootPath,
                    Chip = "埋め込み",
                    Kind = 5,
                    Summary = true,
                    Configured = g.ChildRows.Count > 0,
                    Search = g.RootPath,
                    Make = () => MakeEmbedSummaryRow(group),
                });
            }

            return items;
        }

        private CanvasListItem FxItem(CanvasData owner, int index, string prefix)
        {
            var fx = GetFx(owner, index);
            var path = fx.ElementPath ?? string.Empty;
            var kind = KindOf(path);
            return new CanvasListItem
            {
                Key = "fx:" + path,
                Title = (prefix ?? string.Empty) + (string.IsNullOrEmpty(path) ? RootElementLabel : path),
                Chip = FxKinds[kind],
                Kind = kind,
                Configured = FxConfigured(fx),
                Search = FxSummary(fx),
                Make = () => MakeFxRow(owner, index, prefix, kind),
            };
        }

        // Prefab の中の要素の種類(チップ・絞り込み用)。0 ボタン / 1 スライダー / 2 テキスト / 3 画像 / 4 パネル。
        private int KindOf(string path)
        {
            var prefab = Target != null ? Target.Prefab : null;
            if (prefab == null)
            {
                return 4;
            }

            var t = string.IsNullOrEmpty(path) ? prefab.transform : prefab.transform.Find(path);
            if (t == null)
            {
                return 4;
            }

            if (t.GetComponent<UiButton>() != null || t.GetComponent<UnityEngine.UI.Button>() != null)
            {
                return 0;
            }

            if (t.GetComponent<UiSlider>() != null || t.GetComponent<UnityEngine.UI.Slider>() != null)
            {
                return 1;
            }

            if (t.GetComponent<UnityEngine.UI.Text>() != null || t.GetComponent("TMP_Text") != null)
            {
                return 2;
            }

            return t.GetComponent<UnityEngine.UI.Graphic>() != null ? 3 : 4;
        }

        private string FxSummary(in ElementFx fx) =>
            $"Appear: {PhaseText(fx.AppearPreset, fx.Appear)} / Idle: {PhaseText(fx.IdlePreset, fx.Idle)} / Disappear: {PhaseText(fx.DisappearPreset, fx.Disappear)}";

        private string PhaseText(UiPresetRef preset, AssetId<UiTweenMarker> id)
        {
            if (id.IsValid)
            {
                var match = _catalogChoices.Find(c => c.tween != null && c.tween.Id == id.Value);
                if (match.tween != null)
                {
                    return match.label.Replace("[Catalog] ", string.Empty);
                }

                var tween = FindUiTweenData(id.Value);
                return tween != null ? tween.name : $"(Id 0x{id.Value:X})";
            }

            return preset.Preset != UiPreset.None ? preset.Preset.ToString() : NoneChoice;
        }

        // ── 1 行 ──

        private VisualElement MakeFxRow(CanvasData owner, int index, string prefix, int kind)
        {
            var fx = GetFx(owner, index);
            var path = fx.ElementPath ?? string.Empty;
            var key = "fx:" + path;
            var open = _fxOpen.Contains(key);

            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--hl", _highlightPath != null && _highlightPath == path);
            row.EnableInClassList("ux-fxrow--open", open);
            _fxLineByPath[path] = row;

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.Add(NewFlag(_fxLineFlags, index, _fxIssue));
            var name = new Label((prefix ?? string.Empty) + (string.IsNullOrEmpty(path) ? RootElementLabel : path))
            {
                tooltip = "クリックでこの要素を選択して Inspector に出す(プレハブモード中はステージ内、確認用シーンの表示中はプレビュー実体、どちらも無ければ Prefab アセット内の要素を Ping)",
            };
            name.AddToClassList("ux-fxrow__name");
            name.RegisterCallback<ClickEvent>(_ => SelectElement(path, false));
            head.Add(name);
            var chip = new Label(FxKinds[kind]);
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            head.Add(chip);
            var summary = new Label(FxSummary(fx));
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);

            var configured = FxConfigured(fx);
            var firstPhase = !string.IsNullOrEmpty(FirstAssignedPhase(fx)) ? FirstAssignedPhase(fx) : "Appear";
            var play = new Button { text = "▶" };
            play.AddToClassList("ux-btn");
            play.AddToClassList("ux-btn--icon");
            play.SetEnabled(configured);
            var widgets = new PhaseRowWidgets { ElementPath = path, Phase = firstPhase, PlayButton = play };
            play.clicked += () =>
            {
                if (_phasePreviewHandles.TryGetValue((path, firstPhase), out var h) && _tweenManager != null && _tweenManager.IsPlaying(h))
                {
                    StopPhasePreviewAndReset(path, firstPhase);
                }
                else
                {
                    var f = GetFx(owner, index);
                    var (preset, id) = GetPhaseValue(f, firstPhase);
                    PlayPhasePreview(path, firstPhase, preset, id, true);
                }
            };
            _phaseRowWidgets.Add(widgets);
            head.Add(play);

            var reset = new Button(() => ResetFx(owner, index)) { text = "↺", tooltip = "この要素の動き(Appear / Idle / Disappear・遅れ・SE)を未設定に戻す(Ctrl+Z で戻せる)" };
            reset.AddToClassList("ux-btn");
            reset.AddToClassList("ux-btn--icon");
            reset.SetEnabled(configured || fx.AppearDelay != 0f || fx.AppearSe.IsValid || fx.DisappearSe.IsValid);
            head.Add(reset);

            var toggle = new Button(() =>
            {
                if (!_fxOpen.Add(key))
                {
                    _fxOpen.Remove(key);
                }

                _fxList.Render();
            })
            { text = open ? "▾" : "⚙", tooltip = "この要素の詳細を開く / 閉じる(動きの選び方・出現の遅れ・SE・選択・コピー)" };
            toggle.AddToClassList("ux-btn");
            toggle.AddToClassList("ux-btn--icon");
            head.Add(toggle);
            row.Add(head);

            if (open)
            {
                row.Add(MakeFxDetails(owner, index, path));
            }

            return row;
        }

        private static string FirstAssignedPhase(in ElementFx fx)
        {
            foreach (var p in Phases)
            {
                var (preset, id) = GetPhaseValue(fx, p);
                if (id.IsValid || preset.Preset != UiPreset.None)
                {
                    return p;
                }
            }

            return null;
        }

        private void ResetFx(CanvasData owner, int index)
        {
            if (owner == null || owner.ElementEffects == null || index < 0 || index >= owner.ElementEffects.Length)
            {
                return;
            }

            Undo.RecordObject(owner, "ElementFx: 未設定に戻す");
            owner.ElementEffects[index] = new ElementFx { ElementPath = owner.ElementEffects[index].ElementPath };
            EditorUtility.SetDirty(owner);
            if (owner == Target)
            {
                AfterDataWrite();
            }
        }

        // ── 展開(その行だけ) ──

        private VisualElement MakeFxDetails(CanvasData owner, int index, string elementPath)
        {
            var box = new VisualElement();
            box.AddToClassList("ux-fxrow__details");

            var actions = new VisualElement();
            actions.AddToClassList("ux-toolsline");
            actions.Add(MakeButton("選択", null, () => SelectElement(elementPath, false), "この要素を選択して Inspector に出す"));
            actions.Add(MakeButton("フォーカス", null, () => SelectElement(elementPath, true), "この要素を選択し、SceneView のカメラをその要素の矩形へ寄せる(表示中の実体が無いときは選択のみ)"));
            actions.Add(MakeButton("選択して移動(Prefab を開く)", null, () => SelectElementForMove(elementPath), "Prefab をプレハブモードで開いてこの要素を選ぶ。Unity 標準の Move / Rect ツールで動かせる(Ctrl+Z で戻せる)"));
            actions.Add(MakeButton("他の要素へコピー", null, () => CopyRowToOthers(index), "この要素の設定(Appear / Idle / Disappear)を、他のすべての要素へコピーする"));
            box.Add(actions);

            box.Add(BuildPhaseBlock(owner, index, "Appear", elementPath));
            box.Add(BuildPhaseBlock(owner, index, "Idle", elementPath));
            box.Add(BuildPhaseBlock(owner, index, "Disappear", elementPath));

            var props = new VisualElement();
            props.AddToClassList("ux-fxrow__props");
            AddFxProp(props, index, nameof(ElementFx.AppearDelay), "出現の遅れ(秒)");
            AddFxProp(props, index, nameof(ElementFx.AppearSe), "出現の SE");
            AddFxProp(props, index, nameof(ElementFx.DisappearSe), "消えるときの SE");
            box.Add(props);
            return box;
        }

        private void AddFxProp(VisualElement parent, int index, string field, string label)
        {
            var prop = So?.FindProperty($"{nameof(CanvasData.ElementEffects)}.Array.data[{index}].{field}");
            if (prop == null)
            {
                return;
            }

            var pf = new PropertyField(prop, label);
            pf.AddToClassList("ux-fxrow__prop");
            pf.BindProperty(prop);
            parent.Add(pf);
        }

        // 1 つの動き(Appear / Idle / Disappear)の編集: 選び方(なし / プリセット / [Catalog])+ UiTweenData 直接指定 + ✎ Tween Editor + ▶ ⏸ ■。
        private VisualElement BuildPhaseBlock(CanvasData owner, int index, string phase, string elementPath)
        {
            Func<UiPresetRef> getPreset = () => GetPhaseValue(GetFx(owner, index), phase).preset;
            Func<AssetId<UiTweenMarker>> getId = () => GetPhaseValue(GetFx(owner, index), phase).id;
            Action<UiPresetRef> setPreset = v => UpdateFx(owner, index, e => SetPhasePreset(e, phase, v));
            Action<AssetId<UiTweenMarker>> setId = v => UpdateFx(owner, index, e => SetPhaseId(e, phase, v));

            var line = new VisualElement();
            line.AddToClassList("ux-fxphase");
            var caption = new Label(phase);
            caption.AddToClassList("ux-fxphase__caption");
            line.Add(caption);

            var choices = new List<string> { NoneChoice };
            foreach (var n in Enum.GetNames(typeof(UiPreset)))
            {
                if (n != nameof(UiPreset.None))
                {
                    choices.Add(n);
                }
            }

            foreach (var (choiceLabel, _) in _catalogChoices)
            {
                choices.Add(choiceLabel);
            }

            var currentId = getId();
            var currentPreset = getPreset();
            var currentLabel = NoneChoice;
            if (currentId.IsValid)
            {
                var match = _catalogChoices.Find(c => c.tween != null && c.tween.Id == currentId.Value);
                currentLabel = match.tween != null ? match.label : $"(Id 0x{currentId.Value:X})";
                if (!choices.Contains(currentLabel))
                {
                    choices.Add(currentLabel);
                }
            }
            else if (currentPreset.Preset != UiPreset.None)
            {
                currentLabel = currentPreset.Preset.ToString();
            }

            var startIndex = choices.IndexOf(currentLabel);
            var popup = new PopupField<string>(choices, startIndex >= 0 ? startIndex : 0);
            popup.AddToClassList("ux-fxphase__popup");
            popup.RegisterValueChangedCallback(evt =>
            {
                if (owner == null)
                {
                    return;
                }

                Undo.RecordObject(owner, "ElementFx: 割当変更");
                var value = evt.newValue;
                if (value == NoneChoice)
                {
                    setPreset(default);
                    setId(default);
                }
                else
                {
                    var match = _catalogChoices.Find(c => c.label == value);
                    if (match.tween != null)
                    {
                        setId(new AssetId<UiTweenMarker>(match.tween.Id, AssetType.UiTween));
                        setPreset(default);
                    }
                    else if (Enum.TryParse<UiPreset>(value, out var preset))
                    {
                        setPreset(new UiPresetRef { Preset = preset });
                        setId(default);
                    }
                }

                EditorUtility.SetDirty(owner);
                if (owner == Target)
                {
                    AfterDataWrite();
                }
            });
            line.Add(popup);

            var objectField = new ObjectField { objectType = typeof(UiTweenData) };
            objectField.AddToClassList("ux-fxphase__object");
            objectField.tooltip = "UiTweenData を直接指定する(あれば上の選び方より優先)";
            objectField.SetValueWithoutNotify(currentId.IsValid ? FindUiTweenData(currentId.Value) : null);
            objectField.RegisterValueChangedCallback(evt =>
            {
                if (owner == null)
                {
                    return;
                }

                Undo.RecordObject(owner, "ElementFx: Tween 直接指定");
                if (evt.newValue is UiTweenData tween && tween.Id != 0)
                {
                    setId(new AssetId<UiTweenMarker>(tween.Id, AssetType.UiTween));
                    setPreset(default);
                }
                else
                {
                    setId(default);
                }

                EditorUtility.SetDirty(owner);
                if (owner == Target)
                {
                    AfterDataWrite();
                }
            });
            line.Add(objectField);

            var edit = new Button(() =>
            {
                var tween = currentId.IsValid ? FindUiTweenData(currentId.Value) : null;
                if (tween == null)
                {
                    return;
                }

                if (_manager != null && !IsPreviewUsable())
                {
                    PlacePreview();
                }

                GameObject collectRoot = null;
                RectTransform elementTarget = null;
                if (TryGetPreviewPrefix(out var previewPrefix))
                {
                    collectRoot = _manager.GetGameObject(_previewHandle);
                    elementTarget = _manager.GetComponent<RectTransform>(_previewHandle, EmbeddedPaths.Combine(previewPrefix, elementPath));
                }

                var elementLabel = string.IsNullOrEmpty(elementPath) ? RootElementLabel : elementPath;
                UiTweenEditorWindow.Open(tween, collectRoot, elementTarget, elementLabel);
            })
            { text = "✎ Tween Editor", tooltip = "UI Tween Editor で開く(この要素を自動でプレビュー対象にして Track を編集・確認)。直接指定(UiTweenData)があるときだけ押せる" };
            edit.AddToClassList("ux-btn");
            edit.AddToClassList("ux-btn--small");
            edit.SetEnabled(currentId.IsValid);
            line.Add(edit);

            var play = new Button { text = "▶" };
            play.AddToClassList("ux-btn");
            play.AddToClassList("ux-btn--icon");
            var pause = new Button(() => TogglePausePhasePreview(elementPath, phase)) { text = "⏸ 一時停止", tooltip = "その場で一時停止 / 再開する" };
            pause.AddToClassList("ux-btn");
            pause.AddToClassList("ux-btn--small");
            var status = new Label(string.Empty);
            status.AddToClassList("ux-dim");
            var w = new PhaseRowWidgets { ElementPath = elementPath, Phase = phase, PlayButton = play, PauseButton = pause, StatusLabel = status };
            play.clicked += () =>
            {
                if (_phasePreviewHandles.TryGetValue((elementPath, phase), out var h) && _tweenManager != null && _tweenManager.IsPlaying(h))
                {
                    StopPhasePreviewAndReset(elementPath, phase);
                }
                else
                {
                    PlayPhasePreview(elementPath, phase, getPreset(), getId(), true);
                }
            };
            line.Add(play);
            line.Add(pause);
            line.Add(status);
            _phaseRowWidgets.Add(w);
            UpdatePhaseRowWidgets(w);
            return line;
        }

        private static ElementFx SetPhasePreset(ElementFx e, string phase, UiPresetRef v)
        {
            if (phase == "Appear")
            {
                e.AppearPreset = v;
            }
            else if (phase == "Idle")
            {
                e.IdlePreset = v;
            }
            else
            {
                e.DisappearPreset = v;
            }

            return e;
        }

        private static ElementFx SetPhaseId(ElementFx e, string phase, AssetId<UiTweenMarker> v)
        {
            if (phase == "Appear")
            {
                e.Appear = v;
            }
            else if (phase == "Idle")
            {
                e.Idle = v;
            }
            else
            {
                e.Disappear = v;
            }

            return e;
        }

        // ── 埋め込みの子: 展開せず 1 行の要約 + 「開く」 ──

        private VisualElement MakeEmbedSummaryRow(CanvasEmbeddedEditing.EmbedGroup g)
        {
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            var name = new Label(g.RootPath);
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);
            var chip = new Label("埋め込み");
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            head.Add(chip);
            var overridden = g.OverrideRows.Count;
            var summary = new Label(g.Child == null
                ? "子の CanvasData が未設定、または見つかりません"
                : $"→ {NameOf(g.Child)}: 子の ElementFx {g.ChildRows.Count} 行" + (overridden > 0 ? $"・親で上書き {overridden} 行" : string.Empty) + "(中身は子のエディターで)");
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);
            if (overridden > 0 && g.Child != null)
            {
                var group = g;
                head.Add(MakeButton("上書きを整理…", "ux-btn--small", () => CleanUpOverrides(group),
                    "この埋め込みの配下を指す親の行を整理する。対象の行を一覧した確認が 1 回出る(Ctrl+Z 1 回で戻せる)"));
            }

            if (g.Child != null)
            {
                var index = g.EmbedIndex;
                head.Add(MakeButton("開く", "ux-btn--small", () => EditEmbedded(index),
                    "編集対象をこの子の CanvasData に切り替える(「← 親へ戻る」で戻れる)。▶ 再生・「選択」は親の Prefab の中の埋め込み実体で動く"));
            }

            row.Add(head);
            return row;
        }

        // 選択した要素の行を強調して展開し、一覧の該当位置までスクロールする。行が無ければステータスで知らせる。
        private void HighlightRow(string path)
        {
            path ??= string.Empty;
            _highlightPath = path;
            if (_fxList == null || _fxSection == null)
            {
                return;
            }

            _fxList.ClearFilters();
            _fxList.Render();
            if (!_fxLineByPath.TryGetValue(path, out _))
            {
                SetStatus($"'{(string.IsNullOrEmpty(path) ? RootElementLabel : path)}' の ElementFx 行はまだありません(「要素を自動収集」で追加できます)");
                return;
            }

            _fxSection.Open();
            _fxList.Render(); // 強調の印を付けて作り直す
            if (_fxLineByPath.TryGetValue(path, out var line))
            {
                line.schedule.Execute(() => ScrollToElement(line)).StartingIn(50);
            }
        }

        // 検索(ウィンドウ側): 一致する要素があれば一覧を絞り、欄を開く。
        private void ApplyFxFilter(string query)
        {
            if (_fxList == null)
            {
                return;
            }

            _fxList.SetExternalQuery(query);
            if (!string.IsNullOrEmpty(query) && _fxList.AnyMatch(query))
            {
                _fxSection?.Open();
            }
        }

        private void EditEmbedded(int embedIndex)
        {
            if (Target == null || Target.EmbeddedCanvases == null || embedIndex < 0 || embedIndex >= Target.EmbeddedCanvases.Length)
            {
                return;
            }

            var embed = Target.EmbeddedCanvases[embedIndex];
            var child = Lookup.Find(embed.Canvas);
            if (child == null)
            {
                SetStatus("子の CanvasData が見つかりません");
                return;
            }

            var chain = new List<CanvasEmbeddedEditing.Link>(_ancestors) { new CanvasEmbeddedEditing.Link(Target, embed.RootPath) };
            SetTargetIn(child, chain);
        }

        // 「親での上書き」の「上書きを整理…」: 登録済みの埋め込みについて、既定のままの行は取り除き、設定のある行は確認する。
        private void CleanUpOverrides(CanvasEmbeddedEditing.EmbedGroup g)
        {
            var owner = Target;
            if (owner == null || g.Child == null)
            {
                return;
            }

            var cleanup = CanvasEmbeddedEditing.CleanUpOverrides(owner, g.RootPath, g.Child);
            if (owner != Target)
            {
                return;
            }

            if (cleanup.Cancelled)
            {
                SetStatus($"'{g.RootPath}' の整理をキャンセルしました(何も変更していません)");
                return;
            }

            AfterDataWrite();
            var detail = cleanup.Describe();
            SetStatus(string.IsNullOrEmpty(detail) ? $"'{g.RootPath}' の配下に整理する行はありませんでした" : detail);
        }
    }
}
