using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;

namespace SampleExtension
{
    // 静的ファサード(プログラマーが ID だけで呼ぶ入口。Audio.PlaySe / Vfx.Play と同じ形)。
    // SampleModule が Bind / Unbind する。未 Bind のときは何もしない(無効な Handle を返す)。例外で止めない。
    public static class Samples
    {
        private static SampleManager _manager;

        public static bool IsBound => _manager != null;

        public static void Bind(SampleManager manager) => _manager = manager;

        public static Handle<SampleMarker> Play(AssetId<SampleMarker> id)
            => _manager != null ? _manager.Play(id) : Handle<SampleMarker>.Invalid;

        public static void Stop(Handle<SampleMarker> handle) => _manager?.Stop(handle);

        public static bool IsPlaying(Handle<SampleMarker> handle) => _manager != null && _manager.IsPlaying(handle);
    }
}
