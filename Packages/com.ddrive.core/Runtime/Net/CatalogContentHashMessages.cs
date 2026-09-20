using System;
using DDrive.Foundation.Net;
using DDrive.Foundation.Registry;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §7(6-5) — カタログ ContentHash の接続時照合。
    //
    // Client が接続したら自分のハッシュを Broadcast する(Host 権威: 比較・不一致時の警告/切断判断は
    // 必ず Host 側で行う。[14] §2 の「クライアント発は Server 宛に依頼し Host が検証」と同じ非対称設計)。
    // Host は自分の値と比較するだけでよく、自分のハッシュを送り返す必要は無いため Host→全員への
    // 対になる Msg は用意していない(CatalogContentHashResultMsg で該当 Client にだけ結果を返す)。
    [Serializable]
    public struct CatalogContentHashMsg : INetMessage
    {
        public ulong CombinedHash;

        // 不一致時にどのカタログが違うかを名前 + Entry 数だけで診断できるようにする(実データは含めない)。
        public CatalogContentHasher.CatalogHashEntry[] Catalogs;

        // [42_distribution.md] §5.6/§6 P-8(2026-09-20) — Host/Client の D-Drive 版照合。フィールド追加のみ
        // (JsonUtility は未知/欠落フィールドに寛容なので旧版と混在しても落ちない。旧版 Client はここが
        // 既定値(PackageVersion=null, ProtocolVersion=0)のまま届く)。
        //
        // ProtocolVersion は ContentHash の照合より先に見る(CatalogContentHashGate.ProcessHostSide)。
        // 不一致(旧版含む)は「D-Drive の版が違う」として ContentHashPolicy と同じ方針(開発は警告継続・
        // リリースは切断)を適用する。PackageVersion は表示専用(NetDebugOverlay)で照合には使わない
        // (MINOR/PATCH の版差は互換なので、揃える必要があるのは ProtocolVersion だけ)。
        public string PackageVersion;
        public int ProtocolVersion;
    }

    // Host → 該当 Client への結果通知(SendTo)。一致時も送る(Client 側の NetDebugOverlay/ログが
    // 「検証中」のまま止まらないようにするため)。
    [Serializable]
    public struct CatalogContentHashResultMsg : INetMessage
    {
        public bool Matched;

        // Matched=false のときだけ意味を持つ(カタログ名 + 件数の差分説明。CatalogContentHashPolicy.DescribeDifferences)。
        public string[] Descriptions;
    }
}
