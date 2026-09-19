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

    // Frame/Time イベントの繰り返し方(2026-09-10 追加、[02] §3)。既定 = 毎周回(従来どおり)。
    public enum EventRepeat
    {
        // ループのたびに発火する(OneShot の SE / VFX 向け。従来の挙動)
        EveryLoop,
        // 再生ごとに 1 回だけ(ループしても再発火しない)
        Once,
        // 1 回だけ出して、この再生が終わる/中断されるまで維持し、終了時に止める(ダッシュの土煙などループする追従エフェクト向け)
        KeepWhilePlaying,
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

        [UnityEngine.Tooltip("繰り返し: EveryLoop=ループのたびに / Once=再生ごとに 1 回 / KeepWhilePlaying=1 回出して再生終了・中断で止める(ループする追従エフェクト向け)")]
        public EventRepeat Repeat;
    }
}
