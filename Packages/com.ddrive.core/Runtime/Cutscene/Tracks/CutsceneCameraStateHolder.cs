using DDrive.Foundation.Values;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace DDrive.Runtime.Cutscene.Tracks
{
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
