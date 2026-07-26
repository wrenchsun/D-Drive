namespace DDrive.Foundation.Net
{
    // Foundation は Time.time を直接使わずこれ経由で取得する(ネット時は NetworkTime に差し替え)。
    public interface ITimeSource
    {
        double Time { get; }
        float DeltaTime { get; }
    }
}
