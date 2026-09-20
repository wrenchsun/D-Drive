using UnityEditor;

namespace DDrive.Tests.Editor
{
    // [47_review_p_tickets_2026-09-20.md] P1-3 — テストの一時アセットフォルダを Assets 配下に統一する。
    // これまで多数のテストが `Packages/com.ddrive.core/Tests/Editor/TempXxx` に一時フォルダ・アセットを
    // 作っていたが、パッケージが git URL(またはレジストリ)経由で導入された持ち込み先では
    // `Library/PackageCache/com.ddrive.core@<hash>` は読み取り専用のため `AssetDatabase.CreateFolder`/
    // `CreateAsset` が失敗する([42_distribution.md] §2.1「持ち込み先で ON にしても通るように」に反する)。
    // `Assets/Tests/DDriveTemp/` は常に書き込み可能で、`AssetSearch`(Assets 配下を走査)にも正しく載る。
    //
    // ルート名は意図的に `Assets/Tests/…`(セグメントとして正確に "Tests")にしてある。
    // `CatalogAddressCoverageValidator`/`ContentHashCatalogCoverageValidator`/`AddressablesSync` 等、
    // 複数の既存コードが「パスに `/Tests/` を含むかどうか」でテスト専用データを除外する規約に乗っている
    // ため(旧パス `Packages/com.ddrive.core/Tests/Editor/…` もこの規約を満たしていた)、ここを崩すと
    // それらの Validator テストが偽陽性で壊れる(実際に `TempGameDataAddrCoverage`/`TempGameDataContentHash`
    // を使うテストで発生・修正済み)。
    public static class TestTempFolder
    {
        public const string Root = "Assets/Tests/DDriveTemp";

        // Root 直下に name フォルダを作る(親フォルダが無ければ先に作る)。既存なら何もしない。
        // 戻り値は "Assets/Tests/DDriveTemp/<name>"(呼び出し側の TempDir/TestRoot 定数と同じ形)。
        public static string CreateFolder(string name)
        {
            EnsureRoot();

            var path = Root + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(Root, name);
            }

            return path;
        }

        public static void EnsureRoot()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests"))
            {
                AssetDatabase.CreateFolder("Assets", "Tests");
            }

            if (!AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.CreateFolder("Assets/Tests", "DDriveTemp");
            }
        }
    }
}
