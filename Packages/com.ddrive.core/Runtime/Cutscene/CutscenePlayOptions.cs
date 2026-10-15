using UnityEngine;

namespace DDrive.Runtime.Cutscene
{
    // [26_timeline.md] §4.6.6(MS-2、2026-10-15) — Cutscene.Play / PlayData の追加指定。ローカル専用で、ネットでは送らない
    // (受信側は従来どおり Camera.main・Data.LockInput どおり)。将来の追加に耐えるよう引数ではなく struct にしてある。
    public readonly struct CutscenePlayOptions
    {
        // カメラクリップ・MainCamera バインドの対象。null = 従来どおり Camera.main。
        // 街のディスプレイ等の、RenderTexture を持つ専用 Camera を渡す(RT への出力は持ち込み先がその Camera の targetTexture に設定する)。
        public readonly Camera Camera;

        // true なら Data.LockInput でも入力ロックに数えない(看板の中で流す演出がプレイヤーの入力を止めないため)。
        public readonly bool IgnoreInputLock;

        public CutscenePlayOptions(Camera camera = null, bool ignoreInputLock = false)
        {
            Camera = camera;
            IgnoreInputLock = ignoreInputLock;
        }
    }
}
