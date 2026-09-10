using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEngine;
using AnchorGroupId = DDrive.Foundation.Identity.AssetId<DDrive.Runtime.Anchoring.AnchorGroupMarker>;

namespace DDrive.Runtime.Anchoring
{
    // [22_anchor_group.md] §3.5 — 配置セットの再生。AnchorGroupPlanner の計画を VfxManager / AudioManager で実行し、
    // 全点分の Handle を 1 つの GroupHandle にまとめる(Stop/Kill でまとめて止める)。
    // 定常経路の GC alloc を避けるため、計画リストと Instance は使い回す。
    public sealed class AnchorGroupPlayer
    {
        private sealed class GroupInstance
        {
            public readonly List<Handle<VfxMarker>> Vfx = new();
            public readonly List<Handle<SeMarker>> Se = new();
        }

        private readonly IAssetRegistry _registry;
        private readonly VfxManager _vfx;
        private readonly AudioManager _audio;
        private readonly InstanceStore<AnchorGroupMarker, GroupInstance> _instances = new();
        private readonly List<Handle<AnchorGroupMarker>> _active = new();
        private readonly List<AnchorGroupAction> _plan = new(64);
        private readonly Stack<GroupInstance> _free = new();

        public AnchorGroupPlayer(IAssetRegistry registry, VfxManager vfx, AudioManager audio)
        {
            _registry = registry;
            _vfx = vfx;
            _audio = audio;
        }

        public int ActiveCount => _active.Count;

        // この配置セットが出した VFX Handle を into に追加する(エディタのシーンプレビューが DontSave / 手動 Simulate の
        // 台帳へ引き取るため。無効 Handle なら何もしない)。戻り値は追加数。
        public int CollectVfxHandles(Handle<AnchorGroupMarker> handle, List<Handle<VfxMarker>> into)
        {
            if (into == null || !_instances.TryGet(handle, out var instance))
            {
                return 0;
            }

            for (var i = 0; i < instance.Vfx.Count; i++)
            {
                into.Add(instance.Vfx[i]);
            }

            return instance.Vfx.Count;
        }

        public Handle<AnchorGroupMarker> Play(AnchorGroupId id, Transform contextRoot = null)
            => PlayData(_registry.ResolveOrPlaceholder<AnchorGroupData>(id.Value), contextRoot);

        public Handle<AnchorGroupMarker> PlayData(AnchorGroupData group, Transform contextRoot = null)
        {
            if (group == null)
            {
                return Handle<AnchorGroupMarker>.Invalid;
            }

            _plan.Clear();
            AnchorGroupPlanner.Plan(_registry, group, sampleRandom: true, _plan);
            if (_plan.Count == 0)
            {
                return Handle<AnchorGroupMarker>.Invalid;
            }

            var instance = _free.Count > 0 ? _free.Pop() : new GroupInstance();
            for (var i = 0; i < _plan.Count; i++)
            {
                var action = _plan[i];
                if (action.Kind == AnchorGroupActionKind.Vfx)
                {
                    if (_vfx == null)
                    {
                        continue;
                    }

                    var data = _registry.ResolveOrPlaceholder<VfxData>(action.AssetId);
                    var h = _vfx.SpawnData(data, action.Spec, contextRoot);
                    if (_vfx.IsPlaying(h))
                    {
                        instance.Vfx.Add(h);
                    }
                }
                else
                {
                    if (_audio == null)
                    {
                        continue;
                    }

                    var data = _registry.ResolveOrPlaceholder<SeData>(action.AssetId);
                    var h = _audio.PlaySeData(data, action.Spec, contextRoot);
                    if (_audio.IsPlaying(h))
                    {
                        instance.Se.Add(h);
                    }
                }
            }

            _plan.Clear();

            if (instance.Vfx.Count == 0 && instance.Se.Count == 0)
            {
                _free.Push(instance);
                return Handle<AnchorGroupMarker>.Invalid;
            }

            var handle = _instances.Add(instance);
            _active.Add(handle);
            return handle;
        }

        // どれか 1 つでも再生中(生成待ち含む)なら true。
        public bool IsPlaying(Handle<AnchorGroupMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return false;
            }

            for (var i = 0; i < instance.Vfx.Count; i++)
            {
                if (_vfx.IsPlaying(instance.Vfx[i]))
                {
                    return true;
                }
            }

            for (var i = 0; i < instance.Se.Count; i++)
            {
                if (_audio.IsPlaying(instance.Se[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public void Stop(Handle<AnchorGroupMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            for (var i = 0; i < instance.Vfx.Count; i++)
            {
                _vfx.Stop(instance.Vfx[i]);
            }

            for (var i = 0; i < instance.Se.Count; i++)
            {
                _audio.Stop(instance.Se[i]);
            }

            Release(handle, instance);
        }

        public void Kill(Handle<AnchorGroupMarker> handle)
        {
            if (!_instances.TryGet(handle, out var instance))
            {
                return;
            }

            for (var i = 0; i < instance.Vfx.Count; i++)
            {
                _vfx.Kill(instance.Vfx[i]);
            }

            for (var i = 0; i < instance.Se.Count; i++)
            {
                _audio.Stop(instance.Se[i]);
            }

            Release(handle, instance);
        }

        public void StopAll()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                Kill(_active[i]);
            }
        }

        // 全点が終わったグループの台帳を掃除する(Manager の Tick の後に呼ぶ)。
        public void Tick()
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                if (!_instances.TryGet(handle, out var instance))
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (!IsPlaying(handle))
                {
                    Release(handle, instance);
                }
            }
        }

        private void Release(Handle<AnchorGroupMarker> handle, GroupInstance instance)
        {
            instance.Vfx.Clear();
            instance.Se.Clear();
            _instances.Remove(handle);
            _active.Remove(handle);
            _free.Push(instance);
        }
    }

    // 静的ファサード。未 Bind は no-op(例外で止めない)。
    public static class Anchors
    {
        private static AnchorGroupPlayer _instance;

        public static void Bind(AnchorGroupPlayer instance) => _instance = instance;

        public static bool IsBound => _instance != null;

        public static Handle<AnchorGroupMarker> Play(AnchorGroupId id) => _instance?.Play(id) ?? Handle<AnchorGroupMarker>.Invalid;

        public static Handle<AnchorGroupMarker> Play(AnchorGroupId id, Transform contextRoot) => _instance?.Play(id, contextRoot) ?? Handle<AnchorGroupMarker>.Invalid;

        public static void Stop(Handle<AnchorGroupMarker> h) => _instance?.Stop(h);

        public static void Kill(Handle<AnchorGroupMarker> h) => _instance?.Kill(h);

        public static bool IsPlaying(Handle<AnchorGroupMarker> h) => _instance?.IsPlaying(h) ?? false;
    }
}
