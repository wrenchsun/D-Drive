using DDrive.Editor.Cutscene;
using DDrive.Runtime.Cutscene;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — `PrepareContext` が
    // `CutsceneDirectorContext` を付け、Editor 用 Manager 群(ManagerRefs)を割り当てることを
    // Timeline ウィンドウを起動せずに確認する。
    public class CutsceneEditModePreviewProviderTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void PrepareContext_AddsContext_WithManagerRefs_AndFireDisabled()
        {
            _go = new GameObject("CutsceneEditModePreviewProviderTests");

            CutsceneDirectorContext context = null;
            Assert.DoesNotThrow(() => context = CutsceneEditModePreviewProvider.PrepareContext(_go));

            Assert.IsNotNull(context);
            Assert.AreSame(context, _go.GetComponent<CutsceneDirectorContext>());
            Assert.IsFalse(context.FireEnabled, "まだ再生していないので false(ドラッグ中と同じ無音状態)");
            Assert.IsNotNull(context.ManagerRefs);
            Assert.IsNotNull(context.ManagerRefs.Audio);
            Assert.IsNotNull(context.ManagerRefs.Vfx);
            Assert.IsNotNull(context.ManagerRefs.Ui);
            Assert.IsNotNull(context.ManagerRefs.Groups);
        }

        [Test]
        public void PrepareContext_ReusesExistingComponent_WhenCalledTwice()
        {
            _go = new GameObject("CutsceneEditModePreviewProviderTests2");

            var first = CutsceneEditModePreviewProvider.PrepareContext(_go);
            var second = CutsceneEditModePreviewProvider.PrepareContext(_go);

            Assert.AreSame(first, second);
        }
    }
}
