using System;
using System.Collections.Generic;
using DDrive.Editor.Ux;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // 「カーソル移動(Navigation)」の欄(docs/1010 UX-4 / D-10)。見出しに「N 要素・リンク M 本・FirstSelected」の要約。
    // 中身(Foldout、既定は閉じる): Selectable の自動収集・ノードグラフ(NavigationGraphView。既存のビューをそのまま埋め込む)・到達不能 / 未配線の確認・
    // パッド操作シミュレーション。ノードグラフは静的な編集用ダイアグラムで、実 UI を描画するものではない(ADR-4 の範囲内)。
    internal sealed partial class CanvasEditor
    {
        private CanvasSectionView _navSection;
        private NavigationGraphView _graphView;
        private VisualElement _unreachableContainer;
        private VisualElement _padRow;
        private Label _focusLabel;

        private UxRow CreateNavRows(FieldGuideEntry e, UxCard card)
        {
            _navSection = new CanvasSectionView("nav", _foldOpen, NavTitle, NavSignature, _ => RebuildGraph());
            var s = _navSection.Static;

            var collect = new VisualElement();
            collect.AddToClassList("ux-toolsline");
            collect.Add(MakeButton("Selectable を自動収集", null, () => CollectSelectables(), "Prefab 内の Selectable から Navigation を作る(既存の行は保持する)"));
            collect.Add(MakeButton("自動レイアウトを更新", null, () =>
            {
                _graphView?.ClearManualLayout();
                RebuildGraph();
            }, "ドラッグで動かしたノード位置・追加した Reroute point を破棄し、Prefab のレイアウトどおりに戻す"));
            collect.Add(MakeButton("到達不能を検出", null, DetectUnreachable, "FirstSelected から十字キーでたどれない要素を一覧する"));
            collect.Add(MakeButton("未配線を一覧", null, ReportUnlinked, "Navigation に登録されているが 4 方向とも空(Unity の自動ナビゲーションのまま)の要素を一覧する(データは書き換えない)"));
            s.Add(collect);

            var help = new Label("パン: 中ドラッグ / Alt+左ドラッグ　ズーム: ホイール　リンク作成: ポートからドラッグ　配線を切る: Ctrl+左ドラッグでなぞる　Reroute point 追加: 配線をダブルクリック(移動はドラッグ、削除は右クリック)");
            help.AddToClassList("ux-dim");
            help.AddToClassList("ux-note");
            s.Add(help);

            var heightSlider = new SliderInt("表示高さ", 320, 900) { value = 320 };
            heightSlider.AddToClassList("ux-navheight");
            _graphView = new NavigationGraphView();
            _graphView.AddToClassList("ux-navgraph");
            heightSlider.RegisterValueChangedCallback(evt =>
            {
                if (_graphView != null)
                {
                    _graphView.style.height = evt.newValue; // ユーザーが決める可変の高さ(USS では表せない)
                }
            });
            s.Add(heightSlider);
            s.Add(_graphView);

            _graphView.OnSetLink = (from, dir, to) => ApplyNavEdit(() => NavigationGraph.SetLink(Target, from, dir, to), "リンクを設定");
            _graphView.OnClearLink = (from, dir) => ApplyNavEdit(() => NavigationGraph.ClearLink(Target, from, dir), "リンクを削除");
            _graphView.OnClearAllLinks = from => ApplyNavEdit(() => NavigationGraph.ClearAllLinks(Target, from), "リンクを全て削除");
            _graphView.OnSetFirstSelected = path => ApplyNavEdit(() => Target.FirstSelected = path, "FirstSelected を設定");
            _graphView.OnCutLinks = edges => ApplyNavEdit(() =>
            {
                foreach (var edge in edges)
                {
                    NavigationGraph.ClearLink(Target, edge.From, edge.Direction);
                }
            }, "配線を切る");
            // ドラッグで動かしたノード位置・Reroute point はユーザー要望により例外的に CanvasData へ保存する(ランタイムの動作には使わない
            // エディタ表示専用データ。docs/07 2026-09-12 追記)。グラフ形状は変わらないので再構築は伴わず、Undo + SetDirty だけ行う。
            _graphView.OnLayoutChanged = () =>
            {
                if (Target == null)
                {
                    return;
                }

                Undo.RecordObject(Target, "グラフレイアウトを変更");
                Target.NavigationNodeLayout = _graphView.ExportNodeLayout();
                Target.NavigationEdgeWaypoints = _graphView.ExportEdgeWaypoints();
                EditorUtility.SetDirty(Target);
            };

            _unreachableContainer = new VisualElement();
            s.Add(_unreachableContainer);

            var padTitle = new Label("パッド操作シミュレーション");
            padTitle.AddToClassList("ux-fxphase__caption");
            s.Add(padTitle);
            _padRow = new VisualElement();
            _padRow.AddToClassList("ux-toolsline");
            _padRow.Add(MakeButton("▲", "ux-btn--icon", () => SimulateMove(Vector2.up), "上へ"));
            _padRow.Add(MakeButton("▼", "ux-btn--icon", () => SimulateMove(Vector2.down), "下へ"));
            _padRow.Add(MakeButton("◀", "ux-btn--icon", () => SimulateMove(Vector2.left), "左へ"));
            _padRow.Add(MakeButton("▶", "ux-btn--icon", () => SimulateMove(Vector2.right), "右へ"));
            _padRow.Add(MakeButton("決定", "ux-btn--small", SimulateSubmit, "いまのフォーカスを押す"));
            s.Add(_padRow);
            _focusLabel = new Label("フォーカス: (未確認)");
            _focusLabel.AddToClassList("ux-dim");
            s.Add(_focusLabel);

            _navSection.Rebuild();
            UpdatePadEnabled();
            AddRow(card, SectionRow(e, _navSection, q => ContainsIgnoreCase(Target?.FirstSelected, q) || NavHasPath(q)), false);
            return RawListRow(e, "カーソルの移動先(一覧そのもの)");
        }

        private bool NavHasPath(string q)
        {
            var nav = Target?.Navigation;
            for (var i = 0; nav != null && i < nav.Length; i++)
            {
                if (ContainsIgnoreCase(nav[i].Element, q))
                {
                    return true;
                }
            }

            return false;
        }

        private string NavTitle()
        {
            var nav = Target?.Navigation;
            var nodes = nav?.Length ?? 0;
            var links = 0;
            for (var i = 0; nav != null && i < nav.Length; i++)
            {
                links += (string.IsNullOrEmpty(nav[i].Up) ? 0 : 1) + (string.IsNullOrEmpty(nav[i].Down) ? 0 : 1)
                         + (string.IsNullOrEmpty(nav[i].Left) ? 0 : 1) + (string.IsNullOrEmpty(nav[i].Right) ? 0 : 1);
            }

            var first = string.IsNullOrEmpty(Target?.FirstSelected) ? "未設定" : Target.FirstSelected;
            return $"{nodes} 要素・リンク {links} 本・最初 = {first}";
        }

        private int NavSignature()
        {
            var t = Target;
            if (t == null)
            {
                return 0;
            }

            var h = CanvasSig.Add(17, t.Prefab != null ? t.Prefab.GetInstanceID() : 0);
            h = CanvasSig.Add(h, t.FirstSelected);
            var nav = t.Navigation;
            for (var i = 0; nav != null && i < nav.Length; i++)
            {
                h = CanvasSig.Add(h, nav[i].Element);
                h = CanvasSig.Add(h, nav[i].Up);
                h = CanvasSig.Add(h, nav[i].Down);
                h = CanvasSig.Add(h, nav[i].Left);
                h = CanvasSig.Add(h, nav[i].Right);
            }

            return h;
        }

        // Undo.RecordObject + SetDirty で包んでからグラフ / 検証を再構築する。
        private void ApplyNavEdit(Action mutate, string undoLabel)
        {
            if (Target == null)
            {
                return;
            }

            Undo.RecordObject(Target, undoLabel);
            mutate();
            EditorUtility.SetDirty(Target);
            AfterDataWrite();
        }

        private void RebuildGraph()
        {
            if (_graphView == null)
            {
                return;
            }

            // 保存済みのノード位置 / Reroute point を読み込む(Undo / Redo で配列側が変わった場合もここで追従する)。
            _graphView.LoadLayout(Target?.NavigationNodeLayout, Target?.NavigationEdgeWaypoints);

            if (Target == null || Target.Prefab == null)
            {
                _graphView.SetGraph(new NavigationGraph(), string.Empty, null);
                _unreachableContainer?.Clear();
                UpdatePadEnabled();
                return;
            }

            var graph = NavigationGraph.Build(Target, Target.Prefab);
            _graphView.SetGraph(graph, Target.FirstSelected, Target.Prefab);
            _graphView.SetFocusedPath(_simFocusPath);
            UpdateFocusLabel();
            UpdatePadEnabled();
        }

        private void DetectUnreachable()
        {
            if (_unreachableContainer == null)
            {
                return;
            }

            _unreachableContainer.Clear();
            if (Target == null || Target.Prefab == null)
            {
                return;
            }

            var unreachable = NavigationGraph.Build(Target, Target.Prefab).Unreachable(Target.FirstSelected);
            if (unreachable.Count == 0)
            {
                var none = new Label("到達不能な要素はありません。");
                none.AddToClassList("ux-dim");
                _unreachableContainer.Add(none);
                return;
            }

            _unreachableContainer.Add(new HelpBox($"到達不能な要素が {unreachable.Count} 件あります(赤枠のノード):", HelpBoxMessageType.Warning));
            foreach (var path in unreachable)
            {
                _unreachableContainer.Add(new Label("・" + (string.IsNullOrEmpty(path) ? RootElementLabel : path)));
            }
        }

        // 「未配線を一覧」: Navigation に登録されているが 4 方向とも空(Unity の自動ナビゲーションのまま)の要素を一覧するだけで、データは書き換えない。
        private void ReportUnlinked()
        {
            if (_unreachableContainer == null || Target == null)
            {
                return;
            }

            _unreachableContainer.Clear();
            var unlinked = new List<string>();
            if (Target.Navigation != null)
            {
                foreach (var node in Target.Navigation)
                {
                    if (string.IsNullOrEmpty(node.Up) && string.IsNullOrEmpty(node.Down) && string.IsNullOrEmpty(node.Left) && string.IsNullOrEmpty(node.Right))
                    {
                        unlinked.Add(node.Element);
                    }
                }
            }

            if (unlinked.Count == 0)
            {
                var none = new Label("未配線(Automatic のまま)の要素はありません。");
                none.AddToClassList("ux-dim");
                _unreachableContainer.Add(none);
                return;
            }

            _unreachableContainer.Add(new HelpBox($"未配線(Automatic のまま)の要素が {unlinked.Count} 件あります。Unity の自動ナビゲーションのまま動作します:", HelpBoxMessageType.Info));
            foreach (var path in unlinked)
            {
                _unreachableContainer.Add(new Label("・" + (string.IsNullOrEmpty(path) ? RootElementLabel : path)));
            }
        }

        private void UpdatePadEnabled()
        {
            var enabled = Target != null && _manager != null && _manager.IsOpen(_previewHandle) && _previewData == Target;
            _padRow?.SetEnabled(enabled);
        }

        private void SimulateMove(Vector2 dir)
        {
            if (Target == null || _manager == null || !_manager.IsOpen(_previewHandle))
            {
                return;
            }

            if (string.IsNullOrEmpty(_simFocusPath))
            {
                _simFocusPath = Target.FirstSelected;
            }

            if (UnityEngine.EventSystems.EventSystem.current != null && _manager.MoveFocus(dir))
            {
                var go = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (go != null)
                {
                    _simFocusPath = ComputeRelativePath(go.transform);
                }
            }
            else
            {
                _manager.MoveFocusFrom(_previewHandle, _simFocusPath, dir, out var next);
                _simFocusPath = next;
            }

            _graphView?.SetFocusedPath(_simFocusPath);
            UpdateFocusLabel();
        }

        private void SimulateSubmit()
        {
            if (Target == null || _manager == null || !_manager.IsOpen(_previewHandle) || string.IsNullOrEmpty(_simFocusPath))
            {
                return;
            }

            var button = _manager.GetComponent<UiButton>(_previewHandle, _simFocusPath);
            if (button != null)
            {
                button.SimulateClick();
                return;
            }

            var uguiButton = _manager.GetComponent<UnityEngine.UI.Selectable>(_previewHandle, _simFocusPath);
            (uguiButton as UnityEngine.EventSystems.ISubmitHandler)?.OnSubmit(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
        }

        private string ComputeRelativePath(Transform target)
        {
            var rootGo = _manager.GetGameObject(_previewHandle);
            if (rootGo == null)
            {
                return _simFocusPath;
            }

            return TransformPath.GetRelative(rootGo.transform, target);
        }

        private void UpdateFocusLabel()
        {
            if (_focusLabel == null)
            {
                return;
            }

            _focusLabel.text = string.IsNullOrEmpty(_simFocusPath) ? "フォーカス: (未確認)" : $"フォーカス: {_simFocusPath}";
        }
    }
}
