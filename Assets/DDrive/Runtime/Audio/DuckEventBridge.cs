using System;
using DDrive.Foundation.Event;
using DDrive.Foundation.Manager;
using DDrive.Foundation.Pause;

namespace DDrive.Runtime.Audio
{
    // AssetEvent.Action=Duck を AudioDuckService の Push/Pop に橋渡しする。
    // エンコーディング(暫定): CustomKey = DuckChannel 名、Param.FloatValue = dB(Push 時)。
    // Trigger が OnDisable/OnDestroy なら Pop、それ以外(OnEnable 等)なら Push として扱う
    // (03_audio.md §6 の「OnEnable に Duck、OnDisable に PopDuck」を Trigger 種別で表現する)。
    public sealed class DuckEventBridge : IDisposable
    {
        private readonly EventBus _eventBus;
        private readonly AudioDuckService _duck;

        public DuckEventBridge(EventBus eventBus, AudioDuckService duck)
        {
            _eventBus = eventBus;
            _duck = duck;
            _eventBus.OnEventFired += HandleEvent;
        }

        public void Dispose() => _eventBus.OnEventFired -= HandleEvent;

        private void HandleEvent(InstanceContext ctx, AssetEvent evt)
        {
            if (evt.Action != EventAction.Duck)
            {
                return;
            }

            if (!Enum.TryParse<DuckChannel>(evt.CustomKey, out var channel))
            {
                return;
            }

            if (evt.Trigger == EventTrigger.OnDisable || evt.Trigger == EventTrigger.OnDestroy)
            {
                _duck.Pop(channel);
            }
            else
            {
                _duck.Push(channel, evt.Param.FloatValue);
            }
        }
    }
}
