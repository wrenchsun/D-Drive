using System;
using System.Collections.Generic;
using DDrive.Editor.CanvasTool;
using DDrive.Editor.Ux;
using DDrive.Runtime.Anim;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Anim
{
    // 「表情・手足(BlendShape・IK)」の欄(docs/1010 UX-6)。
    // IK = 手足 4 つのトグル(左手 / 右手 / 左足 / 右足)+ ウェイトカーブ(正規化時間 → 0〜1、空なら常に 1)。
    // BlendShape = 1 シェイプ = 1 行(名前・「カーブ 3 キー · 0 → 100」の 1 行要約・⚙・✕)。展開はその行だけで、ShapeName(モデルのシェイプから選べる)と Weight カーブ。
    // モデルに無い名前・外部管理(FC_ / fcs_)のシェイプ・名前が空の行は ⚠ で知らせる。一覧の上に絞り込み(検索・設定済み / 未設定)と件数。
    internal sealed partial class AnimEditor
    {
        private sealed class ShapeHead
        {
            public Label Name;
            public Label Summary;
        }

        private CanvasItemList _shapeList;
        private AnimSyncBox _shapeSection;
        private List<CanvasListItem> _shapeItems = new();
        private readonly Dictionary<int, Label> _shapeFlags = new();
        private readonly Dictionary<int, ShapeHead> _shapeHeads = new();
        private int _openShape = -1;

        // ── IK ──

        private UxRow CreateIkRow(FieldGuideEntry e)
        {
            var box = new VisualElement();
            var line = new VisualElement();
            line.AddToClassList("ux-toolsline");
            AddIkToggle(line, "LeftHand", "左手");
            AddIkToggle(line, "RightHand", "右手");
            AddIkToggle(line, "LeftFoot", "左足");
            AddIkToggle(line, "RightFoot", "右足");
            box.Add(line);

            var weight = So.FindProperty("Ik.Weight");
            if (weight != null)
            {
                var curve = new CurveField { tooltip = "正規化時間(0〜1)→ IK ウェイト(0〜1)。空なら常に 1" };
                curve.AddToClassList("ux-ikcurve");
                curve.BindProperty(weight);
                box.Add(curve);
            }

            return new UxRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, true, box, RowChanged);
        }

        private void AddIkToggle(VisualElement line, string field, string label)
        {
            var prop = So.FindProperty("Ik." + field);
            if (prop == null)
            {
                return;
            }

            var toggle = new Toggle { text = label };
            toggle.AddToClassList("ux-iktoggle");
            toggle.BindProperty(prop);
            line.Add(toggle);
        }

        // ── BlendShape の一覧 ──

        private UxRow CreateShapeRows(FieldGuideEntry e)
        {
            _shapeList = new CanvasItemList("シェイプ", "設定済み", null, "BlendShape がありません。「＋ BlendShape」か「＋ モデルのシェイプから」で追加してください。");
            var box = new AnimSyncBox(ShapeSignature, RebuildShapeList, null);

            var bar = new VisualElement();
            bar.AddToClassList("ux-toolsline");
            bar.Add(MakeButton("＋ BlendShape", "ux-btn--primary", () => AddShape(string.Empty), "名前の空のシェイプを追加する(展開して ShapeName と Weight カーブを決める)"));
            var fromModel = MakeButton("＋ モデルのシェイプから", null, ShowShapeMenu, "対象モデルが持つ BlendShape の名前から選んで追加する(外部管理の FC_ / fcs_ は除く)");
            bar.Add(fromModel);
            box.Add(bar);
            box.Add(_shapeList);

            _shapeSection = box;
            box.Rebuild();
            return new UxVisualRow(So, DefaultSo, new[] { e }, e.Label, e.Hint, e.Tier, box, RowChanged)
            {
                NoFold = true,
                NoBulkReset = true,
                MaxIssues = 3,
                ExtraMatch = q => _shapeList != null && _shapeList.AnyMatch(q),
            };
        }

        private int ShapeSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var shapes = t.BlendShapes;
            var h = CanvasSig.Add(17, shapes != null ? shapes.Length : 0);
            for (var i = 0; shapes != null && i < shapes.Length; i++)
            {
                h = CanvasSig.Add(h, shapes[i].ShapeName);
            }

            return h;
        }

        private void RebuildShapeList()
        {
            if (_shapeList == null)
            {
                return;
            }

            _shapeHeads.Clear();
            _shapeFlags.Clear();
            _shapeItems = CollectShapeItems();
            _shapeList.SetItems(_shapeItems);
            ApplyShapeFlags();
        }

        private List<CanvasListItem> CollectShapeItems()
        {
            var items = new List<CanvasListItem>();
            var shapes = Target?.BlendShapes;
            for (var i = 0; shapes != null && i < shapes.Length; i++)
            {
                var index = i;
                items.Add(new CanvasListItem
                {
                    Key = "shape:" + i,
                    Title = ShapeTitle(in shapes[i]),
                    IsConfigured = () => ShapeConfigured(index),
                    Search = ShapeSummary(in shapes[i]),
                    Make = () => MakeShapeRow(index),
                });
            }

            return items;
        }

        private static string ShapeTitle(in BlendShapeTrack t) => string.IsNullOrEmpty(t.ShapeName) ? "(名前なし)" : t.ShapeName;

        // 「設定済み」: 名前があり、ウェイトのカーブにキーがある。
        private bool ShapeConfigured(int index)
        {
            var shapes = Target?.BlendShapes;
            if (shapes == null || index < 0 || index >= shapes.Length)
            {
                return false;
            }

            return !string.IsNullOrEmpty(shapes[index].ShapeName) && shapes[index].Weight != null && shapes[index].Weight.length > 0;
        }

        private static string ShapeSummary(in BlendShapeTrack t)
        {
            var curve = t.Weight;
            if (curve == null || curve.length == 0)
            {
                return "カーブなし";
            }

            var max = float.MinValue;
            for (var i = 0; i < curve.length; i++)
            {
                max = Mathf.Max(max, curve[i].value);
            }

            return $"カーブ {curve.length} キー · {curve.Evaluate(0f):0.#} → {curve.Evaluate(1f):0.#}(最大 {max:0.#})";
        }

        private void RefreshShapeSummaries()
        {
            var shapes = Target?.BlendShapes;
            if (shapes == null)
            {
                return;
            }

            for (var i = 0; i < _shapeItems.Count && i < shapes.Length; i++)
            {
                _shapeItems[i].Title = ShapeTitle(in shapes[i]);
                _shapeItems[i].Search = ShapeSummary(in shapes[i]);
            }

            foreach (var kv in _shapeHeads)
            {
                if (kv.Key < 0 || kv.Key >= shapes.Length)
                {
                    continue;
                }

                SetLabel(kv.Value.Name, ShapeTitle(in shapes[kv.Key]));
                SetLabel(kv.Value.Summary, ShapeSummary(in shapes[kv.Key]));
                if (kv.Value.Summary != null)
                {
                    kv.Value.Summary.tooltip = kv.Value.Summary.text;
                }
            }

            ApplyShapeFlags();
            _shapeList?.UpdateCount();
        }

        // 行の ⚠: 名前が空・対象モデルに無い・外部パッケージが管理するシェイプ。
        private void ApplyShapeFlags()
        {
            var shapes = Target?.BlendShapes;
            foreach (var kv in _shapeFlags)
            {
                string msg = null;
                if (shapes != null && kv.Key >= 0 && kv.Key < shapes.Length)
                {
                    var name = shapes[kv.Key].ShapeName;
                    if (string.IsNullOrEmpty(name))
                    {
                        msg = "ShapeName が空です";
                    }
                    else if (ExternalBlendShapePrefixes.IsOwnedExternally(name))
                    {
                        msg = "外部パッケージが管理するシェイプです(D-Drive の AnimData から書くと衝突します)";
                    }
                    else if (_missingShapes.Contains(name))
                    {
                        msg = "対象モデルにこの BlendShape がありません";
                    }
                }

                kv.Value.Show(msg != null);
                kv.Value.tooltip = msg ?? string.Empty;
            }
        }

        private VisualElement MakeShapeRow(int index)
        {
            var shapes = Target?.BlendShapes;
            if (shapes == null || index < 0 || index >= shapes.Length)
            {
                return new VisualElement();
            }

            var t = shapes[index];
            var open = index == _openShape;
            var row = new VisualElement();
            row.AddToClassList("ux-fxrow");
            row.EnableInClassList("ux-fxrow--open", open);

            var head = new VisualElement();
            head.AddToClassList("ux-fxrow__head");
            head.AddToClassList("ux-trk__head");
            var flag = new Label("⚠");
            flag.AddToClassList("ux-lineflag");
            flag.Show(false);
            _shapeFlags[index] = flag;
            head.Add(flag);

            var idx = new Label("#" + (index + 1));
            idx.AddToClassList("ux-dim");
            idx.AddToClassList("ux-trk__idx");
            head.Add(idx);

            var refs = new ShapeHead();
            refs.Name = new Label(ShapeTitle(in t));
            refs.Name.AddToClassList("ux-fxrow__name");
            head.Add(refs.Name);

            refs.Summary = new Label(ShapeSummary(in t));
            refs.Summary.AddToClassList("ux-dim");
            refs.Summary.AddToClassList("ux-fxrow__summary");
            refs.Summary.tooltip = refs.Summary.text;
            head.Add(refs.Summary);
            _shapeHeads[index] = refs;

            var toggle = new Button(() => ToggleShapeOpen(index)) { text = open ? "▾" : "⚙", tooltip = "このシェイプの詳細を開く / 閉じる(ShapeName・Weight カーブ)" };
            toggle.AddToClassList("ux-btn");
            toggle.AddToClassList("ux-btn--icon");
            head.Add(toggle);

            var del = new Button(() => RemoveShape(index)) { text = "✕", tooltip = "このシェイプを削除する(Ctrl+Z で戻せる)" };
            del.AddToClassList("ux-btn");
            del.AddToClassList("ux-btn--icon");
            head.Add(del);
            row.Add(head);

            if (open)
            {
                row.Add(MakeShapeDetails(index));
            }

            return row;
        }

        private void ToggleShapeOpen(int index)
        {
            _openShape = _openShape == index ? -1 : index;
            ReRenderShapeList();
        }

        private void ReRenderShapeList()
        {
            _shapeHeads.Clear();
            _shapeFlags.Clear();
            _shapeList?.Render();
            ApplyShapeFlags();
        }

        private VisualElement MakeShapeDetails(int index)
        {
            var box = new VisualElement();
            box.AddToClassList("ux-fxrow__details");
            var props = new VisualElement();
            props.AddToClassList("ux-fxrow__props");
            var nameProp = So?.FindProperty($"BlendShapes.Array.data[{index}].ShapeName");
            if (nameProp != null)
            {
                var pf = new PropertyField(nameProp, "ShapeName");
                pf.AddToClassList("ux-fxrow__prop");
                pf.BindProperty(nameProp);
                props.Add(pf);
            }

            var names = ModelShapeNames();
            if (names.Count > 0)
            {
                var pick = new DropdownField("モデルから選ぶ", names, -1) { tooltip = "対象モデルが持つ BlendShape の名前から ShapeName を決める" };
                pick.AddToClassList("ux-fxrow__prop");
                pick.RegisterValueChangedCallback(evt => SetShapeName(index, evt.newValue));
                props.Add(pick);
            }

            box.Add(props);

            var curveProp = So?.FindProperty($"BlendShapes.Array.data[{index}].Weight");
            if (curveProp != null)
            {
                var curve = new CurveField("Weight") { tooltip = "正規化時間(0〜1)→ ウェイト(0〜100)" };
                curve.AddToClassList("ux-fxrow__prop");
                curve.BindProperty(curveProp);
                box.Add(curve);
            }

            return box;
        }

        // 対象モデルの BlendShape 名(外部管理の FC_ / fcs_ は除く。重複なし)。
        private List<string> ModelShapeNames()
        {
            var names = new List<string>();
            var animator = CurrentAnimator;
            if (animator == null)
            {
                return names;
            }

            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                {
                    continue;
                }

                for (var i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    var n = smr.sharedMesh.GetBlendShapeName(i);
                    if (!ExternalBlendShapePrefixes.IsOwnedExternally(n) && !names.Contains(n))
                    {
                        names.Add(n);
                    }
                }
            }

            return names;
        }

        private void ShowShapeMenu()
        {
            var names = ModelShapeNames();
            var menu = new GenericMenu();
            if (names.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("対象モデルに BlendShape がありません(先に対象の Animator を決めてください)"));
            }

            foreach (var n in names)
            {
                var name = n;
                menu.AddItem(new GUIContent(name), false, () => AddShape(name));
            }

            menu.ShowAsContext();
        }

        // ── 操作(すべて Undo 1 回) ──

        private void AddShape(string shapeName)
        {
            var d = Target;
            if (d == null)
            {
                return;
            }

            Undo.RecordObject(d, "Add Anim BlendShape");
            var list = new List<BlendShapeTrack>(d.BlendShapes ?? Array.Empty<BlendShapeTrack>())
            {
                new BlendShapeTrack { ShapeName = shapeName, Weight = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f)) },
            };
            d.BlendShapes = list.ToArray();
            EditorUtility.SetDirty(d);
            So?.Update();
            _openShape = d.BlendShapes.Length - 1;
            RefreshModelInfo(); // 照合(モデルに無い名前)を更新
            AfterDataWrite();
            ReRenderShapeList();
        }

        private void SetShapeName(int index, string shapeName)
        {
            var d = Target;
            if (d?.BlendShapes == null || index < 0 || index >= d.BlendShapes.Length)
            {
                return;
            }

            Undo.RecordObject(d, "Set Anim BlendShape Name");
            var t = d.BlendShapes[index];
            t.ShapeName = shapeName;
            d.BlendShapes[index] = t;
            EditorUtility.SetDirty(d);
            So?.Update();
            RefreshModelInfo();
            AfterDataWrite();
            ReRenderShapeList();
        }

        private void RemoveShape(int index)
        {
            var d = Target;
            if (d?.BlendShapes == null || index < 0 || index >= d.BlendShapes.Length)
            {
                return;
            }

            Undo.RecordObject(d, "Remove Anim BlendShape");
            var list = new List<BlendShapeTrack>(d.BlendShapes);
            list.RemoveAt(index);
            d.BlendShapes = list.ToArray();
            EditorUtility.SetDirty(d);
            So?.Update();
            _openShape = -1;
            RefreshModelInfo();
            AfterDataWrite();
            ReRenderShapeList();
        }
    }
}
