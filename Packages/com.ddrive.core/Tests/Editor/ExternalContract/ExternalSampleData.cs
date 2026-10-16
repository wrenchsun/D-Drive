using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace ExternalContract.Tests
{
    // ScriptableObject として保存するので、クラス名 = ファイル名にする(MonoScript の解決条件)。
    [AssetIdDefinition(AssetType.Extension, typeof(ExternalSampleMarker), "SMPID", KindKey = ExternalSampleKind.Key)]
    public sealed class ExternalSampleData : AssetDataBase
    {
    }
}
