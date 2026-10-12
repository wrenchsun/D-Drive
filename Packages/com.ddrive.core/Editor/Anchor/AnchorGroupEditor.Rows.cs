using System;
using System.Collections.Generic;
using System.Text;
using DDrive.Editor.Anim;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Preview;
using DDrive.Editor.Ux;
using DDrive.Editor.Vfx;
using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anchor
{
    // 欄の組み立て(配置セット固有の行): 試し出し・スポーン先と状態・点の一覧(1 点 = 1 行)・パターンごとの欄の出し分け・手置きへの変換・原点の連鎖。
    internal sealed partial class AnchorGroupEditor
    {
        private static readonly Dictionary<string, AnchorLayoutKind> PatternFields = new Dictionary<string, AnchorLayoutKind>
        {
            { nameof(AnchorGroupData.GridCountX), AnchorLayoutKind.Grid },
            { nameof(AnchorGroupData.GridCountY), AnchorLayoutKind.Grid },
            { nameof(AnchorGroupData.GridCountZ), AnchorLayoutKind.Grid },
            { nameof(AnchorGroupData.GridSpacing), AnchorLayoutKind.Grid },
            { nameof(AnchorGroupData.GridCentered), AnchorLayoutKind.Grid },
            { nameof(AnchorGroupData.CircleCount), AnchorLayoutKind.Circle },
            { nameof(AnchorGroupData.CircleRadius), AnchorLayoutKind.Circle },
            { nameof(AnchorGroupData.CircleStartAngle), AnchorLayoutKind.Circle },
            { nameof(AnchorGroupData.CircleArc), AnchorLayoutKind.Circle },
            { nameof(AnchorGroupData.CircleFaceOutward), AnchorLayoutKind.Circle },
            { nameof(AnchorGroupData.LineCount), AnchorLayoutKind.Line },
            { nameof(AnchorGroupData.LineLength), AnchorLayoutKind.Line },
            { nameof(AnchorGroupData.LineDirection), AnchorLayoutKind.Line },
            { nameof(AnchorGroupData.LineCentered), AnchorLayoutKind.Line },
            { nameof(AnchorGroupData.RandomCount), AnchorLayoutKind.Random },
            { nameof(AnchorGroupData.RandomRadius), AnchorLayoutKind.Random },
            { nameof(AnchorGroupData.RandomSeed), AnchorLayoutKind.Random },
        };

        protected override UxRow CreateRow(FieldGuideEntry e, UxCard card, bool isAdmin)
        {
            switch (e.Field)
            {
                case AnchorGroupFieldGuide.RowTry:
                    return new AnchorAuxRow(e, CreateTryBox(), RefreshSelectionUi, true);

                case AnchorGroupFieldGuide.RowTools:
                    return CreateToolsRow(e);

                case AnchorGroupFieldGuide.RowPoints:
                    return CreatePointsRow(e);

                case AnchorGroupFieldGuide.RowConvert:
                {
                    var row = new AnchorAuxRow(e, CreateConvertBox(), null);
                    _conditional.Add(new ConditionalRow { Row = row, Visible = () => AnchorGroupPointConverter.CanConvert(Target) });
                    return row;
                }

                case AnchorGroupFieldGuide.RowOrigin:
                    return CreateOriginRow(e);

                case nameof(AnchorGroupData.Origin):
                case nameof(AnchorGroupData.Points):
                case nameof(AnchorGroupData.Overrides):
                case nameof(AnchorGroupData.Children):
                case nameof(AnchorGroupData.Flags):
                case nameof(AnchorGroupData.Events):
                    GuidedField.Collapse(So, e.Field);
                    return base.CreateRow(e, card, isAdmin);

                default:
                {
                    var row = base.CreateRow(e, card, isAdmin);
                    if (row != null && PatternFields.TryGetValue(e.Field, out var kind))
                    {
                        _conditional.Add(new ConditionalRow { Row = row, Visible = () => Target != null && Target.Layout == kind });
                    }

                    return row;
                }
            }
        }

        // ── 試し出し ──

        private VisualElement CreateTryBox()
        {
            var box = new VisualElement();
            var line = AnchorUi.ToolsLine();
            line.Add(MakeButton("▶ 全点", "ux-btn--primary", () => PlayMain(false), "全点を実際に出す(ランダム・ディレイ・確率は毎回サンプリングされる)"));
            line.Add(MakeButton("▶ 選択点のみ", null, PlaySelected, "点の一覧または SceneView で選んだ 1 点だけを出す"));
            line.Add(MakeButton("■ 停止", null, () => StopMain(), "出している VFX / SE を止める"));
            box.Add(line);

            _selectionLabel = AnchorUi.Note(string.Empty);
            box.Add(_selectionLabel);
            var line2 = AnchorUi.ToolsLine();
            _overrideButton = MakeButton("選択点を上書きに追加(この点だけ変える / 出さない)", null, () => AddOverride(_selectedIndex, false), "選んだ点の番号を Overrides に足す(追加後に「出すもの」で Skip や別の VFX / SE を設定する)");
            line2.Add(_overrideButton);
            line2.Add(MakeButton("選択解除", null, () => SelectPoint(-1), "点の選択を外す"));
            box.Add(line2);
            RefreshSelectionUi();
            return box;
        }

        private void PlaySelected()
        {
            if (_selectedIndex >= 0)
            {
                PlayPreview(_selectedIndex);
            }
        }

        private void RefreshSelectionUi()
        {
            if (_selectionLabel == null)
            {
                return;
            }

            string text;
            var has = Target != null && _selectedIndex >= 0 && _selectedIndex < _pointCount;
            if (!has)
            {
                text = "選択中の点: なし";
            }
            else
            {
                var kind = KindOf(_selectedIndex);
                var overrideIndex = Target.FindOverride(_selectedIndex);
                var overrideText = overrideIndex >= 0 ? (Target.Overrides[overrideIndex].Skip ? "(出さない)" : "(上書きあり)") : string.Empty;
                text = $"選択中の点: #{_selectedIndex} [{kind}] {overrideText}  ローカル {AnchorUi.ShortVector(_specs[_selectedIndex].Def.LocalOffset)}";
            }

            if (_selectionLabel.text != text)
            {
                _selectionLabel.text = text;
            }

            _overrideButton?.SetEnabled(has && Target.FindOverride(_selectedIndex) < 0);
        }

        private string KindOf(int index) => index >= _pointCount - ManualCount ? "手置き" : Target.Layout.ToString();

        private void SelectPoint(int index)
        {
            _selectedIndex = index == _selectedIndex ? -1 : index;
            RefreshSelectionUi();
            RefreshPointHighlights();
            SceneView.RepaintAll();
            Repaint();
        }

        // ── スポーン先と今の状態 ──

        private UxRow CreateToolsRow(FieldGuideEntry e)
        {
            _toolsRow = new VfxAnchorToolsRow(
                e,
                () => _attachTarget,
                go =>
                {
                    _attachTarget = go;
                    _toolsRow?.Sync();
                    SceneView.RepaintAll();
                },
                () => _sceneHandle,
                v =>
                {
                    _sceneHandle = v;
                    SceneView.RepaintAll();
                },
                StatusText,
                () => SceneGuiOwner.DescribeFor(this),
                (menu, attach) => VfxAnchorSceneGui.FillPathMenu(menu.menu, attach, SelectOriginPath),
                e.Label,
                e.Hint);
            return _toolsRow;
        }

        private string StatusText()
        {
            if (Target == null)
            {
                return string.Empty;
            }

            var origin = AnchorUi.ResolutionText("原点:", OriginDef(), AttachTransform);
            return origin + $"  / 点: {_pointCount}  / 全点共通: VFX {CountValid(Target.SharedVfx)}・SE {CountValid(Target.SharedSe)}" +
                   (Target.Children != null && Target.Children.Length > 0 ? $"  / 入れ子: {Target.Children.Length}" : string.Empty) +
                   (Target.DelayPerIndex > 0f ? $"  / 番号順ディレイ {Target.DelayPerIndex:0.##}s" : string.Empty) +
                   (Target.ChancePerPoint < 1f ? $"  / 確率 {Target.ChancePerPoint:P0}" : string.Empty);
        }

        private static int CountValid<T>(DDrive.Foundation.Identity.AssetId<T>[] ids)
        {
            var n = 0;
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    if (id.IsValid)
                    {
                        n++;
                    }
                }
            }

            return n;
        }

        // 「Path を一覧から選ぶ」: 埋め込みの原点の Path を書く(原点に Anchor アセットを使っているときは書かない = そちらの Anchor で決まる)。
        private void SelectOriginPath(string name)
        {
            if (Target == null)
            {
                return;
            }

            if (Target.OriginAnchorId.IsValid)
            {
                Debug.LogWarning("[DDrive] 原点に Anchor アセットが設定されているため、埋め込みの原点の Path は使われません。Anchor Editor で Anchor の Path を変えてください。");
                return;
            }

            Undo.RecordObject(Target, "Change Anchor Group Origin Path");
            var o = Target.Origin;
            o.Path = name;
            if (o.Space == AnchorSpace.World || o.Space == AnchorSpace.ContextTarget)
            {
                o.Space = AnchorSpace.NamedObject;
            }

            Target.Origin = o;
            EditorUtility.SetDirty(Target);
            So?.Update();
            RowChanged();
            SceneView.RepaintAll();
        }

        // ── 手置きの点に変換(U-22) ──

        private VisualElement CreateConvertBox()
        {
            var box = new VisualElement();
            var line = AnchorUi.ToolsLine();
            line.Add(MakeButton("手置きの点に変換", null, ConvertPatternToPoints,
                "今のパターンで計算された点を、そのまま手置きの点(Points)として書き込み、Layout を Manual に切り替える。点を 1 つずつ動かせるようになる。Ctrl+Z で元に戻せる"));
            box.Add(line);
            return box;
        }

        private void ConvertPatternToPoints()
        {
            if (!AnchorGroupPointConverter.CanConvert(Target))
            {
                return;
            }

            RefreshPoints();
            var message =
                $"「{Target.Layout}」パターンで計算された {_pointCount} 点を、手置きの点(Points)として書き込みます。\n\n" +
                "・Layout は Manual になり、点を 1 つずつ SceneView でドラッグして調整できます\n" +
                "・点の番号と並び順は変わらないので、Overrides / 入れ子の指定はそのまま使えます\n" +
                "・パターンの設定値(間隔・半径・長さなど)は残るので、Layout を戻せばやり直せます\n" +
                "・取り消しは Ctrl+Z(Undo)でできます";
            if (!EditorUtility.DisplayDialog("手置きの点に変換", message, "変換する", "キャンセル"))
            {
                return;
            }

            ConvertPatternToPointsNow();
        }

        // 確認のダイアログなしで変換する(テスト・内部用)。
        internal int ConvertPatternToPointsNow()
        {
            var n = AnchorGroupPointConverter.Convert(Target);
            if (n > 0)
            {
                So?.Update();
                RowChanged();
                _pointBox?.Sync();
            }

            return n;
        }

        // ── 原点の連鎖(親子合成の確認) ──

        private List<AnchorData> OriginChain
        {
            get
            {
                if (_originChain == null)
                {
                    var asset = Target != null && Target.OriginAnchorId.IsValid ? EditorAnchorRegistry.Find(Target.OriginAnchorId.Value) : null;
                    _originChain = asset != null ? AnchorChainEditor.CollectRootToTarget(asset) : new List<AnchorData>();
                }

                return _originChain;
            }
        }

        private UxRow CreateOriginRow(FieldGuideEntry e)
        {
            AnimSyncBox box = null;
            box = new AnimSyncBox(OriginSignature, () => RebuildOrigin(box), null);
            box.Rebuild();
            return new AnchorAuxRow(e, box, box.Sync);
        }

        private int OriginSignature()
        {
            if (Target == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, Target.OriginAnchorId.Value);
            var chain = OriginChain;
            for (var i = 0; i < chain.Count; i++)
            {
                h = CanvasSig.Add(h, chain[i].GetInstanceID());
                h = CanvasSig.Add(h, chain[i].name);
                h = CanvasSig.Add(h, chain[i].LocalOffset.x);
                h = CanvasSig.Add(h, chain[i].LocalOffset.y);
                h = CanvasSig.Add(h, chain[i].LocalOffset.z);
            }

            return h;
        }

        private void RebuildOrigin(VisualElement box)
        {
            box.Clear();
            if (Target == null)
            {
                return;
            }

            if (!Target.OriginAnchorId.IsValid)
            {
                box.Add(AnchorUi.Note("埋め込みの原点(Origin)を使っています。「原点 Anchor アセット」を指定すると、その連鎖と合成後のオフセットがここに出ます。"));
                return;
            }

            var chain = OriginChain;
            if (chain.Count == 0)
            {
                var warn = new Label($"⚠ 原点の Anchor 0x{Target.OriginAnchorId.Value:X} のアセットが見つかりません(World 原点扱い)");
                warn.AddToClassList("ux-lineflag");
                box.Add(warn);
                return;
            }

            var line = AnchorUi.ToolsLine();
            line.AddToClassList("ux-anc-chain");
            line.Add(AnchorUi.Note("連鎖:"));
            for (var i = 0; i < chain.Count; i++)
            {
                var node = chain[i];
                line.Add(MakeButton(node.name, null, () => AnchorEditor.Open(node), "この Anchor を Anchor Editor で開く"));
                if (i < chain.Count - 1)
                {
                    line.Add(AnchorUi.Note("→"));
                }
            }

            box.Add(line);
            var def = AnchorChainEditor.ComposeUpTo(chain, chain.Count - 1);
            box.Add(AnchorUi.Note($"合成後の原点: 基準 {def.Space}{(string.IsNullOrEmpty(def.Path) ? string.Empty : " '" + def.Path + "'")} から オフセット {AnchorUi.ShortVector(def.LocalOffset)}・向き {AnchorUi.ShortVector(def.LocalEuler)}"));
            if (chain[0].Parent.IsValid)
            {
                var warn = new Label("⚠ ルートに辿り着けません(未登録または循環)");
                warn.AddToClassList("ux-lineflag");
                box.Add(warn);
            }
        }

        // ── 点の一覧(1 点 = 1 行) ──

        private UxRow CreatePointsRow(FieldGuideEntry e)
        {
            _pointList = new CanvasItemList("点", "上書きあり", PointKinds, "点がありません。配置パターンの数を設定するか、手置きの点を足してください。", "そのまま");
            var box = new AnimSyncBox(PointsSignature, RebuildPoints, null);
            box.Add(_pointList);
            _pointBox = box;
            RefreshPoints();
            box.Rebuild();
            var row = new AnchorAuxRow(e, box, box.Sync, true);
            row.ExtraMatch = q => _pointList != null && _pointList.AnyMatch(q);
            return row;
        }

        private int PointsSignature()
        {
            if (Target == null)
            {
                return 0;
            }

            RefreshPoints();
            var h = CanvasSig.Add(17, _pointCount);
            h = CanvasSig.Add(h, (int)Target.Layout);
            for (var i = 0; i < _pointCount; i++)
            {
                var o = _specs[i].Def.LocalOffset;
                h = CanvasSig.Add(h, o.x);
                h = CanvasSig.Add(h, o.y);
                h = CanvasSig.Add(h, o.z);
            }

            var overrides = Target.Overrides;
            if (overrides != null)
            {
                for (var k = 0; k < overrides.Length; k++)
                {
                    h = CanvasSig.Add(h, overrides[k].Index);
                    h = CanvasSig.Add(h, overrides[k].Skip ? 1 : 0);
                    h = CanvasSig.Add(h, overrides[k].Vfx?.Length ?? 0);
                    h = CanvasSig.Add(h, overrides[k].Se?.Length ?? 0);
                }
            }

            var points = Target.Points;
            if (points != null)
            {
                for (var k = 0; k < points.Length; k++)
                {
                    h = CanvasSig.Add(h, points[k].Name);
                }
            }

            return h;
        }

        private void RebuildPoints()
        {
            if (_pointList == null)
            {
                return;
            }

            _pointElements.Clear();
            var items = new List<CanvasListItem>(_pointCount);
            var manualStart = _pointCount - ManualCount;
            for (var i = 0; i < _pointCount; i++)
            {
                var index = i;
                items.Add(new CanvasListItem
                {
                    Key = "pt:" + i,
                    Title = PointTitle(i, manualStart),
                    Kind = i >= manualStart ? 1 : 0,
                    Configured = Target.FindOverride(i) >= 0,
                    Search = "#" + i + " " + PointSummary(i),
                    Make = () => MakePointRow(index),
                });
            }

            _pointList.SetItems(items);
        }

        private string PointTitle(int index, int manualStart)
        {
            if (index >= manualStart)
            {
                var name = Target.Points[index - manualStart].Name;
                return string.IsNullOrEmpty(name) ? "手置き " + (index - manualStart) : name;
            }

            switch (Target.Layout)
            {
                case AnchorLayoutKind.Grid:
                {
                    var cx = Mathf.Max(1, Target.GridCountX);
                    var cz = Mathf.Max(1, Target.GridCountZ);
                    var cy = Mathf.Max(1, Target.GridCountY);
                    var title = $"横 {index % cx + 1} × 奥 {(index / cx) % cz + 1}";
                    return cy > 1 ? title + $" × 段 {index / (cx * cz) + 1}" : title;
                }

                case AnchorLayoutKind.Circle:
                {
                    var n = Mathf.Max(1, Target.CircleCount);
                    var arc = Mathf.Clamp(Target.CircleArc, 0f, 360f);
                    var step = arc >= 360f - 1e-3f ? 360f / n : (n > 1 ? arc / (n - 1) : 0f);
                    return $"{Mathf.Repeat(Target.CircleStartAngle + step * index, 360f):0.#}°";
                }

                case AnchorLayoutKind.Line:
                    return $"{index + 1} / {Mathf.Max(1, Target.LineCount)}";

                default:
                    return $"{Target.Layout} {index + 1}";
            }
        }

        private string PointSummary(int index)
        {
            var sb = new StringBuilder();
            sb.Append("ローカル ").Append(AnchorUi.ShortVector(_specs[index].Def.LocalOffset));
            var k = Target.FindOverride(index);
            if (k >= 0)
            {
                var o = Target.Overrides[k];
                sb.Append(o.Skip ? " · 出さない" : $" · 上書き: VFX {o.Vfx?.Length ?? 0} / SE {o.Se?.Length ?? 0}");
            }

            var children = 0;
            if (Target.Children != null)
            {
                for (var c = 0; c < Target.Children.Length; c++)
                {
                    if (Target.Children[c].Group.IsValid && (Target.Children[c].AtIndex < 0 || Target.Children[c].AtIndex == index))
                    {
                        children++;
                    }
                }
            }

            if (children > 0)
            {
                sb.Append(" · 入れ子 ").Append(children);
            }

            return sb.ToString();
        }

        private VisualElement MakePointRow(int index)
        {
            var manualStart = _pointCount - ManualCount;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--hl", index == _selectedIndex);
            _pointElements[index] = row;

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            head.AddToClassList("ux-anc-pt");
            head.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                {
                    return;
                }

                SelectPoint(index);
            });

            var idx = new Label("#" + index) { tooltip = "点の番号(SceneView の番号・Overrides の Index と同じ)" };
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var manual = index >= manualStart;
            var chip = new Label(manual ? "手置き" : Target.Layout.ToString()) { tooltip = manual ? "手置きの点(Points)" : "パターンで自動配置された点" };
            chip.AddToClassList("ux-chip");
            chip.AddToClassList("ux-fxrow__chip");
            chip.AddToClassList(manual ? "ux-kind--2" : "ux-kind--0");
            head.Add(chip);

            var title = PointTitle(index, manualStart);
            var name = new Label(title) { tooltip = "クリックでこの点を選ぶ(SceneView の選択と共通)" };
            name.AddToClassList("ux-fxrow__name");
            head.Add(name);

            var summary = new Label(PointSummary(index));
            summary.AddToClassList("ux-dim");
            summary.AddToClassList("ux-fxrow__summary");
            summary.tooltip = summary.text;
            head.Add(summary);

            var overrideIndex = Target.FindOverride(index);
            var skipped = overrideIndex >= 0 && Target.Overrides[overrideIndex].Skip;

            var play = new Button(() => PlayPreview(index)) { text = "▶", tooltip = "この点だけ試し出し(実際の VFX / SE)" };
            play.AddToClassList("ux-btn");
            play.AddToClassList("ux-btn--icon");
            play.SetEnabled(!skipped);
            head.Add(play);

            var skip = new Button(() => ToggleSkip(index)) { text = skipped ? "↺" : "×", tooltip = skipped ? "この点を出すように戻す(Overrides の Skip を外す)" : "この点を出さない(Overrides に Skip を足す)" };
            skip.AddToClassList("ux-btn");
            skip.AddToClassList("ux-btn--icon");
            head.Add(skip);

            var add = new Button(() => AddOverride(index, false)) { text = "＋", tooltip = "この点を上書きに追加(Overrides に番号を足し、「出すもの」で別の VFX / SE にする)" };
            add.AddToClassList("ux-btn");
            add.AddToClassList("ux-btn--icon");
            add.SetEnabled(overrideIndex < 0);
            head.Add(add);

            row.Add(head);
            return row;
        }

        private void RefreshPointHighlights()
        {
            foreach (var kv in _pointElements)
            {
                kv.Value.EnableInClassList("ux-fxrow--hl", kv.Key == _selectedIndex);
            }
        }

        // ── 上書き(Overrides)の追加・Skip の切り替え ──

        private void AddOverride(int index, bool skip)
        {
            if (Target == null || index < 0 || Target.FindOverride(index) >= 0)
            {
                return;
            }

            Undo.RecordObject(Target, "Add Anchor Group Override");
            var list = new List<AnchorGroupOverride>(Target.Overrides ?? Array.Empty<AnchorGroupOverride>())
            {
                new AnchorGroupOverride { Index = index, Skip = skip },
            };
            Target.Overrides = list.ToArray();
            EditorUtility.SetDirty(Target);
            So?.Update();
            RowChanged();
            _pointBox?.Sync();
        }

        private void ToggleSkip(int index)
        {
            if (Target == null || index < 0)
            {
                return;
            }

            var k = Target.FindOverride(index);
            if (k < 0)
            {
                AddOverride(index, true);
                return;
            }

            Undo.RecordObject(Target, "Toggle Anchor Group Skip");
            Target.Overrides[k].Skip = !Target.Overrides[k].Skip;
            EditorUtility.SetDirty(Target);
            So?.Update();
            RowChanged();
            _pointBox?.Sync();
        }

        // ── SceneView(全点の番号付きギズモ・クリック選択・ハンドル) ──

        protected override bool WantsSceneGui => _sceneHandle;

        protected override void DrawSceneGui()
        {
            RefreshPoints();
            var attach = AttachTransform;
            var originDef = OriginDef();
            var baseTransform = AnchorResolver.Resolve(originDef, attach);
            var extraOffset = Vector3.zero;
            if (baseTransform != null && baseTransform.TryGetComponent<AnchorPoint>(out var point))
            {
                extraOffset = point.SpawnOffset;
            }

            var color = new Color(0.4f, 0.8f, 1f);
            var originPos = AnchorPose.WorldPosition(originDef, baseTransform, extraOffset);

            // 描画権が他のウィンドウにあるときは薄い目印だけ。
            if (!SceneGuiOwner.IsOwner(this))
            {
                Handles.color = new Color(color.r, color.g, color.b, 0.25f);
                for (var i = 0; i < _pointCount; i++)
                {
                    var p = AnchorPose.WorldPosition(_specs[i].Def, baseTransform, extraOffset);
                    Handles.DrawWireDisc(p, Vector3.up, HandleUtility.GetHandleSize(p) * 0.1f);
                }

                Handles.Label(originPos, $"Group: {Target.name}", EditorStyles.miniLabel);
                return;
            }

            // 基準(解決先 Transform。未解決ならワールド原点)→ 原点(基準 + Origin の LocalOffset)(U-24)
            var baseWorld = AnchorSceneHandles.DrawOrigin(baseTransform, extraOffset, AnchorSceneHandles.DescribeBase(originDef, baseTransform), originDef.FollowRotation);
            AnchorSceneHandles.DrawOffsetLink(baseWorld, originPos, originDef.LocalOffset, color);

            Handles.color = color;
            var originSize = HandleUtility.GetHandleSize(originPos);
            Handles.DrawWireDisc(originPos, Vector3.up, originSize * 0.3f);
            Handles.Label(originPos + Vector3.up * originSize * 0.4f, $"Group: {Target.name}(原点)");

            // 各点(クリックで選択)
            var manualStart = _pointCount - ManualCount;
            for (var i = 0; i < _pointCount; i++)
            {
                var p = AnchorPose.WorldPosition(_specs[i].Def, baseTransform, extraOffset);
                var size = HandleUtility.GetHandleSize(p) * 0.12f;
                var overrideIndex = Target.FindOverride(i);
                var skipped = overrideIndex >= 0 && Target.Overrides[overrideIndex].Skip;
                Handles.color = i == _selectedIndex ? Color.yellow : skipped ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : (overrideIndex >= 0 ? new Color(1f, 0.6f, 0.3f) : color);
                if (Handles.Button(p, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                {
                    _selectedIndex = i;
                    RefreshSelectionUi();
                    RefreshPointHighlights();
                    Repaint();
                }

                Handles.Label(p + Vector3.up * size * 1.6f, skipped ? $"{i} ×" : i.ToString(), EditorStyles.miniLabel);
                if (i >= manualStart)
                {
                    Handles.DrawDottedLine(originPos, p, 3f);
                }
            }

            if (_selectedIndex < 0 || _selectedIndex >= _pointCount)
            {
                return;
            }

            // 選択点のハンドル: 手置きは直接、パターンは大きさ(間隔 / 半径 / 長さ)を変える。
            var selectedWorld = AnchorPose.WorldPosition(_specs[_selectedIndex].Def, baseTransform, extraOffset);
            EditorGUI.BeginChangeCheck();
            var newWorld = Handles.PositionHandle(selectedWorld, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            // ワールド → 原点ローカル(原点の回転・スケール込みで戻す)
            var composed = AnchorPose.LocalOffsetFromWorld(baseTransform, newWorld, extraOffset);
            var originRot = Quaternion.Euler(originDef.LocalEuler);
            var originScale = originDef.LocalScale == Vector3.zero ? Vector3.one : originDef.LocalScale;
            var local = Quaternion.Inverse(originRot) * (composed - originDef.LocalOffset);
            local = new Vector3(
                originScale.x != 0f ? local.x / originScale.x : 0f,
                originScale.y != 0f ? local.y / originScale.y : 0f,
                originScale.z != 0f ? local.z / originScale.z : 0f);

            Undo.RecordObject(Target, "Move Anchor Group Point");
            if (_selectedIndex >= manualStart)
            {
                Target.Points[_selectedIndex - manualStart].LocalOffset = local;
            }
            else
            {
                ApplyPatternDrag(local);
            }

            EditorUtility.SetDirty(Target);
            So?.Update();
            VisualEdited(false, true);
            _handleDirty = true;
        }

        // 選択したパターン点のドラッグを、パターンの大きさに変換する。
        private void ApplyPatternDrag(Vector3 local)
        {
            switch (Target.Layout)
            {
                case AnchorLayoutKind.Grid:
                {
                    // 端の点(角)からの距離で間隔を決める。中央寄せなら半分が距離。
                    var cx = Mathf.Max(1, Target.GridCountX);
                    var cy = Mathf.Max(1, Target.GridCountY);
                    var cz = Mathf.Max(1, Target.GridCountZ);
                    var ix = _selectedIndex % cx;
                    var iz = (_selectedIndex / cx) % cz;
                    var iy = _selectedIndex / (cx * cz);
                    var spacing = Target.GridSpacing;
                    var fx = Target.GridCentered ? ix - (cx - 1) * 0.5f : ix;
                    var fz = Target.GridCentered ? iz - (cz - 1) * 0.5f : iz;
                    var fy = Target.GridCentered ? iy - (cy - 1) * 0.5f : iy;
                    if (Mathf.Abs(fx) > 1e-3f) spacing.x = Mathf.Max(0f, local.x / fx);
                    if (Mathf.Abs(fz) > 1e-3f) spacing.z = Mathf.Max(0f, local.z / fz);
                    if (Mathf.Abs(fy) > 1e-3f) spacing.y = Mathf.Max(0f, local.y / fy);
                    Target.GridSpacing = spacing;
                    break;
                }
                case AnchorLayoutKind.Circle:
                    Target.CircleRadius = new Vector2(local.x, local.z).magnitude;
                    break;
                case AnchorLayoutKind.Line:
                {
                    var dir = Target.LineDirection.sqrMagnitude > 1e-6f ? Target.LineDirection.normalized : Vector3.forward;
                    var n = Mathf.Max(1, Target.LineCount);
                    if (n > 1)
                    {
                        var t = _selectedIndex / (float)(n - 1);
                        var along = Vector3.Dot(local, dir);
                        var f = Target.LineCentered ? t - 0.5f : t;
                        if (Mathf.Abs(f) > 1e-3f)
                        {
                            Target.LineLength = Mathf.Max(0f, along / f);
                        }
                    }

                    break;
                }
                case AnchorLayoutKind.Random:
                    Target.RandomRadius = Mathf.Max(Target.RandomRadius, local.magnitude);
                    break;
            }
        }
    }
}
