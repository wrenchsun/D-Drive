using UnityEngine;

namespace DDrive.Runtime.Viewing
{
    // [51_tdrive_integration.md] §4.4(FC-3) — 分割画面・独自カメラ制御が実装する視点の提供元。
    // ViewCamera.Register で登録する。subject = 補正をかける対象(顔のオーナー等。null 可)。
    // このプロバイダが担当しない subject なら false を返す(次のプロバイダ / Camera.main に進む)。
    public interface IViewProvider
    {
        bool TryGetView(Transform subject, out ViewPose pose);
    }
}
