#if DDRIVE_NGO
using Unity.Netcode;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) — `DDriveRuntimeBootstrap`(DDrive.Runtime、
    // NGO 非依存)が直接持っていた `NetworkManagerRef`/`NgoBridgeRef` の Inspector 直参照は、NGO 型を
    // 参照できる DDrive.Runtime.Ngo アセンブリ側のこの補助コンポーネントへ移した。
    // NGO を使うシーン(NetCheckScene 等)では DDriveRuntimeBootstrap と同じ GameObject にこれを追加し、
    // 必要なら NetworkManager/NgoNetBridge を明示的に割り当てる(未設定ならシーンから自動検索する。
    // 既存の挙動を変えない)。`NgoBridgeFactoryInstaller` がシーンから `FindAnyObjectByType` で
    // このコンポーネントを探して読む。
    public sealed class DDriveNgoBootstrapHook : MonoBehaviour
    {
        [Tooltip("Ngo モードのとき使う NetworkManager。未設定ならシーンから自動検索する")]
        public NetworkManager NetworkManagerRef;

        [Tooltip("Ngo モードのとき使う NgoNetBridge(NetworkManager と同じ NetworkObject に付ける想定)。未設定ならシーンから自動検索する")]
        public NgoNetBridge NgoBridgeRef;
    }
}
#endif // DDRIVE_NGO
