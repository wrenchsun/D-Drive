using System;
using System.Collections.Generic;
using UnityEditor;

namespace DDrive.Editor
{
    // [09_editor_tools.md] §9 — AssetDatabase.FindAssets の結果をプロジェクト変更までキャッシュする(2026-09-11)。
    //
    // 背景: Unity 6000.3 の AssetDatabase.FindAssets は 1 回ごとに走査ファイル数に比例したネイティブメモリ
    // (フォルダ指定なしで約 9.6 MB、Assets 配下で約 5 MB)を確保し、GC / UnloadUnusedAssets でも解放されない
    // (ドメインリロードで戻る)。EditorAnchorRegistry.Build が 12 型分呼び、AnchorChainEditor が SceneView の
    // ハンドル描画のたびに呼んでいたため、エディタを使うほど数百 MB 単位で増えていた。
    //
    // 対策: (1) 検索範囲を既定で Assets 配下に固定し、(2) 同じ (filter, folders) の結果をキャッシュして
    // プロジェクト変更(EditorApplication.projectChanged / OnPostprocessAllAssets)まで再検索しない。
    // アセットを作った直後に同じフレームで検索する場合(AssetCreationService 等)は Invalidate() を呼ぶ。
    //
    // ルール: Packages/com.ddrive.core 内で AssetDatabase.FindAssets を直接呼ばず、必ずこのクラス経由で呼ぶ([12_review.md] §3)。
    //
    // 2026-09-20(P-5、[42_distribution.md] §2.3/§6) — Assets/DDrive → Packages/com.ddrive.core への
    // 移設に伴い、既定の検索範囲に自分自身のパッケージパス(Tests/Editor の一時フィクスチャ等、
    // "Assets" 配下にない D-Drive 自身のアセットを含む)を追加する。他パッケージ(PackageCache 配下)は
    // 引き続き対象外(パフォーマンス上の意図的な絞り込みは維持)。
    public static class AssetSearch
    {
        public static readonly string[] Roots = ResolveDefaultRoots();

        private static string[] ResolveDefaultRoots()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AssetSearch).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath)
                && !string.Equals(packageInfo.assetPath, "Assets", System.StringComparison.Ordinal))
            {
                return new[] { "Assets", packageInfo.assetPath };
            }

            return new[] { "Assets" };
        }

        private static readonly Dictionary<string, string[]> Cache = new();

        // 統計(テスト・診断用)。
        public static int CacheEntryCount => Cache.Count;
        public static int MissCount { get; private set; }

        static AssetSearch()
        {
            EditorApplication.projectChanged += Invalidate;
        }

        public static string[] FindAssets(string filter) => FindAssets(filter, null);

        public static string[] FindAssets(string filter, string[] searchInFolders)
        {
            var folders = searchInFolders == null || searchInFolders.Length == 0 ? Roots : searchInFolders;
            var key = filter + "\n" + string.Join("|", folders);
            if (!Cache.TryGetValue(key, out var guids))
            {
                var rawGuids = AssetDatabase.FindAssets(filter, folders);
                guids = FilterOutCompatFixtures(rawGuids);
                Cache[key] = guids;
                MissCount++;
            }

            // 呼び出し側が並べ替え・書き換えしてもキャッシュを壊さないよう複製を返す(GUID 文字列の配列なので軽い)。
            return (string[])guids.Clone();
        }

        // [47_review_p_tickets_2026-09-20.md] P2-1(2026-09-20) — P-3 の互換性スナップショットの
        // 旧版フィクスチャ(Tests/Editor/Compat/Fixtures/、実 Data 型で作られている)は、除外を各呼び出し側
        // (AssetBrowser・仕様書インデックス・ID ピッカー・Addressables 同期・各種 Validator/Codegen 等)に
        // ばらばらに実装すると漏れが出る(実測 [42] §2.3-2 参照)。`AssetSearch` が唯一の
        // `AssetDatabase.FindAssets` 呼び出し口(クラス冒頭コメントのルール)であることを利用し、
        // ここ 1 箇所でフィクスチャを結果から落とす(呼び出し側の個別の除外実装は不要になる)。
        private static string[] FilterOutCompatFixtures(string[] guids)
        {
            List<string> filtered = null;

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (IsCompatFixturePath(path))
                {
                    filtered ??= new List<string>(guids[..i]);
                    continue;
                }

                filtered?.Add(guids[i]);
            }

            return filtered?.ToArray() ?? guids;
        }

        public static bool IsCompatFixturePath(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) && assetPath.Contains("/Compat/Fixtures/", StringComparison.Ordinal);

        // アセットの作成・削除・移動の直後、projectChanged が来る前に検索する必要があるときに呼ぶ。
        public static void Invalidate() => Cache.Clear();

        // インポート(CreateAsset / Refresh / 外部変更)を検知して無効化。projectChanged より早く、同期的に届く。
        private sealed class ImportWatcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Length > 0 || deleted.Length > 0 || moved.Length > 0)
                {
                    Invalidate();
                }
            }
        }
    }
}
