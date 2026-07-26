using System;
using DDrive.Foundation.Data;

namespace DDrive.Foundation.Event
{
    public enum EventTrigger
    {
        OnSpawn,
        OnEnable,
        OnLoop,
        OnDisable,
        OnDestroy,
        Frame,
        Time,
        Custom,
    }

    public enum EventAction
    {
        PlayAsset,
        SetParam,
        SendMessage,
        Duck,
    }

    // [02_core_framework.md] §3. Trigger=Frame/Time のとき Time フィールドを対応する単位(フレーム/秒)として使う。
    [Serializable]
    public struct AssetEvent
    {
        public EventTrigger Trigger;
        public float Time;
        public string CustomKey;
        public EventAction Action;
        public AssetRef Target;
        public ParamValue Param;
    }
}
