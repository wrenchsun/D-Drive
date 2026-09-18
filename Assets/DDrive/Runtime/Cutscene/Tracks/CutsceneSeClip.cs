using DDrive.Foundation.Handle;
using DDrive.Runtime.Audio;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using SeId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Audio.SeMarker>;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.3(6-10b) — SE クリップ。区間の長さはループ SE の鳴らし続ける時間として使う
    // (OneShot は開始点のみ使用、鳴らした後は Stop() を呼んでも無害)。実再生は AudioManager(静的ファサード
    // Audio.PlaySe)に委譲する(ADR-4: D-Drive の禁止 API である AudioSource の Play 直呼びを Timeline 標準
    // Audio トラックの代わりに回避する狙いそのもの、[26] §1.3)。
    public sealed class CutsceneSeClip : PlayableAsset
    {
        [Tooltip("再生する SeId。区間の長さはループ SE の鳴らし続ける時間として使う(OneShot は開始点のみ使用)。")]
        public SeId SeId;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutsceneSeBehaviour>.Create(graph);
            playable.GetBehaviour().SeId = SeId;
            return playable;
        }
    }

    // [26_timeline.md] §4.4 — 編集中のスクラブでは発火しない(Application.isPlaying のときだけ発音する)。
    // 区間から抜けたら(OnBehaviourPause、Cancel/Skip による Director 破棄も含む)鳴らした SE を止める
    // (ループ SE 向け。OneShot は既に鳴り終わっているため IsPlaying=false で無害)。
    public sealed class CutsceneSeBehaviour : PlayableBehaviour
    {
        public SeId SeId;
        private Handle<SeMarker> _handle;
        private bool _fired;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_fired || !Application.isPlaying || !SeId.IsValid)
            {
                return;
            }

            _fired = true;
            var root = ResolveRoot(playerData);
            // 完全修飾で呼ぶ(DDrive.Runtime 配下に同名の子ネームスペース DDrive.Runtime.Audio があるため、
            // このファイルの名前空間〔DDrive.Runtime.Cutscene.Tracks〕から素の `Audio` はネームスペースと
            // 解釈され、`Audio.PlaySe` が「ネームスペースの静的クラス」として解決できずコンパイルエラーに
            // なる。[26_timeline.md] §4.3 実装メモ)。
            _handle = root != null ? DDrive.Runtime.Audio.Audio.PlaySe(SeId, root) : DDrive.Runtime.Audio.Audio.PlaySe(SeId);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!_fired)
            {
                return;
            }

            _fired = false;
            // Audio 静的ファサードに IsPlaying は無い(AudioManager インスタンスにしか無い)。Stop() は
            // 無効/既に停止済みの Handle でも安全な no-op なので、無条件に呼ぶ(AudioManager.Stop 参照)。
            DDrive.Runtime.Audio.Audio.Stop(_handle);
        }

        private static Transform ResolveRoot(object playerData)
        {
            if (playerData is Transform t)
            {
                return t;
            }

            if (playerData is Animator anim)
            {
                return anim.transform;
            }

            return null;
        }
    }

    [TrackClipType(typeof(CutsceneSeClip))]
    [TrackBindingType(typeof(Transform))]
    [TrackColor(0.2f, 0.7f, 0.9f)]
    public sealed class CutsceneSeTrack : TrackAsset
    {
    }
}
