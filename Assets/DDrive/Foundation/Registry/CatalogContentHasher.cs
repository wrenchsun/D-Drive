using System;
using System.Collections.Generic;

namespace DDrive.Foundation.Registry
{
    // [14_networking.md] §7(6-5) — カタログの ContentHash 生成。
    //
    // 何を含めるか: docs/14 §7 は「全 Entry の ID + Data ハッシュ」とだけ定めており、Data 本体の
    // どのフィールドを含めるかまでは規定していない。Data 本体(ScriptableObject の全フィールド)を
    // リフレクションで走査してハッシュ化する案もあったが、①Unity Object 参照・浮動小数点の丸め・
    // 配列順序等が絡み、決定性を保証するコストが高い ②接続時照合の目的は「Host/Client の資産定義が
    // 食い違っていないか」の検出であり、その食い違いは通常カタログの Entry 自体(登録されている ID・
    // 種別・Address・NetMode)が変わることで表面化する(デザイナーがカタログに登録し忘れた/Address が
    // 変わった/NetMode を変えた等)。そのため CLAUDE.md 「迷ったら実装せずに聞く」を踏まえ、本チケットの
    // 指示にある既定方針「ID・種別・アドレス・NetMode 等ネット同期に効くフィールドに絞る」を採用し、
    // CatalogEntry(Id/Type/Address/Flags.Net)だけを対象にした。Data 本体の内容(調整値等)まで含めたい
    // 場合は将来 AssetDataBase.Version(6-3 で自動採番される保存カウンタ)をハッシュに混ぜる拡張が容易
    // (CatalogEntry には現状 Version が無いため、混ぜるには Entry 側の拡張が必要。要判断)。
    //
    // 生成タイミング: ビルド前処理(IPreprocessBuildWithReport)で埋め込む案も検討したが、この
    // ハッシュは CatalogEntry の構造的フィールドのみに基づき Data 本体のロードを要さないため、
    // DDriveRuntimeBootstrap.RegisterCatalogsAsync() 完了時点(Editor Play Mode でもビルド実行でも
    // 同じコードパス)で毎回同一の結果を計算できる。Host/Client が同一ビルドを実行する限り、ビルド時に
    // 別ファイル(ScriptableObject/StreamingAssets)へ事前計算・埋め込む追加のパイプラインは不要と判断し、
    // 実装しなかった(要判断: 将来 Data 本体まで含める設計に広げ、かつロードコストが大きくなった場合は
    // ビルド前処理での事前計算を検討すること)。
    //
    // 64bit FNV-1a 風の決定的ハッシュ(PresentationManager.HashSignalKey と同じ考え方の 64bit 版)。
    // 暗号学的な強度は無い(コンテンツのズレ検出が目的であり、悪意ある偽装への耐性は要求していない。
    // [14_networking.md] §7 実装メモ参照)。
    public static class CatalogContentHasher
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // 1 カタログ分の結果。ハンドシェイクで送り合い、不一致時にどのカタログが違うかを
        // 名前 + Entry 数だけで診断できるようにする(実データそのものは送らない)。
        [Serializable]
        public struct CatalogHashEntry
        {
            public string CatalogName;
            public ulong Hash;
            public int EntryCount;
        }

        // Entry 単体の寄与。ID・種別・Address・NetMode(ネット同期に効くフィールドのみ、コメント上部参照)。
        public static ulong HashEntry(in CatalogEntry entry)
        {
            unchecked
            {
                var hash = FnvOffsetBasis;
                hash = Mix(hash, entry.Id);
                hash = Mix(hash, (ulong)(int)entry.Type);
                hash = MixString(hash, entry.Address);
                hash = Mix(hash, (ulong)(int)entry.Flags.Net);
                return hash;
            }
        }

        // カタログ 1 つ分の内容ハッシュ。登録順に依存しない(各 Entry のハッシュを XOR で合成する。
        // XOR は可換・結合的なので、列挙順が変わっても・複数カタログに分けて合成しても結果は一致する)。
        public static ulong HashEntries(IReadOnlyList<CatalogEntry> entries)
        {
            var combined = 0UL;
            if (entries == null)
            {
                return combined;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                combined ^= HashEntry(entries[i]);
            }

            return combined;
        }

        public static CatalogHashEntry HashCatalog(AssetCatalog catalog)
        {
            if (catalog == null)
            {
                return default;
            }

            return new CatalogHashEntry
            {
                CatalogName = catalog.name,
                Hash = HashEntries(catalog.Entries),
                EntryCount = catalog.Entries?.Count ?? 0,
            };
        }

        // 複数カタログの結果をまとめて 1 つの ulong にする(こちらも XOR。カタログの列挙順・
        // どのカタログに分けて登録したかに依存しない)。
        public static ulong CombineCatalogHashes(IReadOnlyList<CatalogHashEntry> catalogHashes)
        {
            var combined = 0UL;
            if (catalogHashes == null)
            {
                return combined;
            }

            for (var i = 0; i < catalogHashes.Count; i++)
            {
                combined ^= catalogHashes[i].Hash;
            }

            return combined;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= FnvPrime;
                return hash;
            }
        }

        private static ulong MixString(ulong hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return hash;
            }

            unchecked
            {
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= FnvPrime;
                }
            }

            return hash;
        }
    }
}
