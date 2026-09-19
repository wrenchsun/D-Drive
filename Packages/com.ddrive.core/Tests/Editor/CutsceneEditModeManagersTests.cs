using DDrive.Editor.Cutscene;
using NUnit.Framework;

namespace DDrive.Tests.Editor
{
    // [26_timeline.md] §4.4(Edit Mode プレビュー、2026-09-19) — `CutsceneEditModeManagers` の組み立て・
    // 破棄がウィンドウを起動せずに確認できる範囲。実際の SE/VFX/UI/AnchorGroup 再生の目視確認は
    // docs/43_manual_verification_2026-09-17.md に委ねる(実 Manager が実際に音・エフェクトを出すかは
    // 自動テストの対象外、既存の Scene*PreviewDriver 群と同じ扱い)。
    public class CutsceneEditModeManagersTests
    {
        [Test]
        public void Construct_BuildsAllManagers_WithoutThrowing()
        {
            CutsceneEditModeManagers managers = null;
            Assert.DoesNotThrow(() => managers = new CutsceneEditModeManagers());

            Assert.IsNotNull(managers.Registry);
            Assert.IsNotNull(managers.Audio);
            Assert.IsNotNull(managers.Vfx);
            Assert.IsNotNull(managers.Ui);
            Assert.IsNotNull(managers.Groups);
            Assert.IsNotNull(managers.Models);
            Assert.IsNotNull(managers.Dispatcher);
            Assert.IsNotNull(managers.ShakeDriver);
            Assert.IsNotNull(managers.HapticsDriver);

            managers.Dispose();
        }

        [Test]
        public void Tick_DoesNotThrow_WhenNothingIsPlaying()
        {
            var managers = new CutsceneEditModeManagers();
            Assert.DoesNotThrow(() => managers.Tick(0.016f));
            managers.Dispose();
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var managers = new CutsceneEditModeManagers();
            managers.Dispose();
            Assert.DoesNotThrow(() => managers.Dispose());
        }
    }
}
