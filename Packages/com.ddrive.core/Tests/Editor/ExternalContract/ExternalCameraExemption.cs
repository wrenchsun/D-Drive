using System;
using System.Collections.Generic;
using DDrive.Editor.Validation;

namespace ExternalPackage.Fake
{
    // [docs/42 §5.14 E-23] 実行順の検査の除外(ICameraExecutionOrderExemptionProvider)の外部実装のダミー。
    // TypeCache で常時発見されるため、普段は何も宣言しない: Probe のフラグが立っているテスト中だけ名乗る
    // (名乗らないとき = 空 = D-Drive 側が無視する。普段の Validation > Run All を汚さない)。
    public static class ExternalCameraExemptionProbe
    {
        public static bool Enabled;          // ExternalCameraReadOnlyProvider が宣言する
        public static bool EmptyReason;      // ↑ の理由を空にする(無効 + Warning)
        public static bool Throws;           // ExternalThrowingCameraExemptionProvider が例外を投げる
        public static int HiddenNestedProviderCalls;  // 外側が internal の入れ子(発見されないので 0 のまま)
        public static int VisibleNestedProviderCalls; // 外側も public の入れ子(発見されるので呼ばれる)

        public static void Reset()
        {
            Enabled = false;
            EmptyReason = false;
            Throws = false;
            HiddenNestedProviderCalls = 0;
            VisibleNestedProviderCalls = 0;
            CameraExecutionOrderExemptions.ResetProviderCacheForTests();
        }
    }

    // 外部パッケージの「カメラを読むだけ」のスクリプトを模す型(MonoBehaviour である必要はない: 検査はスクリプト情報の注入で行う)。
    public sealed class ExternalCameraReader
    {
    }

    public sealed class ExternalCameraReaderOther
    {
    }

    public sealed class ExternalCameraReadOnlyProvider : ICameraExecutionOrderExemptionProvider
    {
        public IEnumerable<CameraExecutionOrderExemption> GetExemptions()
        {
            if (!ExternalCameraExemptionProbe.Enabled)
            {
                yield break;
            }

            // 型は System.Type で渡す(外部パッケージの Editor アセンブリはランタイムの型を直接参照できる。D-Drive の属性は不要)。
            yield return new CameraExecutionOrderExemption(
                typeof(ExternalCameraReader),
                ExternalCameraExemptionProbe.EmptyReason ? " " : "カメラを読むだけ(書き込みはしない)");
        }
    }

    public sealed class ExternalThrowingCameraExemptionProvider : ICameraExecutionOrderExemptionProvider
    {
        public IEnumerable<CameraExecutionOrderExemption> GetExemptions()
        {
            if (ExternalCameraExemptionProbe.Throws)
            {
                throw new InvalidOperationException("external camera exemption provider failure (test)");
            }

            return Array.Empty<CameraExecutionOrderExemption>();
        }
    }

    internal static class ExternalHiddenCameraExemptionHost
    {
        public sealed class ExternalHiddenNestedCameraExemptionProvider : ICameraExecutionOrderExemptionProvider
        {
            public IEnumerable<CameraExecutionOrderExemption> GetExemptions()
            {
                ExternalCameraExemptionProbe.HiddenNestedProviderCalls++;
                return Array.Empty<CameraExecutionOrderExemption>();
            }
        }
    }

    public static class ExternalVisibleCameraExemptionHost
    {
        public sealed class ExternalVisibleNestedCameraExemptionProvider : ICameraExecutionOrderExemptionProvider
        {
            public IEnumerable<CameraExecutionOrderExemption> GetExemptions()
            {
                ExternalCameraExemptionProbe.VisibleNestedProviderCalls++;
                return Array.Empty<CameraExecutionOrderExemption>();
            }
        }
    }
}
