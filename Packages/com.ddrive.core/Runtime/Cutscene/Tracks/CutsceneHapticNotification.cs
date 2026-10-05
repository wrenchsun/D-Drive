using System;
using DDrive.Foundation.Event;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // ── D-Drive Haptic マーカー: Haptics.Play(id) を委譲する ──
    [Serializable]
    public sealed class CutsceneHapticNotification : Marker
    {
        public DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Haptics.HapticMarker> HapticId;
    }
}
