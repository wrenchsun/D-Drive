using System;
using DDrive.Foundation.Event;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // ── D-Drive Shake マーカー: CameraFx.Shake(id) を委譲する([26] §4.3 の「Shake/Haptic マーカー」) ──
    [Serializable]
    public sealed class CutsceneShakeNotification : Marker
    {
        public DDrive.Foundation.Identity.AssetId<DDrive.Runtime.CameraShake.ShakeMarker> ShakeId;
    }
}
