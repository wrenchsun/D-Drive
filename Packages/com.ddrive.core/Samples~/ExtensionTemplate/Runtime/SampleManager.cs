using System.Collections.Generic;
using DDrive.Foundation.Extension;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;
using DDrive.Foundation.Registry;

namespace SampleExtension
{
    // 種別 "Sample" の Manager(docs/1014 §2.2)。GameLoop に登録されて Tick される(自前の Update は持たない)。
    // 定常経路(Tick / Play)に LINQ・クロージャ・boxing を書かない。走査は for だけ。
    public sealed class SampleManager : IExtensionAssetManager
    {
        private sealed class Instance
        {
            public Handle<SampleMarker> Handle;
            public SampleData Data;
            public float Elapsed;
        }

        private readonly IAssetRegistry _registry;
        private readonly InstanceStore<SampleMarker, Instance> _store = new();
        private readonly List<Instance> _active = new();
        private bool _paused;

        public SampleManager(IAssetRegistry registry)
        {
            _registry = registry;
        }

        public AssetType Type => AssetType.Extension;

        public string KindKey => SampleKind.Key;

        public int ActiveCount => _active.Count;

        // ID で再生する。未登録の ID は Placeholder(空の SampleData)で動く(例外で止めない)。
        public Handle<SampleMarker> Play(AssetId<SampleMarker> id)
        {
            var data = _registry != null ? _registry.ResolveOrPlaceholder<SampleData>(id.Value) : null;
            return PlayData(data);
        }

        // Data を直接再生する(エディターの ▶ が使う)。
        public Handle<SampleMarker> PlayData(SampleData data)
        {
            if (data == null)
            {
                return Handle<SampleMarker>.Invalid;
            }

            var instance = new Instance { Data = data };
            instance.Handle = _store.Add(instance);
            _active.Add(instance);
            return instance.Handle;
        }

        public bool IsPlaying(Handle<SampleMarker> handle) => _store.IsValidSilent(handle);

        // 経過時間(秒)。再生していなければ 0。
        public float Elapsed(Handle<SampleMarker> handle) => _store.TryGetQuiet(handle, out var instance) ? instance.Elapsed : 0f;

        public void Stop(Handle<SampleMarker> handle)
        {
            if (!_store.TryGetQuiet(handle, out var instance))
            {
                return;
            }

            Release(instance);
        }

        public void Tick(float dt)
        {
            if (_paused)
            {
                return;
            }

            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var instance = _active[i];
                instance.Elapsed += dt;
                if (!instance.Data.Loop && instance.Elapsed >= instance.Data.Duration)
                {
                    Release(instance);
                }
            }
        }

        public void OnPause(PauseChannel channel, bool paused)
        {
            if (channel == PauseChannel.Gameplay)
            {
                _paused = paused;
            }
        }

        public void StopAll(StopReason reason)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                Release(_active[i]);
            }
        }

        public void OnSceneUnload() => StopAll(StopReason.SceneUnload);

        private void Release(Instance instance)
        {
            _active.Remove(instance);
            _store.Remove(instance.Handle);
        }
    }
}
