using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4 追記(2026-10-06、埋め込みの有効 / 無効) — 埋め込み行の「無効で始める」(データに保存)と、
    // 作業用の「表示 / 非表示」(保存しない)の切り替え。
    public sealed partial class CanvasEditorWindow
    {
        // 埋め込み行(BuildEmbeddedRow)に足す 2 つの操作。各操作は作ったときの対象(owner)に対して行う。
        private void AddEmbedActiveControls(VisualElement row, CanvasData owner, int index)
        {
            var embed = owner.EmbeddedCanvases[index];

            var startInactive = new Toggle
            {
                text = "無効で始める",
                value = embed.StartInactive,
                tooltip = "親を開いたとき、この子を無効(非表示)で始める。Ui.SetEmbeddedActive か、ボタンの配線(ActivateEmbedded / ToggleEmbedded)で有効にすると、子の Appear → Idle が始まる。オフ = 従来どおり有効で始まる",
                style = { marginLeft = 4 },
            };
            startInactive.RegisterValueChangedCallback(evt =>
            {
                if (owner == null || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
                {
                    return;
                }

                Undo.RecordObject(owner, "Canvas: 埋め込みを無効で始める");
                var changed = owner.EmbeddedCanvases[index];
                changed.StartInactive = evt.newValue;
                owner.EmbeddedCanvases[index] = changed;
                EditorUtility.SetDirty(owner);
                if (owner == _target)
                {
                    RefreshValidation();
                    _statusLabel.text = evt.newValue
                        ? $"'{changed.RootPath}' は無効で始まります(確認用プレビューは開き直すと反映されます)"
                        : $"'{changed.RootPath}' は有効で始まります(確認用プレビューは開き直すと反映されます)";
                }
            });
            row.Add(startInactive);

            row.Add(new Button(() => ToggleEmbedWorkVisibility(owner, index))
            {
                text = "表示 / 非表示(作業用)",
                tooltip = "作業のために、この子の表示を一時的に切り替える(データにも Prefab にも保存しない)。確認用プレビューでは実際に有効 / 無効を切り替え(子の Appear / Disappear も再生される)、プレハブモードでは SceneView での表示だけを切り替える",
            });
        }

        // 作業用の表示切り替え(保存しない)。確認用プレビュー: 実 UiManager の SetEmbeddedActive(演出つき)。
        // プレハブモード: SceneVisibilityManager(Prefab を汚さない。ステージを閉じると戻る)。
        private void ToggleEmbedWorkVisibility(CanvasData owner, int index)
        {
            if (owner != _target || owner.EmbeddedCanvases == null || index >= owner.EmbeddedCanvases.Length)
            {
                return;
            }

            var rootPath = owner.EmbeddedCanvases[index].RootPath;
            if (string.IsNullOrEmpty(rootPath))
            {
                _statusLabel.text = "RootPath が空です";
                return;
            }

            var stage = GetTargetStage();
            var displayed = FindPlaybackTarget(rootPath, out _);
            if (displayed == null)
            {
                _statusLabel.text = $"'{rootPath}' の表示中の実体がありません(プレハブモードで開くか、「確認用シーンを開く」で表示してください)";
                return;
            }

            if (stage != null)
            {
                var visibility = SceneVisibilityManager.instance;
                var hidden = visibility.IsHidden(displayed.gameObject, false);
                if (hidden)
                {
                    visibility.Show(displayed.gameObject, true);
                }
                else
                {
                    visibility.Hide(displayed.gameObject, true);
                }

                _statusLabel.text = hidden
                    ? $"'{rootPath}' を SceneView に表示しました(作業用。保存されません)"
                    : $"'{rootPath}' を SceneView で非表示にしました(作業用。保存されません。もう一度押すと戻ります)";
                return;
            }

            if (_manager != null && _manager.IsOpen(_previewHandle))
            {
                var previewRoot = _manager.GetGameObject(_previewHandle);
                var path = previewRoot != null ? TransformPath.GetRelative(previewRoot.transform, displayed) : null;
                if (!string.IsNullOrEmpty(path))
                {
                    var active = _manager.IsEmbeddedActive(_previewHandle, path);
                    _manager.SetEmbeddedActive(_previewHandle, path, !active);
                    _statusLabel.text = active
                        ? $"'{rootPath}' を無効にしました(作業用。保存されません)"
                        : $"'{rootPath}' を有効にしました(作業用。保存されません)";
                    return;
                }
            }

            _statusLabel.text = $"'{rootPath}' を切り替えられませんでした(プレハブモードか確認用プレビューで表示してください)";
        }
    }
}
