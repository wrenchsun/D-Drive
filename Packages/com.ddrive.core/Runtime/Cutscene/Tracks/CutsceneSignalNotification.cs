using System;
using DDrive.Foundation.Event;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // ── D-Drive Signal マーカー: 文字列キーをコードへ通知する(CutsceneHandle.OnMarker)。
    //    Skip=ToMarker の目標(CutsceneData.SkipToMarkerKey)としても使う([26] §4.1/§4.3) ──
    [Serializable]
    public sealed class CutsceneSignalNotification : Marker
    {
        [Tooltip("cutscene/xxx 規約のキー(CutsceneHandle.OnMarker(key) で受け取る)。")]
        public string Key;
    }
}
