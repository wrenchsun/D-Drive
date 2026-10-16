using System;
using System.Collections.Generic;
using DDrive.Editor.Manual;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // カード(試験版 D の PdCard を移植): 角丸の枠 + 見出し(14px 太字)+ 一言説明 + 右上「？」。表示できる欄が無いときは既定のままで大丈夫の案内。
    public sealed class UxCard : VisualElement
    {
        private readonly VisualElement _body;
        private readonly Label _empty;
        private readonly Label _desc;

        public List<UxRow> Rows { get; } = new List<UxRow>();
        public VisualElement Body => _body;

        public UxCard(string title, string desc, Action help)
        {
            AddToClassList("ux-card");

            var head = new VisualElement();
            head.AddToClassList("ux-card__head");
            var titles = new VisualElement();
            titles.AddToClassList("ux-card__titles");
            var t = new Label(title);
            t.AddToClassList("ux-card__title");
            titles.Add(t);
            _desc = new Label(desc);
            _desc.AddToClassList("ux-dim");
            _desc.AddToClassList("ux-card__desc");
            titles.Add(_desc);
            head.Add(titles);
            var q = new Button(help) { text = "?", tooltip = "マニュアルを開く" };
            q.AddToClassList("ux-btn");
            q.AddToClassList("ux-btn--icon");
            head.Add(q);
            Add(head);

            _body = new VisualElement();
            _body.AddToClassList("ux-card__body");
            Add(_body);

            _empty = new Label("この設定は既定のままで大丈夫です");
            _empty.AddToClassList("ux-card__empty");
            _empty.Show(false);
            Add(_empty);
        }

        // 「?」のクリック先: マニュアルの page(必要なら #anchor 付き)を開く。
        public static Action Help(string page, string anchor = null) => () =>
        {
            if (string.IsNullOrEmpty(anchor))
            {
                ManualLauncher.OpenPage(page);
            }
            else
            {
                ManualLauncher.OpenPage(page, anchor);
            }
        };

        public void AddRow(UxRow row)
        {
            Rows.Add(row);
            _body.Add(row);
        }

        // ── 未調整の設定の折りたたみ ──

        private VisualElement _foldBox;
        private VisualElement _foldBody;
        private Button _foldHead;
        private bool _foldOpen;
        private int _foldShown;

        private void EnsureFold()
        {
            if (_foldBox != null)
            {
                return;
            }

            _foldBox = new VisualElement();
            _foldBox.AddToClassList("ux-fold");
            _foldHead = new Button(() =>
            {
                _foldOpen = !_foldOpen;
                UpdateFold();
            });
            _foldHead.AddToClassList("ux-fold__head");
            _foldBox.Add(_foldHead);
            _foldBody = new VisualElement();
            _foldBody.AddToClassList("ux-fold__body");
            _foldBox.Add(_foldBody);
            Insert(IndexOf(_body) + 1, _foldBox);
            _foldBox.Show(false);
        }

        // row を折りたたみ側(folded)か通常側へ移す。
        public void SetFolded(UxRow row, bool folded)
        {
            if (folded)
            {
                EnsureFold();
                if (row.parent != _foldBody)
                {
                    _foldBody.Add(row);
                }
            }
            else if (_foldBody != null && row.parent == _foldBody)
            {
                _body.Add(row);
                // 元の並びに戻す。
                for (var i = 0; i < Rows.Count; i++)
                {
                    if (Rows[i].parent == _body)
                    {
                        Rows[i].BringToFront();
                    }
                }

                for (var i = Rows.Count - 1; i >= 0; i--)
                {
                    if (Rows[i].parent == _body)
                    {
                        Rows[i].SendToBack();
                    }
                }
            }
        }

        public bool IsFolded(UxRow row) => _foldBody != null && row.parent == _foldBody;

        // 折りたたみ内で表示対象になっている欄の数(重複欄を除く)。0 なら見出しごと隠す。
        public void SetFoldShown(int shown)
        {
            _foldShown = shown;
            UpdateFold();
        }

        private void UpdateFold()
        {
            if (_foldBox == null)
            {
                return;
            }

            _foldBox.Show(_foldShown > 0);
            _foldBody.Show(_foldOpen);
            _foldHead.text = (_foldOpen ? "▾ " : "▸ ") + "未調整の設定（" + _foldShown + "）";
            _foldHead.tooltip = "既定値のままの設定です。クリックで開きます";
        }

        // visibleRows: 表示する欄の数。filtering = 検索 / 変更済みだけで絞っている。
        public void SetVisibility(int visibleRows, bool filtering)
        {
            if (visibleRows > 0)
            {
                this.Show(true);
                _body.Show(true);
                _empty.Show(false);
                RemoveFromClassList("ux-card--empty");
                return;
            }

            if (filtering)
            {
                this.Show(false);
                return;
            }

            this.Show(true);
            _body.Show(false);
            _empty.Show(true);
            AddToClassList("ux-card--empty");
        }
    }
}
