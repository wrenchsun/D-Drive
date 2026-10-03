using System.Collections.Generic;
using DDrive.Runtime.Viewing;
using UnityEngine;

namespace ExternalPackage.Fake
{
    // 外部パッケージ(分割画面など)を模した IViewProvider(FC-3 / E-18)。D-Drive の公開 API だけで書く。
    // Subject が null でなければ、subject が Subject と同じときだけ自分のカメラの姿勢を返す。
    public sealed class ExternalViewProvider : IViewProvider
    {
        public static readonly List<string> CallOrder = new();

        public string Name = "provider";
        public Transform Subject;
        public Camera Camera;
        public bool Throw;
        public int Calls;

        public bool TryGetView(Transform subject, out ViewPose pose)
        {
            Calls++;
            CallOrder.Add(Name);
            if (Throw)
            {
                throw new System.InvalidOperationException("ExternalViewProvider boom");
            }

            if (Camera == null || (Subject != null && subject != Subject))
            {
                pose = default;
                return false;
            }

            var t = Camera.transform;
            pose = new ViewPose(t.position, t.rotation, Camera.fieldOfView, ViewSource.Override, Camera);
            return true;
        }
    }

    // 破棄されうる MonoBehaviour 実装の IViewProvider(破棄済みは飛ばされて取り除かれる)。
    public sealed class ExternalViewBehaviour : MonoBehaviour, IViewProvider
    {
        public int Calls;

        public bool TryGetView(Transform subject, out ViewPose pose)
        {
            Calls++;
            pose = new ViewPose(Vector3.one, Quaternion.identity, 33f, ViewSource.Override, null);
            return true;
        }
    }

    // LateUpdate で ViewCamera.TryGetCurrent を呼ぶ外部コンポーネント(T-Drive の Runner 相当)。
    // 実行順 1001 = DDriveCutsceneCameraApplier(1000)より後。
    [DefaultExecutionOrder(1001)]
    public sealed class ExternalLateViewReader : MonoBehaviour
    {
        public bool Found;
        public ViewPose Pose;
        public int LateUpdates;

        private void LateUpdate()
        {
            LateUpdates++;
            Found = ViewCamera.TryGetCurrent(null, out Pose);
        }
    }
}
