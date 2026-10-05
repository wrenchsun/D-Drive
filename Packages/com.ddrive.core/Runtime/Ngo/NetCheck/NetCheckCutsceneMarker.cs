// [14_networking.md] §22 / [29_network_device_test.md] §27(N-8、2026-10-06) — Cutscene のマーカーの NetCheck 用の
// 外部マーカー(`ICutsceneMarker`、FC-4)。発火を `Fired` で NetCheckRunner に通知し、Runner が
// `[NetCheck] cutscene_marker key=... markerTime=... elapsed=... handle=...` を 1 行出す。
// MonoScript が取れるよう、クラス名と同じ名前のファイルに 1 クラス 1 ファイルで置く(Player ビルドで読める形)。
#if DDRIVE_NGO
using System;
using DDrive.Foundation.Handle;
using DDrive.Runtime.Cutscene;
using UnityEngine;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Net
{
    [Serializable]
    public sealed class NetCheckCutsceneMarker : Marker, ICutsceneMarker
    {
        [Tooltip("マーカーを区別するキー(m0 / m1 / m4 / m6 / m15)。")]
        public string Key;

        // (key, markerTime, elapsed, handle)。確認用サンプル専用の静的イベント(購読は NetCheckRunner だけ)。
        public static Action<string, double, double, Handle<CutsceneMarker>> Fired;

        public void Fire(in CutsceneMarkerContext context)
        {
            Fired?.Invoke(Key, context.MarkerTime, context.Elapsed, context.Handle);
        }
    }
}
#endif
