using System;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace DDrive.Tests.Performance
{
    // [11_tasks.md] 6-2(パフォーマンス計測・0 alloc 検証) — 定常経路(Tick 等、フレームごとに必ず
    // 通る経路)の GC alloc を検証する共通ヘルパー。
    //
    // 判定方法について: Unity.PerformanceTesting の Measure.Method(...).GC() は計測結果を
    // Performance テストレポート(TestResults の XML、Tools/CI/Summarize-Results.ps1 と同様に
    // 人が読める形にできる)に記録するだけで、それ自体は NUnit の Assert をしない
    // (ダッシュボード比較用の生データを残す設計、[PerformanceTest.cs] 参照)。
    // CI で実際に fail させる 0 alloc ゲートは、既存の DDrive の慣習
    // (UiTweenTests.Tick_EightRunningTweens_AllocatesNothing)に合わせて
    // GC.GetAllocatedBytesForCurrentThread() の差分を直接 Assert する方式にしている。
    internal static class AllocProbe
    {
        // Mono ランタイムでは計測に僅かなノイズが乗ることがあるため、0 を理想としつつ 1KB 未満を許容する
        // (UiTweenTests と同じ閾値)。
        private const long ZeroAllocToleranceBytes = 1024;

        // 完全に 0 alloc であるべき経路(Tick 等、既にアクティブな Instance を回すだけの呼び出し)用。
        public static void AssertZeroAlloc(
            string label,
            Action warmup,
            Action measured,
            int warmupIterations = 5,
            int measuredIterations = 100)
        {
            for (var i = 0; i < warmupIterations; i++)
            {
                warmup();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < measuredIterations; i++)
            {
                measured();
            }

            var after = GC.GetAllocatedBytesForCurrentThread();
            var delta = after - before;

            Assert.Less(delta, ZeroAllocToleranceBytes,
                $"{label}: 定常経路(Tick 等)は 0 alloc であるべき(delta={delta} bytes / {measuredIterations} 回)");
        }

        // Spawn/Play 系(呼び出し側の設計上「1 アクションにつき 1 回」であり、Instance(class)を
        // 1 個 new する既存設計を持つものが多い。6-2 で判明した既知課題、[12_review.md] §3 参照)は
        // 厳密な 0 alloc を強制せず、Performance レポートに記録するだけに留める
        // (呼び出し側は [Test, Performance] を付けること。CI の fail 条件にはしない)。
        public static void RecordGc(string sampleGroupName, Action action, int warmupCount = 3, int measurementCount = 10)
        {
            Measure.Method(action)
                .SampleGroup(sampleGroupName)
                .WarmupCount(warmupCount)
                .MeasurementCount(measurementCount)
                .GC()
                .Run();
        }
    }
}
