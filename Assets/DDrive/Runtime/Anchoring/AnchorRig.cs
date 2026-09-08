using UnityEngine;

namespace DDrive.Runtime.Anchoring
{
    // AnchorPoint 群をまとめるルートマーカー([04_vfx.md] §2.5)。
    // Tools > D-Drive > Generate > Anchor プレハブを生成 で雛形が作られ、子に AnchorPoint を
    // 追加・配置して使う。キャラクターの子に置けば PlaySe/Vfx.Spawn の contextRoot 経由で、
    // 環境オブジェクトとして単独配置すればその Transform を直接渡して解決される。
    public sealed class AnchorRig : MonoBehaviour
    {
        public AnchorPoint[] GetPoints() => GetComponentsInChildren<AnchorPoint>(true);
    }
}
