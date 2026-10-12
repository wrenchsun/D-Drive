using UnityEngine.UIElements;

namespace DDrive.Editor.Ux
{
    // 表示 / 非表示は style.display ではなく USS クラス(.ux-hidden)で切り替える(Ux 配下はインラインスタイル禁止。docs/1010 §2)。
    internal static class UxShow
    {
        public const string HiddenClass = "ux-hidden";

        public static void Show(this VisualElement element, bool shown) => element?.EnableInClassList(HiddenClass, !shown);

        public static bool IsShown(this VisualElement element) => element != null && !element.ClassListContains(HiddenClass);
    }

    // 部品が持つ「外部変更(Inspector・Undo・別の欄)を取り込んで描き直す」口。UxVisualRow が束ねる。
    internal interface IUxSyncable
    {
        void Sync();
    }
}
