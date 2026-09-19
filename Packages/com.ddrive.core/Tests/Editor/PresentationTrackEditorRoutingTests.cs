using DDrive.Editor.Presentation;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Anim2D;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [08_presentation.md] 指摘3/4(2026-09-20) — 「専用エディタで開く(一緒に調整)」がどの Open(data, attach)
    // overload を呼ぶべきかの判定(PresentationTrackEditorRouting.Classify)を、ウィンドウを開かずに検証する。
    public class PresentationTrackEditorRoutingTests
    {
        [Test]
        public void Classify_Null_ReturnsNone()
        {
            Assert.AreEqual(PresentationTrackEditorRouting.EditorKind.None, PresentationTrackEditorRouting.Classify(null));
        }

        [Test]
        public void Classify_VfxData_ReturnsVfxWithAttach()
        {
            var vfx = ScriptableObject.CreateInstance<VfxData>();
            try
            {
                Assert.AreEqual(PresentationTrackEditorRouting.EditorKind.VfxWithAttach, PresentationTrackEditorRouting.Classify(vfx));
            }
            finally
            {
                Object.DestroyImmediate(vfx);
            }
        }

        [Test]
        public void Classify_AnchorGroupData_ReturnsAnchorGroupWithAttach()
        {
            var group = ScriptableObject.CreateInstance<AnchorGroupData>();
            try
            {
                Assert.AreEqual(PresentationTrackEditorRouting.EditorKind.AnchorGroupWithAttach, PresentationTrackEditorRouting.Classify(group));
            }
            finally
            {
                Object.DestroyImmediate(group);
            }
        }

        [Test]
        public void Classify_AnimData_ReturnsAnimWithAttach()
        {
            var anim = ScriptableObject.CreateInstance<AnimData>();
            try
            {
                Assert.AreEqual(PresentationTrackEditorRouting.EditorKind.AnimWithAttach, PresentationTrackEditorRouting.Classify(anim));
            }
            finally
            {
                Object.DestroyImmediate(anim);
            }
        }

        // Anim2DData : AnimData のため、AnimData より先に判定する必要がある(PresentationTrackKindMapping と
        // 同じ注意点)。3D の Animator を対象にする attach 概念が無いため DefaultOpen にフォールバックする。
        [Test]
        public void Classify_Anim2DData_ReturnsDefaultOpen_NotAnimWithAttach()
        {
            var anim2d = ScriptableObject.CreateInstance<Anim2DData>();
            try
            {
                Assert.AreEqual(PresentationTrackEditorRouting.EditorKind.DefaultOpen, PresentationTrackEditorRouting.Classify(anim2d));
            }
            finally
            {
                Object.DestroyImmediate(anim2d);
            }
        }

        [Test]
        public void Classify_OtherAssetType_ReturnsDefaultOpen()
        {
            var presentation = ScriptableObject.CreateInstance<PresentationData>();
            try
            {
                Assert.AreEqual(PresentationTrackEditorRouting.EditorKind.DefaultOpen, PresentationTrackEditorRouting.Classify(presentation));
            }
            finally
            {
                Object.DestroyImmediate(presentation);
            }
        }
    }
}
