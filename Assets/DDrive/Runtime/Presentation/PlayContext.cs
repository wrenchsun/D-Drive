using System;
using UnityEngine;

namespace DDrive.Runtime.Presentation
{
    // [08_presentation.md] §2 — 再生文脈。プログラマーが Presentation.Play(id, ctx) に渡す。
    public struct PlayContext
    {
        // 再生主体(プレイヤー等)。Target=Self のトラックはこの配下から Anchor を解決する。
        public Transform Self;

        // 対象(敵等)。null 可。Target=ContextTarget のトラックはこの配下から Anchor を解決する。
        public Transform Target;

        // 発生位置(将来の CameraShake.FromSource 等が使う想定、[16_camera_haptics.md] A-1)。
        // 5-1 時点では Vfx/Se トラックの解決には使わない(Target=World/Anchor は contextRoot=null で
        // Anchor.LocalOffset をそのまま絶対座標として使う。実装メモ: [08] 実装メモ参照)。
        public Vector3 Position;

        // 逆方向: 演出データ(Kind=Signal のトラック)→ ゲームコードへの通知。
        public Action<string> OnSignal;
    }
}
