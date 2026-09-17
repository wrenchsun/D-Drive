using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DDrive.Editor.Preview
{
    // 「確認用シーンに配置 / 確認用シーンを開く」ボタンの共通 UI 部品(U-5、2026-09-17)。
    //   - 左クリック  : PreviewPlaceMode.CheckScene(従来どおり、確認用シーンを開いて配置)
    //   - 右クリック  : コンテキストメニュー「このシーンに配置」/「このシーンに本配置」
    // 各エディタは Action<PreviewPlaceMode> を 1 つ渡すだけでよい(メニューの文言・並びをここに一元化する)。
    public static class PreviewPlacementButton
    {
        public const string PlaceHereText = "このシーンに配置(一時・保存されない)";
        public const string PlaceHerePersistentText = "このシーンに本配置(シーンを移動しても消えない)";
        public const string ContextHint = "右クリック: このシーンに配置 / このシーンに本配置";

        public static Button Create(string text, string tooltip, Action<PreviewPlaceMode> place)
        {
            var button = new Button(() => place?.Invoke(PreviewPlaceMode.CheckScene))
            {
                text = text,
                tooltip = ComposeTooltip(tooltip),
            };
            ApplyNarrowWindowStyle(button);
            AttachContextMenu(button, place);
            return button;
        }

        public static ToolbarButton CreateToolbarButton(string text, string tooltip, Action<PreviewPlaceMode> place)
        {
            var button = new ToolbarButton(() => place?.Invoke(PreviewPlaceMode.CheckScene))
            {
                text = text,
                tooltip = ComposeTooltip(tooltip),
            };
            ApplyNarrowWindowStyle(button);
            AttachContextMenu(button, place);
            return button;
        }

        // [09_editor_tools.md] §7.1(横方向の拡縮規約、2026-09-17) — 拡大率 100% / 幅 500px でも切れないこと。
        //  - 固定幅を持たせない。狭いときは縮む(flexShrink=1、minWidth=0 で内容幅より小さくなれるようにする)
        //  - 縮みきったらラベルを折り返す(whiteSpace=Normal。既定の Nowrap だと末尾が切れて読めなくなる)
        // 説明文はラベルに足さず tooltip 側へ逃がす(ComposeTooltip)。
        public static void ApplyNarrowWindowStyle(VisualElement button)
        {
            if (button == null)
            {
                return;
            }

            button.style.flexShrink = 1f;
            button.style.minWidth = 0f;
            button.style.whiteSpace = WhiteSpace.Normal;
        }

        // 「配置」ボタンを並べる行に付ける。狭いときに 1 行へ詰め込まず 2 段へ折り返す([09] §7.1)。
        public static VisualElement ConfigureRow(VisualElement row)
        {
            if (row != null)
            {
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
            }

            return row;
        }

        // 既存のボタン(独自の見た目・並びを持つもの)に、右クリックメニューだけを後付けする。
        public static void AttachContextMenu(VisualElement element, Action<PreviewPlaceMode> place)
        {
            if (element == null || place == null)
            {
                return;
            }

            element.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(PlaceHereText, _ => place(PreviewPlaceMode.CurrentScene));
                evt.menu.AppendAction(PlaceHerePersistentText, _ => place(PreviewPlaceMode.CurrentScenePersistent));
            }));
        }

        public static string ComposeTooltip(string tooltip)
            => string.IsNullOrEmpty(tooltip) ? ContextHint : $"{tooltip}\n{ContextHint}";
    }
}
