using DDrive.Editor.Compat;
using DDrive.Runtime.Net;
using NUnit.Framework;
using UnityEngine;

namespace DDrive.Tests.Editor.Compat
{
    // [42_distribution.md] §5.6 / §5.11-6(P-3、2026-09-20) — `INetMessage` 実装型の型名(`FullName` =
    // `NgoNetBridge` の受信側型解決キー)とフィールド一覧・型のゴールデン。改名・削除・型変更は fail
    // (禁止)。新しいメッセージ型の追加・既存メッセージへのフィールド追加は許可(MINOR)。
    //
    // `NgoNetBridge` の実際の直列化は `JsonUtility.ToJson`(`INetSerializable` 相当の書き込みバイト列)
    // なので、代表的な 2 型について固定入力 → 固定 JSON 文字列もゴールデンにする(§5.11-6 の
    // 「INetSerializable の書き込みバイト列」に対応する実装がこのコードベースには無いため、実際の
    // ワイヤ形式である JsonUtility の出力で代替する)。
    public class NetMessageSnapshotTests
    {
        private const string Hint = "ネットメッセージの改名・削除・型変更は禁止([42] §5.6)。新規メッセージ追加・フィールド追加は MINOR。";

        [Test]
        public void FieldLayout_MatchesGolden()
        {
            var actual = NetMessageSnapshotBuilder.Build();
            CompatGoldenAssert.AssertMatches(CompatSnapshotPaths.NetMessages, actual, Hint);
        }

        [Test]
        public void NetPingMsg_WireFormat_MatchesFixedGolden()
        {
            var msg = new NetPingMsg { SentAtNetworkTime = 1.5 };
            var json = JsonUtility.ToJson(msg);
            Assert.AreEqual("{\"SentAtNetworkTime\":1.5}", json, Hint);
        }

        [Test]
        public void SeNetMsg_WireFormat_MatchesFixedGolden()
        {
            var msg = new SeNetMsg { SeId = 42UL, AnchorNetId = 7UL, Position = new Vector3(1f, 2f, 3f) };
            var json = JsonUtility.ToJson(msg);
            Assert.AreEqual("{\"SeId\":42,\"AnchorNetId\":7,\"Position\":{\"x\":1.0,\"y\":2.0,\"z\":3.0}}", json, Hint);
        }
    }
}
