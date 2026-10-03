using System;
using System.Collections.Generic;
using DDrive.Editor.Import;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [51_tdrive_integration.md] §4.15(FC-14、2026-10-03) — 他パッケージ(Packages/ 配下)の ShaderConversionTable を
    // マテリアル変換ウィンドウ等に見つけさせる拡張点。
    //
    // 検索範囲を「全パッケージ」に広げず、外部が「自分のパッケージの変換表はこれ」と返す方式にした理由:
    // AssetSearch.Roots(Assets + D-Drive 自身)は ModelData の探索ほか全検索に効き、AssetDatabase.FindAssets は呼ぶたびに
    // 走査ファイル数に比例したネイティブメモリを確保する(AssetSearch クラス冒頭のコメント参照)。Packages 全体
    // (Unity 公式パッケージを含む PackageCache)を対象にすると全検索の性能とメモリが悪化するため、Roots は広げない。
    // 提供口なら外部が必要な表だけを返せるので、性能に影響せず、検索結果にも依存しない(決定的)。
    //
    // 発見: `TypeCache`(FC-5 と同じ方式)。public な非 abstract 型 + public な引数なしコンストラクタが必須。
    // `DDrive.Tests*` で始まるアセンブリの実装は除外する。インスタンスはドメインリロードまでキャッシュ。
    // GetTables() は変換表を集めるたび(ウィンドウの再読み込み時)に呼ばれる。例外は実装ごとに隔離する。
    // 互換: DDrive.Editor の弱い互換面(Editor 契約。docs/42 §5.9 / §5.14 E-22)。追加のみ。
    public interface IShaderConversionTableProvider
    {
        // 提供する変換表(null 要素・重複は無視される)。
        IEnumerable<ShaderConversionTable> GetTables();
    }

    // 変換表の集め方と優先順位(同じ元シェーダー → 先シェーダーの表が複数あるとき、先に並んだものが使われる)。
    // 優先順位(決定的): ① プロジェクト(Assets/)の表(パスの序数順)> ② 外部パッケージの提供口の表(型のフルネーム順 →
    // 返した順)> ③ D-Drive 同梱(パッケージ内)の表(パスの序数順)。Tests フォルダの表は検索結果から除外する(従来どおり)。
    public static class ShaderConversionTables
    {
        private static List<IShaderConversionTableProvider> _providers;

        // 外部提供口の発見結果を捨てる(テスト専用。テスト用ダミー提供口が static フラグで切り替わるため)。
        public static void ResetProviderCacheForTests() => _providers = null;

        public static List<ShaderConversionTable> Collect()
        {
            var project = new List<(string path, ShaderConversionTable table)>();
            var bundled = new List<(string path, ShaderConversionTable table)>();
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(ShaderConversionTable)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var table = AssetDatabase.LoadAssetAtPath<ShaderConversionTable>(path);
                if (table == null)
                {
                    continue;
                }

                (path.StartsWith("Assets/", StringComparison.Ordinal) ? project : bundled).Add((path, table));
            }

            project.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            bundled.Sort((a, b) => string.CompareOrdinal(a.path, b.path));

            var result = new List<ShaderConversionTable>(project.Count + bundled.Count);
            var seen = new HashSet<ShaderConversionTable>();
            foreach (var entry in project)
            {
                if (seen.Add(entry.table))
                {
                    result.Add(entry.table);
                }
            }

            // 外部提供口の表は ① の後ろ。プロジェクト(①)や同梱(③)と同じ実体を返してきたときは、①は先に並んだ側を優先、
            // ③ は外部が名乗ったものとして ② の位置に入れ、重複させない。
            _providers ??= ExtensionPointDiscovery.Instantiate<IShaderConversionTableProvider>();
            foreach (var provider in _providers)
            {
                try
                {
                    var tables = provider.GetTables();
                    if (tables == null)
                    {
                        continue;
                    }

                    foreach (var table in tables)
                    {
                        if (table != null && seen.Add(table))
                        {
                            result.Add(table);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            foreach (var entry in bundled)
            {
                if (seen.Add(entry.table))
                {
                    result.Add(entry.table);
                }
            }

            return result;
        }
    }
}
