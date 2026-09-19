namespace DDrive.Foundation.Net
{
    public sealed class NetworkTimeSource : ITimeSource
    {
        private readonly INetBridge _bridge;

        public NetworkTimeSource(INetBridge bridge)
        {
            _bridge = bridge;
        }

        public double Time => _bridge.NetworkTime;
        public float DeltaTime => UnityEngine.Time.deltaTime;
    }
}
