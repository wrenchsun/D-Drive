using System;
using System.Collections.Generic;
using DDrive.Foundation.Manager;

namespace DDrive.Foundation.Event
{
    // Manager は Instance のライフサイクル節目(Fire)と毎フレーム(Tick)を呼ぶだけ。
    // 実際の Action 実行(PlayAsset/SetParam/...)は購読側(各 Manager/Presentation)が OnEventFired で行う。
    public sealed class EventBus
    {
        private sealed class Session
        {
            public AssetEvent[] Events = Array.Empty<AssetEvent>();
            public readonly HashSet<int> FiredOnce = new();
            public float ElapsedTime;
            public int ElapsedFrame;
        }

        private readonly Dictionary<InstanceContext, Session> _sessions = new();

        public event Action<InstanceContext, AssetEvent> OnEventFired;

        // Instance の終了(End)。KeepWhilePlaying で出した SE / VFX を止めるために Dispatcher が購読する。
        public event Action<InstanceContext> OnSessionEnded;

        public void Begin(InstanceContext ctx, AssetEvent[] events)
        {
            _sessions[ctx] = new Session { Events = events ?? Array.Empty<AssetEvent>() };
        }

        public void End(InstanceContext ctx)
        {
            if (_sessions.Remove(ctx))
            {
                OnSessionEnded?.Invoke(ctx);
            }
        }

        // OnSpawn/OnEnable/OnLoop/OnDisable/OnDestroy/Custom 用。Frame/Time は Tick から発火する。
        public void Fire(InstanceContext ctx, EventTrigger trigger, string customKey = null)
        {
            if (!_sessions.TryGetValue(ctx, out var session))
            {
                return;
            }

            for (var i = 0; i < session.Events.Length; i++)
            {
                var evt = session.Events[i];
                if (evt.Trigger != trigger)
                {
                    continue;
                }

                if (trigger == EventTrigger.Custom && evt.CustomKey != customKey)
                {
                    continue;
                }

                OnEventFired?.Invoke(ctx, evt);
            }
        }

        // アニメーション用: Frame/Time トリガを「ゲームのフレーム数」ではなく「クリップ時間」で判定する
        // ([05_model_animation.md] B-3、2026-09-08)。clipTimeSeconds はクリップ先頭からの秒、Frame は frameRate で秒に換算。
        public void TickAnimation(InstanceContext ctx, float clipTimeSeconds, float frameRate)
        {
            if (!_sessions.TryGetValue(ctx, out var session))
            {
                return;
            }

            for (var i = 0; i < session.Events.Length; i++)
            {
                if (session.FiredOnce.Contains(i))
                {
                    continue;
                }

                var evt = session.Events[i];
                var crossed = evt.Trigger switch
                {
                    EventTrigger.Time => clipTimeSeconds >= evt.Time,
                    EventTrigger.Frame => frameRate > 0f && clipTimeSeconds * frameRate >= evt.Time,
                    _ => false,
                };

                if (!crossed)
                {
                    continue;
                }

                session.FiredOnce.Add(i);
                OnEventFired?.Invoke(ctx, evt);
            }
        }

        // シーク用: Frame/Time を発火せずに「clipTimeSeconds 以前のものは発火済み」に揃える(エディタのタイムライン操作)。
        public void SeekAnimation(InstanceContext ctx, float clipTimeSeconds, float frameRate)
        {
            if (!_sessions.TryGetValue(ctx, out var session))
            {
                return;
            }

            session.FiredOnce.Clear();
            for (var i = 0; i < session.Events.Length; i++)
            {
                var evt = session.Events[i];
                var crossed = evt.Trigger switch
                {
                    EventTrigger.Time => clipTimeSeconds >= evt.Time,
                    EventTrigger.Frame => frameRate > 0f && clipTimeSeconds * frameRate >= evt.Time,
                    _ => false,
                };
                if (crossed)
                {
                    session.FiredOnce.Add(i);
                }
            }
        }

        // ループ周回時に Frame/Time を再発火可能にする。Repeat=EveryLoop のものだけ戻し、Once / KeepWhilePlaying は発火済みのまま。
        public void ResetOnce(InstanceContext ctx)
        {
            if (!_sessions.TryGetValue(ctx, out var session))
            {
                return;
            }

            for (var i = 0; i < session.Events.Length; i++)
            {
                if (session.Events[i].Repeat == EventRepeat.EveryLoop)
                {
                    session.FiredOnce.Remove(i);
                }
            }
        }

        public void Tick(InstanceContext ctx, float deltaTime)
        {
            if (!_sessions.TryGetValue(ctx, out var session))
            {
                return;
            }

            session.ElapsedTime += deltaTime;
            session.ElapsedFrame++;

            for (var i = 0; i < session.Events.Length; i++)
            {
                if (session.FiredOnce.Contains(i))
                {
                    continue;
                }

                var evt = session.Events[i];
                var crossed = evt.Trigger switch
                {
                    EventTrigger.Time => session.ElapsedTime >= evt.Time,
                    EventTrigger.Frame => session.ElapsedFrame >= (int)evt.Time,
                    _ => false,
                };

                if (!crossed)
                {
                    continue;
                }

                session.FiredOnce.Add(i);
                OnEventFired?.Invoke(ctx, evt);
            }
        }
    }
}
