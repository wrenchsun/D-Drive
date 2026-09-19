using System;
using System.Collections.Generic;
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

        // Repeat=KeepWhilePlaying で出した実体。発火元の Instance が終わったら止める(ループする追従エフェクトの後始末)。
        private readonly List<(InstanceContext ctx, Handle<VfxMarker> handle)> _keptVfx = new();
        private readonly List<(InstanceContext ctx, Handle<SeMarker> handle)> _keptSe = new();
        private readonly List<(InstanceContext ctx, Handle<AnchorGroupMarker> handle)> _keptGroups = new();

        // プレビュー等が「イベントから出た実体」を追跡するためのフック(EditMode の手動 Simulate 用)。
        public event Action<Handle<VfxMarker>> OnVfxSpawned;
        public event Action<Handle<SeMarker>> OnSePlayed;
        public event Action<Handle<AnchorGroupMarker>> OnGroupPlayed;

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
            _bus.OnSessionEnded += HandleEnded;
        }

        public void Dispose()
        {
            _bus.OnEventFired -= HandleEvent;
            _bus.OnSessionEnded -= HandleEnded;
            _keptVfx.Clear();
            _keptSe.Clear();
            _keptGroups.Clear();
        }

        // 維持中(KeepWhilePlaying)の実体数(テスト / デバッグ表示用)。
        public int KeptCount => _keptVfx.Count + _keptSe.Count + _keptGroups.Count;

        private void HandleEnded(InstanceContext ctx)
        {
            // 既に自然終了した実体は Stop しない(無効 Handle 警告を出さない)。
            for (var i = _keptVfx.Count - 1; i >= 0; i--)
            {
                if (_keptVfx[i].ctx.Equals(ctx))
                {
                    if (_vfx != null && _vfx.IsPlaying(_keptVfx[i].handle))
                    {
                        _vfx.Stop(_keptVfx[i].handle);
                    }

                    _keptVfx.RemoveAt(i);
                }
            }

            for (var i = _keptSe.Count - 1; i >= 0; i--)
            {
                if (_keptSe[i].ctx.Equals(ctx))
                {
                    if (_audio != null && _audio.IsPlaying(_keptSe[i].handle))
                    {
                        _audio.Stop(_keptSe[i].handle);
                    }

                    _keptSe.RemoveAt(i);
                }
            }

            for (var i = _keptGroups.Count - 1; i >= 0; i--)
            {
                if (_keptGroups[i].ctx.Equals(ctx))
                {
                    if (_groups != null && _groups.IsPlaying(_keptGroups[i].handle))
                    {
                        _groups.Stop(_keptGroups[i].handle);
                    }

                    _keptGroups.RemoveAt(i);
                }
            }
        }

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
                            if (evt.Repeat == EventRepeat.KeepWhilePlaying)
                            {
                                _keptSe.Add((ctx, h));
                            }

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
                            if (evt.Repeat == EventRepeat.KeepWhilePlaying)
                            {
                                _keptVfx.Add((ctx, h));
                            }

                            OnVfxSpawned?.Invoke(h);
                        }
                    }

                    break;

                case AssetType.AnchorGroup:
                    if (_groups != null)
                    {
                        var h = _groups.Play(new AssetId<AnchorGroupMarker>(evt.Target.Id, AssetType.AnchorGroup), contextRoot);
                        if (_groups.IsPlaying(h))
                        {
                            if (evt.Repeat == EventRepeat.KeepWhilePlaying)
                            {
                                _keptGroups.Add((ctx, h));
                            }

                            OnGroupPlayed?.Invoke(h);
                        }
                    }

                    break;
            }
        }
    }
}
