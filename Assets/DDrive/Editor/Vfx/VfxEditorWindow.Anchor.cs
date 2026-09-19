using System.Collections.Generic;
using DDrive.Editor.Anchor;
using DDrive.Editor.Audio;
using DDrive.Editor.Common;
using DDrive.Editor.Preview;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Vfx
{
    // Anchor 編集: Space/Path(ボーン・AnchorPoint ドロップダウン) / 高さ・向きスライダー / 2D パッド /
    // SceneView 上の移動・回転ハンドル。どの経路で編集しても AnchorDef に Undo 付きで書き戻し、
    // 再生中の実体には VfxManager.ReapplyAnchor で即時反映する(停止→再生の押し直し不要)。
    public sealed partial class VfxEditorWindow
    {
        private const float AnchorPadHeight = 160f;
        private const float AnchorPadRange = 5f;

        private EnumField _anchorSpaceField;
        private TextField _anchorPathField;
        private Label _anchorStatusLabel;
        private Slider _anchorHeightSlider;
        private Slider _anchorFacingSlider;
        private Vector3Field _anchorScaleField;
        private Toggle _anchorFollowRotToggle;
        private Toggle _anchorDetachToggle;
        private Toggle _sceneHandleToggle;
        private ToolbarMenu _boneDropdown;
        private IMGUIContainer _padContainer;

        // AnchorId(Anchor アセット参照)。設定されている間は埋め込み欄を畳み、編集は AnchorEditor へ誘導する([21] §3.6)。
        private PropertyField _anchorIdField;
        private Button _openAnchorButton;
        private Button _toAssetButton;
        private Label _anchorAssetLabel;
        private VisualElement _embeddedContainer;

        private bool UsesAnchorAsset => _target != null && _target.AnchorId.IsValid;

        private Label _sceneOwnerLabel;

        private Vector2 _anchorPadOffset; // X/Z(メートル)。パッド上の 2D 表現
        private bool _draggingAnchorPad;

        private void BuildAnchorSection(VisualElement root)
        {
            var foldout = new Foldout { text = "Anchor(アタッチ位置)", value = true };

            // ── Anchor アセット(AnchorId) ──
            var idRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _anchorIdField = new PropertyField { label = "Anchor アセット", style = { flexGrow = 1f }, tooltip = "設定すると埋め込み Anchor より優先される。位置の調整は AnchorEditor で行う" };
            _anchorIdField.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                RefreshAnchorUi();
                RestartMainIfPlaying();
            });
            idRow.Add(_anchorIdField);
            _openAnchorButton = new Button(() =>
            {
                var asset = UsesAnchorAsset ? EditorAnchorRegistry.Find(_target.AnchorId.Value) : null;
                if (asset != null)
                {
                    AnchorEditorWindow.Open(asset);
                }
            }) { text = "AnchorEditor で開く" };
            idRow.Add(_openAnchorButton);
            _toAssetButton = new Button(ConvertEmbeddedAnchorToAsset) { text = "埋め込みをアセット化", tooltip = "現在の埋め込み Anchor から AnchorData を作り、この VFX の AnchorId に設定する(埋め込み値は残る)" };
            idRow.Add(_toAssetButton);
            foldout.Add(idRow);

            _anchorAssetLabel = new Label { style = { opacity = 0.8f, marginLeft = 4, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            foldout.Add(_anchorAssetLabel);

            _embeddedContainer = new VisualElement();
            foldout.Add(_embeddedContainer);
            var embeddedRoot = foldout;

            _anchorSpaceField = new EnumField("Space", AnchorSpace.World)
            {
                tooltip = "World=固定座標 / BoneName・NamedObject=スポーン先の階層から Path の名前を検索 / ContextTarget=スポーン先そのもの",
            };
            _anchorSpaceField.RegisterValueChangedCallback(evt =>
            {
                ApplyAnchorChange(a =>
                {
                    a.Space = (AnchorSpace)evt.newValue;
                    return a;
                });
                RestartMainIfPlaying();
            });
            _embeddedContainer.Add(_anchorSpaceField);

            var pathRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _anchorPathField = new TextField("Path(ボーン/オブジェクト名)") { style = { flexGrow = 1f } };
            _anchorPathField.RegisterValueChangedCallback(evt =>
            {
                ApplyAnchorChange(a =>
                {
                    a.Path = evt.newValue;
                    return a;
                });
                RestartMainIfPlaying();
            });
            pathRow.Add(_anchorPathField);

            _boneDropdown = new ToolbarMenu { text = "一覧から選択" };
            pathRow.Add(_boneDropdown);
            _embeddedContainer.Add(pathRow);

            _anchorStatusLabel = new Label { style = { opacity = 0.8f, marginLeft = 4, marginBottom = 4, whiteSpace = WhiteSpace.Normal } };
            _embeddedContainer.Add(_anchorStatusLabel);

            _anchorHeightSlider = new Slider("高さオフセット(Y)", -3f, 3f) { showInputField = true };
            _anchorHeightSlider.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                var offset = a.LocalOffset;
                offset.y = evt.newValue;
                a.LocalOffset = offset;
                return a;
            }));
            _embeddedContainer.Add(_anchorHeightSlider);

            _anchorFacingSlider = new Slider("向き(Euler Y)", -180f, 180f) { showInputField = true, tooltip = "アタッチ先の回転に対する相対回転(回転追従 OFF ならワールド基準)" };
            _anchorFacingSlider.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                var euler = a.LocalEuler;
                euler.y = evt.newValue;
                a.LocalEuler = euler;
                return a;
            }));
            _embeddedContainer.Add(_anchorFacingSlider);

            _anchorScaleField = new Vector3Field("スケール") { tooltip = "スポーン物の localScale。(0,0,0) は 1 扱い" };
            _anchorScaleField.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                a.LocalScale = evt.newValue;
                return a;
            }));
            _embeddedContainer.Add(_anchorScaleField);

            // [09] §7.1 — Toggle 3 つを横一列に詰め込むため、幅 500px では折り返し(flexWrap)+
            // ラベル幅の縮小(ShrinkLabel)が無いと右端が見切れる(2026-09-17, U-27)。
            var toggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            _anchorFollowRotToggle = new Toggle("回転追従") { tooltip = "アタッチ先の回転に追従する(FollowRotation)" };
            _anchorFollowRotToggle.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                a.FollowRotation = evt.newValue;
                return a;
            }));
            CompactFieldLayout.ShrinkLabel(_anchorFollowRotToggle.labelElement);
            toggleRow.Add(_anchorFollowRotToggle);

            _anchorDetachToggle = new Toggle("親消滅後も残す") { style = { marginLeft = 12 }, tooltip = "アタッチ先が破棄されてもその場に残って再生完了まで続ける(DetachOnStop)" };
            _anchorDetachToggle.RegisterValueChangedCallback(evt => ApplyAnchorChange(a =>
            {
                a.DetachOnStop = evt.newValue;
                return a;
            }));
            CompactFieldLayout.ShrinkLabel(_anchorDetachToggle.labelElement);
            toggleRow.Add(_anchorDetachToggle);

            _sceneHandleToggle = new Toggle("SceneView 表示") { style = { marginLeft = 12 }, value = _sceneHandleEnabled, tooltip = "OFF: この VFX の Anchor を SceneView に一切描かない。ON: 目印を描き、このウィンドウを最後に操作していれば移動/回転ハンドルも出す(回転ツール選択時は回転)" };
            CompactFieldLayout.ShrinkLabel(_sceneHandleToggle.labelElement);
            _sceneHandleToggle.RegisterValueChangedCallback(evt =>
            {
                _sceneHandleEnabled = evt.newValue;
                RefreshSceneOwnerLabel();
                SceneView.RepaintAll();
            });
            toggleRow.Add(_sceneHandleToggle);
            _embeddedContainer.Add(toggleRow);

            _sceneOwnerLabel = new Label { style = { opacity = 0.65f, marginLeft = 4, whiteSpace = WhiteSpace.Normal } };
            _embeddedContainer.Add(_sceneOwnerLabel);
            RefreshSceneOwnerLabel();

            _padContainer = new IMGUIContainer(DrawAnchorPad);
            _padContainer.style.height = AnchorPadHeight;
            _embeddedContainer.Add(_padContainer);

            root.Add(embeddedRoot);
        }

        // Anchor 編集 UI がまだ構築されていない(初回 CreateGUI 実行前)場合は何もしない。
        private void RefreshAnchorUi()
        {
            if (_anchorSpaceField == null)
            {
                return;
            }

            var anchor = _target != null ? _target.Anchor : default;
            _anchorPadOffset = new Vector2(anchor.LocalOffset.x, anchor.LocalOffset.z);

            // AnchorId 行(SerializedObject バインド)と、埋め込み欄の表示切替。
            // 2026-09-17([39] U-13): 対象が破棄済み(削除・再インポート)の SerializedObject は
            // FindProperty が null を返し、BindProperty が ArgumentNullException を投げていた。
            // その例外で RefreshTargetUi / OnUndoRedo が途中終了し、後段の RefreshValidation に
            // 到達せず「検証」セクションが空のままになっていた(CLAUDE.md §0-4: 例外で止めない)。
            var anchorIdProp = _serializedTarget != null && _serializedTarget.targetObject != null
                ? _serializedTarget.FindProperty("AnchorId")
                : null;
            if (anchorIdProp != null)
            {
                _anchorIdField.BindProperty(anchorIdProp);
            }
            else
            {
                _anchorIdField.Unbind();
            }

            var usesAsset = UsesAnchorAsset;
            _anchorIdField.SetEnabled(_target != null);
            _openAnchorButton.SetEnabled(usesAsset);
            _toAssetButton.SetEnabled(_target != null && !usesAsset);
            _embeddedContainer.style.display = usesAsset ? DisplayStyle.None : DisplayStyle.Flex;
            if (usesAsset)
            {
                var asset = EditorAnchorRegistry.Find(_target.AnchorId.Value);
                _anchorAssetLabel.text = asset != null
                    ? $"Anchor アセット '{asset.name}' を使用中(埋め込みの Anchor は無視されます)。位置・ランダム・ディレイの調整は「AnchorEditor で開く」から。"
                    : $"⚠ AnchorId 0x{_target.AnchorId.Value:X} のアセットが見つかりません(World 原点扱い)。";
            }
            else
            {
                _anchorAssetLabel.text = string.Empty;
            }

            _anchorSpaceField.SetValueWithoutNotify(anchor.Space);
            _anchorPathField.SetValueWithoutNotify(anchor.Path ?? string.Empty);
            _anchorHeightSlider.SetValueWithoutNotify(anchor.LocalOffset.y);
            _anchorFacingSlider.SetValueWithoutNotify(anchor.LocalEuler.y);
            _anchorScaleField.SetValueWithoutNotify(anchor.LocalScale);
            _anchorFollowRotToggle.SetValueWithoutNotify(anchor.FollowRotation);
            _anchorDetachToggle.SetValueWithoutNotify(anchor.DetachOnStop);

            var enabled = _target != null;
            _anchorSpaceField.SetEnabled(enabled);
            _anchorPathField.SetEnabled(enabled);
            _anchorHeightSlider.SetEnabled(enabled);
            _anchorFacingSlider.SetEnabled(enabled);
            _anchorScaleField.SetEnabled(enabled);
            _anchorFollowRotToggle.SetEnabled(enabled);
            _anchorDetachToggle.SetEnabled(enabled);

            RefreshAnchorStatus();
            _padContainer?.MarkDirtyRepaint();
        }

        // 「今の設定でどこに出るか」を常に文字で示す(設定不備でプレビューが「効いていないだけ」に見える事故を防ぐ)。
        private void RefreshAnchorStatus()
        {
            if (_anchorStatusLabel == null)
            {
                return;
            }

            if (_target == null)
            {
                _anchorStatusLabel.text = string.Empty;
                return;
            }

            if (UsesAnchorAsset)
            {
                _anchorStatusLabel.text = string.Empty;
                return;
            }

            var anchor = _target.Anchor;
            var attach = _attachTarget != null ? _attachTarget.transform : null;

            switch (anchor.Space)
            {
                case AnchorSpace.World:
                    _anchorStatusLabel.text = "解決: World 固定(スポーン先は使いません。オフセット = ワールド座標)";
                    return;

                case AnchorSpace.ContextTarget:
                    _anchorStatusLabel.text = attach != null
                        ? $"解決: ✓ スポーン先 '{attach.name}' そのもの"
                        : "解決: ⚠ スポーン先が未指定のため、ワールド固定として扱われます";
                    return;

                default:
                    if (attach == null)
                    {
                        _anchorStatusLabel.text = "解決: ⚠ スポーン先が未指定のため Path を検索できません(ワールド固定扱い)。上の「スポーン先」にキャラクターや AnchorRig を指定してください";
                        return;
                    }

                    if (string.IsNullOrEmpty(anchor.Path))
                    {
                        _anchorStatusLabel.text = "解決: ⚠ Path が空です。「一覧から選択」でボーンか ★AnchorPoint を選んでください";
                        return;
                    }

                    var resolved = AnchorResolver.Resolve(anchor, attach);
                    if (resolved == null)
                    {
                        _anchorStatusLabel.text = $"解決: ⚠ '{anchor.Path}' がスポーン先 '{attach.name}' の階層に見つかりません(ワールド固定扱い)";
                        return;
                    }

                    var isPoint = resolved.TryGetComponent<AnchorPoint>(out _);
                    _anchorStatusLabel.text = $"解決: ✓ '{resolved.name}'{(isPoint ? "(★AnchorPoint: SpawnOffset/ランダム散らばりが追加適用されます)" : string.Empty)}";
                    return;
            }
        }

        // ToolbarMenu はクリック時に「その時点の menu の中身」を開くだけなので、選択元の
        // スポーン先が変わるたびに中身を作り直す(クリック時イベントではなく変化時に更新)。
        // AnchorPoint(シーン配置型アンカー)を持つ場合はそれを先頭に「★」付きで出す。
        // 選択時、Space が World のままなら NamedObject に切り替える(Path だけ入れても効かない事故の防止)。
        private void RefreshBoneMenu()
        {
            if (_boneDropdown == null)
            {
                return;
            }

            _boneDropdown.menu.MenuItems().Clear();

            if (_attachTarget == null)
            {
                _boneDropdown.menu.AppendAction("(「スポーン先」にシーン内のキャラクターや AnchorRig を指定してください)", _ => { }, DropdownMenuAction.Status.Disabled);
                return;
            }

            foreach (var point in _attachTarget.GetComponentsInChildren<AnchorPoint>(true))
            {
                var name = point.name;
                _boneDropdown.menu.AppendAction($"★ {name}", _ => SelectAnchorPath(name));
            }

            _boneDropdown.menu.AppendSeparator();

            foreach (var t in _attachTarget.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name;
                _boneDropdown.menu.AppendAction(name, _ => SelectAnchorPath(name));
            }
        }

        private void SelectAnchorPath(string name)
        {
            if (_target == null)
            {
                return;
            }

            ApplyAnchorChange(a =>
            {
                a.Path = name;
                if (a.Space == AnchorSpace.World || a.Space == AnchorSpace.ContextTarget)
                {
                    a.Space = AnchorSpace.NamedObject;
                }

                return a;
            });
            RefreshAnchorUi();
            RestartMainIfPlaying();
        }

        private void ApplyAnchorChange(System.Func<AnchorDef, AnchorDef> mutate)
        {
            if (_target == null)
            {
                return;
            }

            Undo.RecordObject(_target, "Change VFX Anchor");
            _target.Anchor = mutate(_target.Anchor);
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();

            _driver?.ReapplyAnchorToAll();
            RefreshAnchorStatus();
            SceneView.RepaintAll();
        }

        // ── 2D パッド(上から見た X/Z) ──

        private void DrawAnchorPad()
        {
            var rect = GUILayoutUtility.GetRect(100, AnchorPadHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.center.y - 0.5f, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            EditorGUI.DrawRect(new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.08f));

            if (_target == null)
            {
                GUI.Label(rect, "対象アセットが未選択です", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var originPx = rect.center;
            EditorGUI.DrawRect(new Rect(originPx.x - 4f, originPx.y - 4f, 8f, 8f), new Color(0.95f, 0.25f, 0.2f));

            var pointPx = ListenerPadMath.MetersToPixel(_anchorPadOffset, rect, AnchorPadRange, AnchorPadRange);
            EditorGUI.DrawRect(new Rect(pointPx.x - 5f, pointPx.y - 5f, 10f, 10f), new Color(0.35f, 0.85f, 0.65f));

            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f),
                $"X: {_anchorPadOffset.x:F2}m   Z: {_anchorPadOffset.y:F2}m(上から見た配置。ドラッグで移動 / SceneView のハンドルでも可)",
                EditorStyles.miniLabel);

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown when rect.Contains(evt.mousePosition):
                    _draggingAnchorPad = true;
                    MoveAnchorPad(rect, evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseDrag when _draggingAnchorPad:
                    MoveAnchorPad(rect, evt.mousePosition);
                    evt.Use();
                    break;

                case EventType.MouseUp when _draggingAnchorPad:
                    _draggingAnchorPad = false;
                    evt.Use();
                    break;
            }
        }

        private void MoveAnchorPad(Rect rect, Vector2 mousePx)
        {
            var meters = ListenerPadMath.PixelToMeters(mousePx, rect, AnchorPadRange, AnchorPadRange);
            _anchorPadOffset = ListenerPadMath.ClampToRange(meters, AnchorPadRange, AnchorPadRange);

            ApplyAnchorChange(a =>
            {
                var offset = a.LocalOffset;
                offset.x = _anchorPadOffset.x;
                offset.z = _anchorPadOffset.y;
                a.LocalOffset = offset;
                return a;
            });
        }

        // ── SceneView ハンドル ──
        // 再生中は実体の追従先(+AnchorPoint のランダム分)、停止中はスポーン先から解決した Transform を基準に
        // Anchor のワールド姿勢を求め、移動/回転ハンドルの結果を AnchorDef に逆変換して書き戻す。
        private void RefreshSceneOwnerLabel()
        {
            if (_sceneOwnerLabel != null)
            {
                _sceneOwnerLabel.text = _sceneHandleEnabled ? SceneGuiOwner.DescribeFor(this) : "SceneView: 表示オフ";
            }
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (!_sceneHandleEnabled || _target == null || _driver == null)
            {
                return; // 表示オフ
            }

            // AnchorId 使用時は AnchorEditor と同じ連鎖表示(基準 → 各段 → 最終位置)+ ハンドル編集にする。
            // ハンドルは参照先の AnchorData アセットを書き換える(同じ Anchor を使う他の VFX / SE にも効く)。
            List<AnchorData> assetChain = null;
            if (UsesAnchorAsset)
            {
                var asset = EditorAnchorRegistry.Find(_target.AnchorId.Value);
                assetChain = asset != null ? AnchorChainEditor.CollectRootToTarget(asset) : null;
                if (assetChain == null || assetChain.Count == 0)
                {
                    return;
                }
            }

            var anchor = assetChain != null ? assetChain[0].ToDef() : _target.Anchor;
            Transform baseTransform;
            var extraOffset = Vector3.zero;

            if (_driver.IsPlaying(_mainHandle) && _driver.Manager.TryGetAnchorTarget(_mainHandle, out var followTarget))
            {
                baseTransform = followTarget;
                extraOffset = _driver.Manager.GetAnchorExtraOffset(_mainHandle);
            }
            else
            {
                baseTransform = AnchorResolver.Resolve(anchor, _attachTarget != null ? _attachTarget.transform : null);
                if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
                {
                    extraOffset = point.SpawnOffset;
                }
            }

            // 描画権が他のウィンドウにあるときは薄い目印だけ(重なりを避ける)。
            var vfxColor = new Color(0.35f, 0.85f, 0.65f);
            if (assetChain != null)
            {
                var targetDef = AnchorChainEditor.ComposeUpTo(assetChain, assetChain.Count - 1);
                var vfxName = _target.DisplayName ?? _target.name;
                if (!SceneGuiOwner.IsOwner(this))
                {
                    // 2026-09-20(指摘1): 薄い目印もクリック可能にし、押したらこのウィンドウが描画権を持つ
                    // ようにする(AnchorSceneHandles.DrawClickableMarker、PresentationEditorWindow と共通)。
                    if (AnchorSceneHandles.DrawClickableMarker(targetDef, baseTransform, extraOffset, $"VFX: {vfxName}", vfxColor, active: false))
                    {
                        SceneGuiOwner.Claim(this);
                        Focus();
                    }

                    return;
                }

                var chainOrigin = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(anchor, baseTransform), anchor.FollowRotation);
                var chainParent = AnchorSceneHandles.DrawChain(assetChain, baseTransform, extraOffset, chainOrigin, vfxColor);
                AnchorSceneHandles.DrawOffsetLink(chainParent, AnchorPose.WorldPosition(targetDef, baseTransform, extraOffset), assetChain[assetChain.Count - 1].LocalOffset, vfxColor);
                var chainResult = AnchorSceneHandles.Draw(targetDef, baseTransform, extraOffset, $"VFX Anchor: {vfxName}(Anchor アセットを編集)", vfxColor);
                if (chainResult.PositionChanged || chainResult.RotationChanged)
                {
                    ApplyAnchorAssetHandle(assetChain, chainResult);
                }

                return;
            }

            if (!SceneGuiOwner.IsOwner(this))
            {
                if (AnchorSceneHandles.DrawClickableMarker(anchor, baseTransform, extraOffset, $"VFX: {(_target.DisplayName ?? _target.name)}", vfxColor, active: false))
                {
                    SceneGuiOwner.Claim(this);
                    Focus();
                }

                return;
            }

            // 描画と逆変換は AnchorEditor と共通(AnchorSceneHandles)。
            // 最終位置だけだと「何を基準にしたオフセットか」が分からないので、基準(解決先 Transform。
            // 未解決ならワールド原点)にも 3 軸とラベルを描き、基準 → 最終位置を線で結ぶ(U-24)。
            var originWorld = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(anchor, baseTransform), anchor.FollowRotation);
            AnchorSceneHandles.DrawOffsetLink(originWorld, AnchorPose.WorldPosition(anchor, baseTransform, extraOffset), anchor.LocalOffset, vfxColor);
            var result = AnchorSceneHandles.Draw(anchor, baseTransform, extraOffset, $"VFX Anchor: {(_target.DisplayName ?? _target.name)}", vfxColor);
            if (result.RotationChanged)
            {
                var euler = result.LocalEuler;
                ApplyAnchorChange(a =>
                {
                    a.LocalEuler = euler;
                    return a;
                });
                RefreshAnchorUi();
            }

            if (result.PositionChanged)
            {
                var local = result.LocalOffset;
                ApplyAnchorChange(a =>
                {
                    a.LocalOffset = local;
                    return a;
                });
                RefreshAnchorUi();
            }
        }

        // ハンドルの結果を参照先 AnchorData(連鎖の最終段)へ書き戻す。合成済みの値を親基準に変換するのは
        // AnchorEditor と同じ(AnchorChainEditor.ToChildLocal*)。
        private void ApplyAnchorAssetHandle(List<AnchorData> chain, AnchorSceneHandles.Result result)
        {
            var asset = chain[chain.Count - 1];
            AnchorDef? parentDef = chain.Count > 1 ? AnchorChainEditor.ComposeUpTo(chain, chain.Count - 2) : null;

            Undo.RecordObject(asset, "Move Anchor");
            if (result.PositionChanged)
            {
                asset.LocalOffset = AnchorChainEditor.ToChildLocalOffset(parentDef, result.LocalOffset);
            }

            if (result.RotationChanged)
            {
                asset.LocalEuler = AnchorChainEditor.ToChildLocalEuler(parentDef, result.LocalEuler);
            }

            EditorUtility.SetDirty(asset);
            // Registry は同じ AnchorData インスタンスを既に持っているので Refresh(全アセット走査)は不要
            // (ドラッグ中は毎フレーム呼ばれる。docs/44 P2-2)。再生中の実体への反映だけ行う。
            _driver?.ReapplyAnchorToAll();
            RefreshAnchorUi();
            SceneView.RepaintAll();
        }

        // 埋め込み Anchor → AnchorData アセット化(移行補助)。埋め込み値は残す。
        private void ConvertEmbeddedAnchorToAsset()
        {
            if (_target == null || UsesAnchorAsset)
            {
                return;
            }

            var category = string.IsNullOrEmpty(_target.Category) ? AnchorAssetFactory.DefaultCategory : AnchorAssetFactory.ToIdentifier(_target.Category);
            var identifier = AnchorAssetFactory.ToIdentifier(_target.name) + "Anchor";
            var asset = AnchorAssetFactory.CreateFromDef(_target.Anchor, $"{(_target.DisplayName ?? _target.name)} の Anchor", category, identifier);
            if (asset == null)
            {
                return;
            }

            Undo.RecordObject(_target, "Set VFX AnchorId");
            _target.AnchorId = new AssetId<AnchorMarker>(asset.Id, AssetType.Anchor);
            EditorUtility.SetDirty(_target);
            _serializedTarget?.Update();
            EditorAnchorRegistry.Refresh(_driver?.Registry);
            RefreshAnchorUi();
            RestartMainIfPlaying();
            EditorGUIUtility.PingObject(asset);
        }
    }
}
