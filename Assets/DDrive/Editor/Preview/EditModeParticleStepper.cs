using UnityEngine;

namespace DDrive.Editor.Preview
{
    // EditMode では ParticleSystem が自動シミュレーションされない(再生時間が進むのは PlayMode のみ)。
    // 試聴中の VFX を手動で進めるための共通処理。PreviewService(プレビューシーン方式)と
    // SceneVfxPreviewDriver(開いているシーン方式)が同じ式を共有する(重複実装の解消、2026-09-08)。
    public static class EditModeParticleStepper
    {
        // systems: Spawn 時にキャッシュした ParticleSystem 配列(子も個別に含む)。
        public static void Step(ParticleSystem[] systems, float dt)
        {
            if (systems == null || dt <= 0f)
            {
                return;
            }

            foreach (var ps in systems)
            {
                if (ps != null)
                {
                    // withChildren=false: 配列に子も個別に入っているため二重適用を避ける。
                    // restart=false: 現在時刻から dt 分だけ進める。fixedTimeStep=false: 任意 dt で滑らかに。
                    ps.Simulate(dt, false, false, false);
                }
            }
        }
    }
}
