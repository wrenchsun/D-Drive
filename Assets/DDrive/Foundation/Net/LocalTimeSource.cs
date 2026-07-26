namespace DDrive.Foundation.Net
{
    public sealed class LocalTimeSource : ITimeSource
    {
        public double Time => UnityEngine.Time.timeAsDouble;
        public float DeltaTime => UnityEngine.Time.deltaTime;
    }
}
