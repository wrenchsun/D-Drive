using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [26_timeline.md] §4.7(6-10a) — CutsceneData.Flags.Net == NetMode.Cosmetic のときに配送するメッセージ。
    // PresentationPlayMsg/PresentationSignalMsg/PresentationCancelMsg([14_networking.md] §5、Runtime/Net/
    // PresentationMessages.cs)と同じ規約(HandleNetKey・発行者検証・レート制限)にそのまま乗せる。
    [Serializable]
    public struct CutscenePlayMsg : INetMessage
    {
        public ulong CutId;
        public ulong SelfNetId;
        public ulong TargetNetId;
        public Vector3 Position;
        public double StartNetTime; // NetworkTime 基準の開始時刻。受信側は NetworkTime - StartNetTime だけシークする
        public ushort Seed;
        public uint HandleNetKey;
    }

    // Skip() の中継用([26] §4.7)。ToTime は director.time に相当する秒。
    [Serializable]
    public struct CutsceneSeekMsg : INetMessage
    {
        public uint HandleNetKey;
        public double ToTime;
    }

    // Cancel() の中継用。
    [Serializable]
    public struct CutsceneCancelMsg : INetMessage
    {
        public uint HandleNetKey;
    }
}
