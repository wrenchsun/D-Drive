using UnityEngine.UIElements;

namespace DDrive.Editor.Common
{
    // [09_editor_tools.md] §7.1(2026-09-17) — 横幅 500px でも見切れないようにするための小物。
    //
    // UI Toolkit の `BaseField<T>`(Toggle / Slider / TextField …)のラベル部には USS 既定で
    // `min-width: 120px` / `flex-basis: 120px` が付く。Inspector のように縦に積む分には列が揃って
    // 都合が良いが、1 行に複数のフィールドを並べる行では「ループ」のような 2 文字のラベルでも
    // 120px を占め、チェックボックスやつまみを右へ押し出して見切れさせる原因になる
    //   (U-10 「Button Skin Editor の SE も鳴らすが見切れている」/ U-12 「Asset Browser 下部の
    //    プレビューバーの UI が崩れている」はどちらもこれが主因)。
    //
    // 横並びの行に入れるフィールドにだけ使うこと(縦に積むフィールドは既定のままにして列を揃える)。
    public static class CompactFieldLayout
    {
        // 使い方: CompactFieldLayout.ShrinkLabel(myToggle.labelElement);
        public static void ShrinkLabel(Label label, float marginRight = 4f)
        {
            if (label == null)
            {
                return; // 念のため(CLAUDE.md §0-4: 例外で止めない)
            }

            label.style.minWidth = StyleKeyword.Auto;
            label.style.width = StyleKeyword.Auto;
            label.style.flexBasis = StyleKeyword.Auto;
            label.style.marginRight = marginRight;
        }
    }
}
