using System;
using DDrive.Foundation.Net;
using UnityEngine;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §5(5-8/5-9) — PresentationData.Flags.Net == NetMode.Cosmetic のときに配送する
    // メッセージ。SE/VFX の Cosmetic メッセージ(CosmeticMessages.cs)と同じ既知の制約を持つ:
    // SelfNetId/TargetNetId は将来の NGO アダプタ向けの予約フィールドで、現状は常に 0(INetBridge に
    // Transform→NetId の逆引きが無いため)。受信側は Position のみを使い、ResolveNetObject が解決できる
    // 場合(0 でない値が来た場合)だけそれを使う。
    //
    // HandleNetKey: 送信者(行為者)が 1 回だけ生成し、以後の Signal/Cancel/Late-Join スナップショットの
    // 突き合わせキーとして使う(PrefabSpawnRequestMsg.RequestKey と同じ考え方)。0 は「無効」を意味する
    // (PresentationManager.NextHandleNetKey は 0 を返さない)。
    [Serializable]
    public struct PresentationPlayMsg : INetMessage
    {
        public ulong PresId;
        public ulong SelfNetId;   // 予約(常に 0)
        public ulong TargetNetId; // 予約(常に 0)
        public Vector3 Position;
        public double StartNetTime; // NetworkTime 基準の開始時刻。受信側は NetworkTime - StartNetTime だけシークする
        public ushort Seed;         // ランダム要素(SE選択等)を決定的にするための種(§6)。5-8 時点では PresentationManager
                                     // が保持するのみで、AudioManager 側の消費は未接続(要判断、実装メモ参照)
        public uint HandleNetKey;
    }

    // SignalKey は毎回文字列を送らず 16bit FNV-1a ハッシュに畳んで送る(帯域節約。事前登録テーブル方式は
    // 見送った。同一 Presentation 内で衝突する可能性はゼロではないが、実運用の SignalKey 数は少数のため
    // 許容する。要判断は docs/28 参照)。
    [Serializable]
    public struct PresentationSignalMsg : INetMessage
    {
        public uint HandleNetKey;
        public ushort SignalKeyHash;
    }

    // Cancel() の中継用。Interruptible=false な Presentation はローカル側で既に無視されるため届かない。
    [Serializable]
    public struct PresentationCancelMsg : INetMessage
    {
        public uint HandleNetKey;
    }
}
