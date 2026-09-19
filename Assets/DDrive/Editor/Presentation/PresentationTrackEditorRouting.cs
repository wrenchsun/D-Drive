using DDrive.Foundation.Data;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Vfx;

namespace DDrive.Editor.Presentation
{
    // [08_presentation.md] 指摘3/4(2026-09-20) — 「専用エディタで開く(一緒に調整)」を押したとき、参照先の
    // Data 型からどの専用エディタの attach 付き Open(data, attachTarget) を呼ぶべきかを決める、ウィンドウを
    // 開かずに検証できる純粋関数(PresentationEditorWindow.TrackEditors.cs から使う)。attach の概念が無い
    // 種別(CameraShake/Haptic/Canvas/UiTween/Timeline 等)や Anim2D(3D 用 Animator の概念が無い)は
    // DefaultOpen(= 通常の DataEditorRegistry.Open(data)、「単体で確認用シーンに開き直す」と同じ経路)にする。
    public static class PresentationTrackEditorRouting
    {
        public enum EditorKind
        {
            None,               // asset が null(未設定 / 見つからない)
            VfxWithAttach,      // VfxEditorWindow.Open(VfxData, GameObject)
            AnchorGroupWithAttach, // AnchorGroupEditorWindow.Open(AnchorGroupData, GameObject)
            AnimWithAttach,     // AnimEditorWindow.Open(AnimData, GameObject)
            DefaultOpen,        // DataEditorRegistry 経由の通常の Open(data)
        }

        public static EditorKind Classify(AssetDataBase asset)
        {
            switch (asset)
            {
                case null:
                    return EditorKind.None;
                case VfxData _:
                    return EditorKind.VfxWithAttach;
                case AnchorGroupData _:
                    return EditorKind.AnchorGroupWithAttach;
                // Anim2DData : AnimData のため、AnimData より先に判定する([08] 実装メモ 5-4 の
                // PresentationTrackKindMapping と同じ注意)。Anim2D は 3D の Animator を対象にする概念が
                // 無いため、attach 付きの経路を持たず通常の Open にフォールバックする。
                case Anim2DData _:
                    return EditorKind.DefaultOpen;
                case AnimData _:
                    return EditorKind.AnimWithAttach;
                default:
                    return EditorKind.DefaultOpen;
            }
        }
    }
}
