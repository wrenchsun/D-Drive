using DDrive.Editor.Anim;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Foundation.Data;
using DDrive.Editor.Vfx;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anchor
{
    // 欄の組み立て(Anchor 固有の行): 位置のパッド・親 Anchor・連鎖・スポーン先の基準・試し出し・ヒエラルキーから作る・SceneView のハンドル。
    internal sealed partial class AnchorEditor
    {
        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case nameof(AnchorData.LocalOffset):
                    return CreatePadRows(e, card);

                case nameof(AnchorData.Parent):
                    return CreateParentRow(e);

                case AnchorFieldGuide.RowChain:
                    return CreateChainRow(e);

                case AnchorFieldGuide.RowTools:
                    return CreateToolsRow(e);

                case AnchorFieldGuide.RowTry:
                    return new AnchorAuxRow(e, CreateTryBox(), null, true);

                case AnchorFieldGuide.RowCreate:
                    return new AnchorAuxRow(e, CreateCreateBox(), null);

                case nameof(AnchorData.Flags):
                case nameof(AnchorData.Events):
                    GuidedField.Collapse(So, e.Field);
                    return base.CreateRow(e, card, isAdmin);

                default:
                    return base.CreateRow(e, card, isAdmin);
            }
        }

        // ── 位置のパッド(主部品) ──

        private UxRow CreatePadRows(FieldGuideEntry e, UxCard card)
        {
            var data = Target;
            var pad = new AnchorPad(
                data,
                () => data.LocalOffset,
                v => data.LocalOffset = v,
                final => VisualEdited(final, true));
            _pad = pad;
            var padRow = new UxVisualRow(So, DefaultSo, new[] { e }, "出る位置", "上から見たパッドをドラッグ。高さは右のスライダー。", FieldTier.Required, pad, RowChanged) { NoFold = true };
            padRow.Q<Label>(className: "ux-badge")?.Show(false); // かんたんモードにも出す欄だが「必須」のバッジは出さない
            AddRow(card, padRow, false);
            RefreshPadNote();

            // 同じ欄の通常入力(数値 3 つ)は詳細でだけ。
            return new UxRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, true, GuidedField.MakeInput(So, e.Field), RowChanged) { IsDuplicate = true };
        }

        // ── 親 Anchor(欄 + 「親を開く」) ──

        private UxRow CreateParentRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            box.AddToClassList("ux-anc-parent");
            var field = GuidedField.MakeInput(So, e.Field);
            field.AddToClassList("ux-anc-parent__field");
            box.Add(field);
            var open = MakeButton("親を開く", null, OpenParent, "親 Anchor をこのウィンドウで開く(連鎖をさかのぼる)");
            box.Add(open);
            return new UxRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, false, box, RowChanged);
        }

        private void OpenParent()
        {
            var parent = Target != null && Target.Parent.IsValid ? EditorAnchorRegistry.Find(Target.Parent.Value) : null;
            if (parent != null)
            {
                SetTarget(parent);
            }
        }

        // ── 連鎖(ルート → 対象。押すとその Anchor に切り替え) ──

        private UxRow CreateChainRow(FieldGuideEntry e)
        {
            AnimSyncBox box = null;
            box = new AnimSyncBox(ChainSignature, () => RebuildChain(box), null);
            box.Rebuild();
            return new AnchorAuxRow(e, box, box.Sync);
        }

        private int ChainSignature()
        {
            var h = 17;
            var chain = Chain;
            for (var i = 0; i < chain.Count; i++)
            {
                h = CanvasSig.Add(h, chain[i].GetInstanceID());
                h = CanvasSig.Add(h, chain[i].name);
            }

            h = CanvasSig.Add(h, Target != null && Target.Parent.IsValid ? 1 : 0);
            return h;
        }

        private void RebuildChain(VisualElement box)
        {
            box.Clear();
            if (Target == null)
            {
                return;
            }

            var chain = Chain;
            var line = AnchorUi.ToolsLine();
            line.AddToClassList("ux-anc-chain");
            line.Add(AnchorUi.Note("連鎖:"));
            for (var i = 0; i < chain.Count; i++)
            {
                var node = chain[i];
                var isTarget = ReferenceEquals(node, Target);
                var button = MakeButton(isTarget ? "[" + node.name + "]" : node.name, null, () => SetTarget(node), isTarget ? "いま開いている Anchor" : "この Anchor に切り替える");
                button.SetEnabled(!isTarget);
                line.Add(button);
                if (i < chain.Count - 1)
                {
                    line.Add(AnchorUi.Note("→"));
                }
            }

            box.Add(line);
            if (Target.Parent.IsValid && chain.Count > 0 && chain[0].Parent.IsValid)
            {
                var warn = new Label("⚠ ルートに辿り着けません(未登録または循環)");
                warn.AddToClassList("ux-lineflag");
                box.Add(warn);
            }
            else if (chain.Count <= 1)
            {
                box.Add(AnchorUi.Note("親はありません(この Anchor がルート)"));
            }
        }

        // ── スポーン先の基準(VFX Editor と同じ部品) ──

        private UxRow CreateToolsRow(FieldGuideEntry e)
        {
            _toolsRow = new VfxAnchorToolsRow(
                e,
                () => _attachTarget,
                SetAttach,
                () => _sceneHandle,
                v =>
                {
                    _sceneHandle = v;
                    SceneView.RepaintAll();
                },
                DescribeResolution,
                () => SceneGuiOwner.DescribeFor(this),
                (menu, attach) => VfxAnchorSceneGui.FillPathMenu(menu.menu, attach, SelectPath),
                e.Label,
                e.Hint);
            return _toolsRow;
        }

        private void SetAttach(GameObject go)
        {
            _attachTarget = go;
            RestartIfPlaying();
            _toolsRow?.Sync();
            SceneView.RepaintAll();
        }

        // 「今の設定でどこに出るか」(ルートの基準で解決。連鎖の途中に子があるときは合成後の値)と、生成のディレイ・確率。
        private string DescribeResolution()
        {
            if (Target == null)
            {
                return string.Empty;
            }

            var chain = Chain;
            var def = AnchorChainEditor.ComposeUpTo(chain, chain.Count - 1);
            var prefix = chain.Count > 1 ? $"ルート '{chain[0].name}' の基準で" : "基準:";
            var text = AnchorUi.ResolutionText(prefix, def, AttachTransform);
            var delay = 0f;
            var chance = 1f;
            foreach (var node in chain)
            {
                delay += node.DelaySec;
                chance *= Mathf.Clamp01(node.SpawnChance);
            }

            if (delay > 0f || chance < 1f)
            {
                text += $"  / 生成: {delay:0.##}s 後・確率 {chance:P0}";
            }

            return text;
        }

        private void SelectPath(string name)
        {
            if (Target == null)
            {
                return;
            }

            Undo.RecordObject(Target, "Change Anchor Path");
            Target.Path = name;
            if (Target.Space == AnchorSpace.World || Target.Space == AnchorSpace.ContextTarget)
            {
                Target.Space = AnchorSpace.NamedObject;
            }

            EditorUtility.SetDirty(Target);
            So?.Update();
            OnSideEffect();
            RunValidation();
            RefreshAll(true);
            RestartIfPlaying();
            SceneView.RepaintAll();
        }

        // ── 試し出し(実 Manager 経由) ──

        private VisualElement CreateTryBox()
        {
            var box = new VisualElement();
            box.Add(PickerLine("確認用 VFX", typeof(VfxData), _testVfx, o => _testVfx = o as VfxData, PlayVfx, () => StopVfxOnly(), "この VFX を、この Anchor の位置に出す(連打で出し直し。ランダム・ディレイ・確率も毎回サンプリングされる)", "出している VFX を止める"));
            _tryStatus = new Label();
            _tryStatus.AddToClassList("ux-dim");
            _tryStatus.AddToClassList("ux-note");
            box.Add(_tryStatus);
            box.Add(PickerLine("確認用 SE", typeof(SeData), _testSe, o => _testSe = o as SeData, PlaySe, () => _sePreview?.StopAll(), "この SE を、この Anchor の位置で鳴らす", "鳴らしている SE を止める"));
            return box;
        }

        private static VisualElement PickerLine(string label, System.Type type, Object value, System.Action<Object> set, System.Action play, System.Action stop, string playTip, string stopTip)
        {
            var line = new VisualElement();
            line.AddToClassList("ux-slot");
            var field = new ObjectField { objectType = type, tooltip = label }; // 型は欄に出る(None (Vfx Data))ので、ラベルは付けない
            field.SetValueWithoutNotify(value);
            field.AddToClassList("ux-slot__field");
            field.RegisterValueChangedCallback(evt => set(evt.newValue));
            line.Add(field);
            var p = MakeButton("▶", "ux-slot__btn", () => play(), playTip);
            line.Add(p);
            var s = MakeButton("■", "ux-slot__btn", () => stop(), stopTip);
            line.Add(s);
            return line;
        }

        private void StopVfxOnly()
        {
            if (_driver != null && _driver.IsPlaying(_handle))
            {
                _driver.Kill(_handle);
            }

            _handle = DDrive.Foundation.Handle.Handle<VfxMarker>.Invalid;
            if (_tryStatus != null)
            {
                _tryStatus.text = string.Empty;
            }
        }

        // ── 既存のヒエラルキーから作る ──

        private VisualElement CreateCreateBox()
        {
            var box = new VisualElement();
            var line = AnchorUi.ToolsLine();
            line.Add(MakeButton("選択した Transform から作成", null, CreateFromSelection, "Hierarchy で選んだ Transform から、その親を基準にしたオフセットを持つ Anchor を作る"));
            line.Add(MakeButton("AnchorRig から一括生成", null, CreateFromRig, "Hierarchy で選んだ AnchorRig の AnchorPoint ごとに Anchor を作り、親子関係を Parent の連鎖として写す"));
            box.Add(line);
            return box;
        }

        private void CreateFromSelection()
        {
            var t = Selection.activeTransform;
            if (t == null)
            {
                Debug.LogWarning("[DDrive] Hierarchy で Transform を選択してください。");
                return;
            }

            var asset = AnchorAssetFactory.CreateFromTransform(t, CategoryFor(t.root.name));
            if (asset != null)
            {
                AfterCreated(asset);
            }
        }

        private void CreateFromRig()
        {
            var go = Selection.activeGameObject;
            if (go == null || go.GetComponentInChildren<AnchorPoint>(true) == null)
            {
                Debug.LogWarning("[DDrive] AnchorPoint を含む AnchorRig を選択してください。");
                return;
            }

            var created = AnchorAssetFactory.CreateFromRig(go);
            if (created.Count > 0)
            {
                AfterCreated(created[0]);
            }
        }

        private void AfterCreated(AnchorData asset)
        {
            EditorAnchorRegistry.Refresh(_driver?.Registry);
            SetTarget(asset);
            EditorGUIUtility.PingObject(asset);
        }

        private static string CategoryFor(string rootName)
        {
            var id = AnchorAssetFactory.ToIdentifier(rootName);
            return string.IsNullOrEmpty(id) ? AnchorAssetFactory.DefaultCategory : id;
        }

        // ── SceneView(連鎖・ハンドル) ──

        protected override bool WantsSceneGui => _sceneHandle;

        // SceneView の描画権: 最後にフォーカスしたウィンドウだけが連鎖・ハンドルを描く(SceneGuiOwner。基底が OnFocus で Claim する)。
        protected override void DrawSceneGui()
        {
            var chain = Chain;
            if (chain.Count == 0)
            {
                return;
            }

            var attach = AttachTransform;
            var rootDef = chain[0].ToDef();
            var baseTransform = AnchorResolver.Resolve(rootDef, attach);
            var extraOffset = Vector3.zero;
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            var color = new Color(0.95f, 0.75f, 0.3f);
            var targetDef = AnchorChainEditor.ComposeUpTo(chain, chain.Count - 1);

            // 描画権が他のウィンドウにあるときは対象の薄い目印だけ(クリックするとこのウィンドウが描画権を持つ)。
            if (!SceneGuiOwner.IsOwner(this))
            {
                if (AnchorSceneHandles.DrawClickableMarker(targetDef, baseTransform, extraOffset, $"Anchor: {Target.name}", color, active: false))
                {
                    SceneGuiOwner.Claim(this);
                    Focus();
                }

                return;
            }

            // 基準(原点) → 連鎖の各段 → 最終位置。どこを起点にしたオフセットなのかを 1 枚の絵で見せる(U-24)。
            var originWorld = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(rootDef, baseTransform), rootDef.FollowRotation);
            var parentWorld = AnchorSceneHandles.DrawChain(chain, baseTransform, extraOffset, originWorld, color);
            AnchorSceneHandles.DrawOffsetLink(parentWorld, AnchorPose.WorldPosition(targetDef, baseTransform, extraOffset), Target.LocalOffset, color);
            var result = AnchorSceneHandles.Draw(targetDef, baseTransform, extraOffset, $"Anchor: {Target.name}", color);
            if (!result.PositionChanged && !result.RotationChanged)
            {
                return;
            }

            AnchorDef? parentDef = chain.Count > 1 ? AnchorChainEditor.ComposeUpTo(chain, chain.Count - 2) : null;
            Undo.RecordObject(Target, "Move Anchor");
            if (result.PositionChanged)
            {
                Target.LocalOffset = AnchorChainEditor.ToChildLocalOffset(parentDef, result.LocalOffset);
            }

            if (result.RotationChanged)
            {
                Target.LocalEuler = AnchorChainEditor.ToChildLocalEuler(parentDef, result.LocalEuler);
            }

            EditorUtility.SetDirty(Target);
            So?.Update();
            VisualEdited(false, true); // ドラッグ中は検証を省く(つかみが終わったら OnTick がまとめて検証)
            _handleDirty = true;
        }
    }
}
