using DDrive.Foundation.Values;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
    // [26_timeline.md] §4.6.4 — ピント(DoF)の書き先。Off=一切書かない/Volume=Volume の DepthOfField(既定)
    // /CameraOnly=Camera.focusDistance のみ(HDRP 移植時の逃げ道、Volume は作らない)。
    public enum CameraFocusMode
    {
        Off,
        Volume,
        CameraOnly,
    }

    // [26_timeline.md] §4.6.2 — Maya カメラのカーブ + ステップ fps + ゲームカメラとのブレンド設定。
    // 取り込み(6-10c)が自動生成し、デザイナーは StepFps/BlendIn/BlendOut/Focus だけを触る。
    public sealed class CutsceneCameraClip : PlayableAsset, ITimelineClipAsset
    {
        [Header("焼かれたカーブ(取り込みが生成。再取り込みで上書き)")]
        public AnimationCurve PosX = new();
        public AnimationCurve PosY = new();
        public AnimationCurve PosZ = new();
        public AnimationCurve RotX = new();
        public AnimationCurve RotY = new();
        public AnimationCurve RotZ = new();
        public AnimationCurve RotW = AnimationCurve.Constant(0f, 0f, 1f);
        public AnimationCurve FieldOfView = AnimationCurve.Constant(0f, 0f, 60f);
        public AnimationCurve FocalLengthMm = new();
        public AnimationCurve FocusDistance = new();
        public AnimationCurve Aperture = new();

        [Header("デザイナーが触る設定(再取り込みで保持)")]
        [Tooltip("0 = 量子化なし(元 fps のまま)。カメラの評価時刻だけを量子化する([26] §4.6.3)。")]
        public float StepFps;

        [Tooltip("クリップ先頭からの 0→1 ブレンド(ゲームカメラ→Timelineカメラ)。先頭クリップにのみ適用する。")]
        public ValueDef BlendIn = ValueDef.Constant01(1f);

        [Tooltip("クリップ末尾までの 1→0 ブレンド(Timelineカメラ→ゲームカメラ)。末尾クリップにのみ適用する。")]
        public ValueDef BlendOut = ValueDef.Constant01(1f);

        public CameraFocusMode Focus = CameraFocusMode.Volume;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CutsceneCameraBehaviour>.Create(graph);
            playable.GetBehaviour().Clip = this;
            return playable;
        }

        // クリップ内ローカル時刻(秒、0 起点)を StepFps で量子化する。0 以下は無効(量子化なし)。
        public float QuantizeTime(float localTime)
        {
            if (StepFps <= 0f)
            {
                return localTime;
            }

            return Mathf.Floor(localTime * StepFps) / StepFps;
        }
    }

    // クリップ自体は評価しない(Mixer が Clip 参照を直接読んでカーブを評価する)。
    // ProcessFrame を持たない薄いデータ受け渡し用。
    public sealed class CutsceneCameraBehaviour : PlayableBehaviour
    {
        public CutsceneCameraClip Clip;
    }

    [TrackClipType(typeof(CutsceneCameraClip))]
    [TrackColor(0.95f, 0.85f, 0.2f)]
    public sealed class CutsceneCameraTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<CutsceneCameraMixerBehaviour>.Create(graph, inputCount);
            mixer.GetBehaviour().Owner = go;
            return mixer;
        }
    }

    // [26_timeline.md] §4.6.2/§4.6.3 — 複数クリップ(ショット切替)の重み付き合成 + StepFps 量子化。
    // 定常経路(毎フレーム評価)のため LINQ・クロージャ・boxing を避ける(CLAUDE.md §0-3)。
    public sealed class CutsceneCameraMixerBehaviour : PlayableBehaviour
    {
        public GameObject Owner;
        private CutsceneCameraStateHolder _holder;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_holder == null && Owner != null)
            {
                _holder = Owner.GetComponent<CutsceneCameraStateHolder>();
            }

            if (_holder == null)
            {
                return;
            }

            var count = playable.GetInputCount();
            var totalWeight = 0f;
            var pos = Vector3.zero;
            var rotX = 0f;
            var rotY = 0f;
            var rotZ = 0f;
            var rotW = 0f;
            var fov = 0f;
            var focusDistance = 0f;
            var aperture = 0f;
            var focalLength = 0f;
            var gameBlend = 0f;
            var focusMode = CameraFocusMode.Off;
            var any = false;

            for (var i = 0; i < count; i++)
            {
                var weight = playable.GetInputWeight(i);
                if (weight <= 0f)
                {
                    continue;
                }

                var input = (ScriptPlayable<CutsceneCameraBehaviour>)playable.GetInput(i);
                var clip = input.GetBehaviour().Clip;
                if (clip == null)
                {
                    continue;
                }

                var localTime = (float)input.GetTime();
                var duration = (float)input.GetDuration();
                var sampleTime = clip.QuantizeTime(localTime);

                pos += weight * new Vector3(
                    clip.PosX.Evaluate(sampleTime),
                    clip.PosY.Evaluate(sampleTime),
                    clip.PosZ.Evaluate(sampleTime));

                rotX += weight * clip.RotX.Evaluate(sampleTime);
                rotY += weight * clip.RotY.Evaluate(sampleTime);
                rotZ += weight * clip.RotZ.Evaluate(sampleTime);
                rotW += weight * clip.RotW.Evaluate(sampleTime);

                fov += weight * clip.FieldOfView.Evaluate(sampleTime);
                focusDistance += weight * clip.FocusDistance.Evaluate(sampleTime);
                aperture += weight * clip.Aperture.Evaluate(sampleTime);
                focalLength += weight * clip.FocalLengthMm.Evaluate(sampleTime);

                // [26] §4.6.2 — ゲームカメラとのブレンドは「最初のクリップの頭」と「最後のクリップの尻」だけに
                // 掛ける(中間クリップは常に 1 = Timeline カメラそのまま)。
                var g = 1f;
                if (i == 0)
                {
                    var blendInDur = clip.BlendIn.Duration;
                    if (blendInDur > 0f && localTime < blendInDur)
                    {
                        g = Mathf.Min(g, clip.BlendIn.Evaluate(Mathf.Clamp01(localTime / blendInDur)));
                    }
                }

                if (i == count - 1)
                {
                    var blendOutDur = clip.BlendOut.Duration;
                    if (blendOutDur > 0f && duration > 0f)
                    {
                        var remain = duration - localTime;
                        if (remain < blendOutDur)
                        {
                            var t = Mathf.Clamp01(1f - remain / blendOutDur);
                            g = Mathf.Min(g, clip.BlendOut.Evaluate(1f - t));
                        }
                    }
                }

                gameBlend += weight * g;
                totalWeight += weight;
                focusMode = clip.Focus;
                any = true;
            }

            if (!any || totalWeight <= 0f)
            {
                _holder.HasData = false;
                return;
            }

            var invWeight = 1f / totalWeight;
            _holder.HasData = true;
            _holder.LocalPos = pos * invWeight;
            _holder.LocalRot = NormalizeSafe(new Quaternion(rotX * invWeight, rotY * invWeight, rotZ * invWeight, rotW * invWeight));
            _holder.Fov = fov * invWeight;
            _holder.FocusDistance = focusDistance * invWeight;
            _holder.Aperture = aperture * invWeight;
            _holder.FocalLength = focalLength * invWeight;
            _holder.GameBlendWeight = Mathf.Clamp01(gameBlend * invWeight);
            _holder.Focus = focusMode;
        }

        private static Quaternion NormalizeSafe(Quaternion q)
        {
            var lenSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (lenSq <= 1e-8f)
            {
                return Quaternion.identity;
            }

            var invLen = 1f / Mathf.Sqrt(lenSq);
            return new Quaternion(q.x * invLen, q.y * invLen, q.z * invLen, q.w * invLen);
        }
    }

    // [26_timeline.md] §4.6.2 — CutsceneCameraMixerBehaviour(Update フェーズ)と DDriveCutsceneCameraApplier
    // (LateUpdate フェーズ、実行順 1000)の間で 1 フレーム分の計算結果を橋渡しする素朴なデータ置き場。
    // CutsceneManager.RentDirector が CutsceneRoot に 1 つ付ける(free-list で再利用されるため使い回す)。
    public sealed class CutsceneCameraStateHolder : MonoBehaviour
    {
        public bool HasData;
        public Vector3 LocalPos;
        public Quaternion LocalRot = Quaternion.identity;
        public float Fov = 60f;
        public float FocusDistance;
        public float Aperture;
        public float FocalLength;
        public float GameBlendWeight;
        public CameraFocusMode Focus = CameraFocusMode.Off;
    }
}
