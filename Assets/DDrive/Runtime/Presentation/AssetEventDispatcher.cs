using System;
using DDrive.Foundation.Event;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Registry;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Vfx;
using UnityEngine;

namespace DDrive.Runtime.Presentation
{
    // [02_core_framework.md] §3 — AssetEvent(Action=PlayAsset)を種別に応じて各 Manager へディスパッチする。
    // AnimManager の Frame/Time イベント(「15 フレーム目で SE」等)を実際に鳴らす配線がこれ。
    // 発火元 Instance の Transform(Animator など)を contextRoot として渡すので、SE/VFX の Anchor(BoneName 等)が
    // そのモデルの階層から解決される。Presentation 層(Phase 5)が出来たらそちらの標準配線に統合する。
    public sealed class AssetEventDispatcher : IDisposable
    {
        private readonly EventBus _bus;
        private readonly IAssetRegistry _registry;
        private readonly AudioManager _audio;
        private readonly VfxManager _vfx;
        private readonly AnchorGroupPlayer _groups;
        private readonly Func<InstanceContext, Transform> _contextResolver;

        // プレビュー等が「イベントから出た実体」を追跡するためのフック(EditMode の手動 Simulate 用)。
        public event Action<Handle<VfxMarker>> OnVfxSpawned;
        public event Action<Handle<SeMarker>> OnSePlayed;

        public AssetEventDispatcher(EventBus bus, IAssetRegistry registry, AudioManager audio, VfxManager vfx,
            Func<InstanceContext, Transform> contextResolver, AnchorGroupPlayer groups = null)
        {
            _bus = bus;
            _registry = registry;
            _audio = audio;
            _vfx = vfx;
            _groups = groups;
            _contextResolver = contextResolver;
            _bus.OnEventFired += HandleEvent;
        }

        public void Dispose() => _bus.OnEventFired -= HandleEvent;

        private void HandleEvent(InstanceContext ctx, AssetEvent evt)
        {
            if (evt.Action != EventAction.PlayAsset || !evt.Target.IsAssigned)
            {
                return;
            }

            var contextRoot = _contextResolver?.Invoke(ctx);
            switch (evt.Target.Type)
            {
                case AssetType.Se:
                    if (_audio != null)
                    {
                        var h = _audio.PlaySeData(_registry.ResolveOrPlaceholder<SeData>(evt.Target.Id), contextRoot: contextRoot);
                        if (_audio.IsPlaying(h))
                        {
                            OnSePlayed?.Invoke(h);
                        }
                    }

                    break;

                case AssetType.Vfx:
                    if (_vfx != null)
                    {
                        var h = _vfx.SpawnData(_registry.ResolveOrPlaceholder<VfxData>(evt.Target.Id), contextRoot: contextRoot);
                        if (_vfx.IsPlaying(h))
                        {
                            OnVfxSpawned?.Invoke(h);
                        }
                    }

                    break;

                case AssetType.AnchorGroup:
                    _groups?.Play(new AssetId<AnchorGroupMarker>(evt.Target.Id, AssetType.AnchorGroup), contextRoot);
                    break;
            }
        }
    }
}
