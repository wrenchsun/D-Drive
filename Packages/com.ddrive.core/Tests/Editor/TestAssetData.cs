using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;

namespace DDrive.Tests.Editor
{
    public readonly struct TestAssetMarker
    {
    }

    // AssetDataBase は abstract のため、テスト用の最小具象クラスを一つ用意する。
    [AssetIdDefinition(AssetType.Se, typeof(TestAssetMarker), "DDRIVE_TEST_ID")]
    public sealed class TestAssetData : AssetDataBase
    {
    }
}
