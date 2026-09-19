using DDrive.Foundation.Identity;
using DDrive.Runtime.Loop;
using DDrive.Runtime.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DDrive.Samples
{
    // [11_tasks.md] 5-1 — 「剣攻撃デモが 1 API で再生」(AC)の手動確認用。
    // Assets/GameData/PreviewScenes/PresentationSkillSlashPreviewScene.unity に置く。
    // 確認できたら削除して構わない(NetBridgeSmokeTest.cs と同じ扱い)。
    //
    // このサンプルは DDrive.Samples.asmdef(DDrive.Runtime のみ参照)からは
    // Assets/Generated/AssetIds.g.cs(asmdef 無し = Assembly-CSharp 所属)の
    // PRESENTID.DemoSkillSlash を直接参照できないため、生成された ID と同じ値を
    // 生の AssetId として構築している。実際のゲームコード(Assembly-CSharp 側)では
    //     Presentation.Play(DDrive.Generated.PRESENTID.DemoSkillSlash, ctx)
    // とだけ書けばよい(このデモが示したいのはまさにこの 1 行)。
    public sealed class PresentationSkillSlashDemo : MonoBehaviour
    {
        // PRES_Demo_SkillSlash.asset の Id(Assets/Generated/AssetIds.g.cs の
        // PRESENTID.DemoSkillSlash と同じ値)。エディタで作成し直した場合はここも更新すること。
        private static readonly AssetId<PresentationMarker> SkillSlashId =
            new(0xCD2986D134D20E66UL, AssetType.Presentation);

        [Tooltip("ctx.Self。未設定ならこの GameObject の Transform を使う。")]
        [SerializeField] private Transform self;

        [Tooltip("Play/Space で有効時、起動後すぐに再生を始める。")]
        [SerializeField] private bool playOnStart = true;

        private PresentationHandle _handle;

        private async void Start()
        {
            if (self == null)
            {
                self = transform;
            }

            var bootstrap = DDriveRuntimeBootstrap.Instance;
            if (bootstrap != null)
            {
                await bootstrap.WhenReady;
            }

            if (playOnStart)
            {
                Play();
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.pKey.wasPressedThisFrame)
            {
                Play();
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                TriggerHit();
            }

            if (keyboard.cKey.wasPressedThisFrame)
            {
                CancelPresentation();
            }
        }

        [ContextMenu("Play")]
        public void Play()
        {
            // ── 剣攻撃デモが 1 API で再生される、まさにこの行(AC) ──
            _handle = Presentation.Play(SkillSlashId, new PlayContext { Self = self, Position = self.position });
            Debug.Log($"[PresentationSkillSlashDemo] Play() -> IsPlaying={_handle.IsPlaying}");
        }

        [ContextMenu("Signal(hit)")]
        public void TriggerHit()
        {
            _handle.Signal("hit");
            Debug.Log("[PresentationSkillSlashDemo] Signal(\"hit\") -> HitStop + SE");
        }

        [ContextMenu("Cancel")]
        public void CancelPresentation()
        {
            _handle.Cancel();
            Debug.Log($"[PresentationSkillSlashDemo] Cancel() -> IsPlaying={_handle.IsPlaying}");
        }
    }
}
