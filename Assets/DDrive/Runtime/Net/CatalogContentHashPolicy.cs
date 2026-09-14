using System.Collections.Generic;
using DDrive.Foundation.Registry;

namespace DDrive.Runtime.Net
{
    // [14_networking.md] §7(6-5) — 不一致/タイムアウト時にどうするかの決定と、差分の説明文づくり。
    // Unity API(Debug.isDebugBuild 等)に依存しない純関数のみで構成し、EditMode/PlayMode どちらの
    // テストからも(NetBridge 無しで)直接検証できるようにする。
    //
    // ユーザー決定(2026-09-15、docs/11_tasks.md P6 決定事項): 開発ビルド・エディタでは警告のみで
    // 接続を継続、リリースビルドでは切断する。判定の基準(Debug.isDebugBuild)は呼び出し側
    // (CatalogContentHashGate)が渡す — 「不一致/タイムアウトのどちらでも同じ方針を適用する」という
    // 決定自体は本クラスに置き、Unity 依存の判定材料だけを外側に残す。
    public static class CatalogContentHashPolicy
    {
        public enum Outcome
        {
            WarnAndContinue,
            Disconnect,
        }

        public static Outcome Decide(bool isDevelopmentOrEditor)
            => isDevelopmentOrEditor ? Outcome.WarnAndContinue : Outcome.Disconnect;

        // カタログ名をキーに local/remote を突き合わせ、「どちらかに無い」「Hash が違う(Entry 数を併記)」を
        // 説明文にする。実データそのものは一切出さない(要求どおり、カタログ名 + Entry 数のみ)。
        // LINQ 不使用(CLAUDE.md §0-3。本処理は接続時 1 回だけなので alloc 自体は許容されるが、
        // 既存コードの書き方に揃える)。
        public static List<string> DescribeDifferences(
            IReadOnlyList<CatalogContentHasher.CatalogHashEntry> local,
            IReadOnlyList<CatalogContentHasher.CatalogHashEntry> remote)
        {
            var result = new List<string>();
            var remoteByName = new Dictionary<string, CatalogContentHasher.CatalogHashEntry>();

            if (remote != null)
            {
                for (var i = 0; i < remote.Count; i++)
                {
                    remoteByName[remote[i].CatalogName] = remote[i];
                }
            }

            var seen = new HashSet<string>();
            if (local != null)
            {
                for (var i = 0; i < local.Count; i++)
                {
                    var l = local[i];
                    seen.Add(l.CatalogName);

                    if (!remoteByName.TryGetValue(l.CatalogName, out var r))
                    {
                        result.Add($"{l.CatalogName}: リモートに存在しません(ローカル entries={l.EntryCount})");
                    }
                    else if (r.Hash != l.Hash)
                    {
                        result.Add($"{l.CatalogName}: entries local={l.EntryCount} remote={r.EntryCount}");
                    }
                }
            }

            if (remote != null)
            {
                for (var i = 0; i < remote.Count; i++)
                {
                    var name = remote[i].CatalogName;
                    if (!seen.Contains(name))
                    {
                        result.Add($"{name}: ローカルに存在しません(リモート entries={remote[i].EntryCount})");
                    }
                }
            }

            // 2026-09-15 修正: 差分が 1 つも無いとき(= 全カタログのハッシュが一致)は空リストを返す。
            // 以前はここで「詳細不明」を足しており、ハッシュが同じでも差分ありと報告していた(PlayMode テストで判明)。
            // 「合成ハッシュは違うのにカタログ単位の差分が見つからない」ケースの説明は、不一致と判定した呼び出し側
            // (CatalogContentHashGate)が空リストを受け取ったときに付ける。
            return result;
        }
    }
}
