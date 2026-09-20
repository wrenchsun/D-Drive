#if DDRIVE_NGO
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Prefab;
using Unity.Netcode;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §10(4-13) / [42_distribution.md] §2.3-9/§7 A-7(P1-1、2026-09-20) —
    // NetMode.Simulated はサーバー権威で複製されるため、Prefab に NetworkObject が無いと NGO 統合後に
    // Spawn できない。以前は `PrefabDataValidator`(DDrive.Runtime、NGO 非依存)内に `#if DDRIVE_NGO` で
    // 直接書かれていたが、DDrive.Runtime.asmdef が `Unity.Netcode.Runtime` を参照しなくなったため、
    // `NetworkObject` 型を直接参照するこの検査だけをこの NGO アセンブリへ切り出した(TypeCache による
    // IValidator の自動発見は全ロード済みアセンブリが対象なので、このアセンブリの型も
    // `CI.DiscoverValidators`/`Validation > Run All` に自動で載る。登録リストの追加は不要)。
    public sealed class PrefabNetworkObjectValidator : IValidator
    {
        public AssetType Target => AssetType.Prefab;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not PrefabData prefab || prefab.Flags.Net != DDrive.Foundation.Net.NetMode.Simulated)
            {
                yield break;
            }

            if (prefab.Prefab != null && prefab.Prefab.GetComponent<NetworkObject>() == null)
            {
                yield return ValidationResult.Error("Flags.Net=Simulated ですが Prefab に NetworkObject がありません(サーバー権威の複製には NetworkObject が必須です)");
            }
        }
    }
}
#endif // DDRIVE_NGO
