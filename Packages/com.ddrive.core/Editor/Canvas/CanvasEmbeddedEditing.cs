using System;
using System.Collections.Generic;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4 追記(2026-10-03、Canvas の埋め込み) — Canvas Editor の「埋め込み Canvas」まわりの
    // ロジック(候補の検出・登録・削除、ElementFx 一覧のグループ分け、選択した要素の持ち主の解決)。
    // CanvasEditorWindow が肥大化しないよう、UI を持たない部分だけをここに切り出した(EditMode で単体テストできる)。
    public static class CanvasEmbeddedEditing
    {
        // ── 子 CanvasData の引き当て ──

        // Id → CanvasData の対応表(プロジェクト内の全 CanvasData。AssetSearch のキャッシュ経由)。
        public sealed class CanvasLookup
        {
            private readonly Dictionary<ulong, CanvasData> _byId = new();
            private readonly List<CanvasData> _all = new();

            public IReadOnlyList<CanvasData> All => _all;

            public static CanvasLookup Build()
            {
                var lookup = new CanvasLookup();
                foreach (var guid in AssetSearch.FindAssets("t:" + nameof(CanvasData)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<CanvasData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset == null)
                    {
                        continue;
                    }

                    lookup._all.Add(asset);
                    if (asset.Id != 0)
                    {
                        lookup._byId.TryAdd(asset.Id, asset);
                    }
                }

                return lookup;
            }

            public static CanvasLookup From(IEnumerable<CanvasData> canvases)
            {
                var lookup = new CanvasLookup();
                foreach (var asset in canvases)
                {
                    if (asset == null)
                    {
                        continue;
                    }

                    lookup._all.Add(asset);
                    if (asset.Id != 0)
                    {
                        lookup._byId.TryAdd(asset.Id, asset);
                    }
                }

                return lookup;
            }

            public CanvasData Find(AssetId<CanvasMarker> id)
                => id.IsValid && _byId.TryGetValue(id.Value, out var data) ? data : null;

            // CanvasData.Prefab が prefab と同じものを探す(プレハブステージの assetPath から持ち主を引くのに使う)。
            public CanvasData FindByPrefabPath(string prefabAssetPath)
            {
                if (string.IsNullOrEmpty(prefabAssetPath))
                {
                    return null;
                }

                for (var i = 0; i < _all.Count; i++)
                {
                    var c = _all[i];
                    if (c != null && c.Prefab != null && AssetDatabase.GetAssetPath(c.Prefab) == prefabAssetPath)
                    {
                        return c;
                    }
                }

                return null;
            }
        }

        // ── 埋め込み候補の検出と登録 ──

        public readonly struct Candidate
        {
            public readonly string RootPath;
            public readonly CanvasData Canvas;
            public readonly bool Registered;

            public Candidate(string rootPath, CanvasData canvas, bool registered)
            {
                RootPath = rootPath;
                Canvas = canvas;
                Registered = registered;
            }
        }

        // 親 Prefab の中の入れ子 Prefab インスタンス(ルートを除く)のうち、元の Prefab が既存の CanvasData の
        // Prefab と一致するものを返す(登録済みかどうかも付ける)。Prefab アセット自身(Prefab ステージ外)を渡してよい。
        public static List<Candidate> DetectCandidates(GameObject parentPrefab, IReadOnlyList<CanvasData> allCanvases, EmbeddedCanvas[] registered)
        {
            var result = new List<Candidate>();
            if (parentPrefab == null || allCanvases == null)
            {
                return result;
            }

            var root = parentPrefab.transform;
            var transforms = parentPrefab.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == root || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                {
                    continue;
                }

                var source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                var canvas = FindCanvasForSource(allCanvases, source, original);
                if (canvas == null)
                {
                    continue;
                }

                var path = TransformPath.GetRelative(root, t);
                result.Add(new Candidate(path, canvas, IsRegistered(registered, path, canvas)));
            }

            return result;
        }

        private static CanvasData FindCanvasForSource(IReadOnlyList<CanvasData> allCanvases, GameObject source, GameObject original)
        {
            for (var i = 0; i < allCanvases.Count; i++)
            {
                var c = allCanvases[i];
                if (c == null || c.Prefab == null)
                {
                    continue;
                }

                if ((source != null && source == c.Prefab) || (original != null && original == c.Prefab))
                {
                    return c;
                }
            }

            return null;
        }

        private static bool IsRegistered(EmbeddedCanvas[] registered, string rootPath, CanvasData canvas)
        {
            if (registered == null)
            {
                return false;
            }

            for (var i = 0; i < registered.Length; i++)
            {
                if (registered[i].RootPath == rootPath && registered[i].Canvas.IsValid && registered[i].Canvas.Value == canvas.Id)
                {
                    return true;
                }
            }

            return false;
        }

        // 登録する(同じ RootPath が既にあれば子の CanvasData だけ差し替える)。戻り値: データを書き換えたか。
        public static bool Register(CanvasData parent, string rootPath, CanvasData child)
        {
            if (parent == null || child == null || string.IsNullOrEmpty(rootPath) || child == parent)
            {
                return false;
            }

            var id = new AssetId<CanvasMarker>(child.Id, AssetType.Canvas);
            var rows = parent.EmbeddedCanvases ?? Array.Empty<EmbeddedCanvas>();
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].RootPath != rootPath)
                {
                    continue;
                }

                if (rows[i].Canvas == id)
                {
                    return false;
                }

                Undo.RecordObject(parent, "Canvas: 埋め込み Canvas を更新");
                parent.EmbeddedCanvases[i] = new EmbeddedCanvas { RootPath = rootPath, Canvas = id };
                EditorUtility.SetDirty(parent);
                return true;
            }

            Undo.RecordObject(parent, "Canvas: 埋め込み Canvas を登録");
            var next = new EmbeddedCanvas[rows.Length + 1];
            Array.Copy(rows, next, rows.Length);
            next[rows.Length] = new EmbeddedCanvas { RootPath = rootPath, Canvas = id };
            parent.EmbeddedCanvases = next;
            EditorUtility.SetDirty(parent);
            return true;
        }

        // 空の行を 1 つ足す(手動の追加用。Inspector で RootPath / Canvas を埋める)。
        public static void AddEmpty(CanvasData parent)
        {
            if (parent == null)
            {
                return;
            }

            var rows = parent.EmbeddedCanvases ?? Array.Empty<EmbeddedCanvas>();
            Undo.RecordObject(parent, "Canvas: 埋め込み Canvas を追加");
            var next = new EmbeddedCanvas[rows.Length + 1];
            Array.Copy(rows, next, rows.Length);
            parent.EmbeddedCanvases = next;
            EditorUtility.SetDirty(parent);
        }

        public static bool RemoveAt(CanvasData parent, int index)
        {
            var rows = parent != null ? parent.EmbeddedCanvases : null;
            if (rows == null || index < 0 || index >= rows.Length)
            {
                return false;
            }

            Undo.RecordObject(parent, "Canvas: 埋め込み Canvas を削除");
            var next = new EmbeddedCanvas[rows.Length - 1];
            for (int src = 0, dst = 0; src < rows.Length; src++)
            {
                if (src != index)
                {
                    next[dst++] = rows[src];
                }
            }

            parent.EmbeddedCanvases = next;
            EditorUtility.SetDirty(parent);
            return true;
        }

        // 登録済みの埋め込みルートのパス一覧(RootPath が空の行は除く)。自動収集の除外に使う。
        public static List<string> RegisteredRoots(CanvasData parent)
        {
            var roots = new List<string>();
            var rows = parent != null ? parent.EmbeddedCanvases : null;
            if (rows == null)
            {
                return roots;
            }

            for (var i = 0; i < rows.Length; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].RootPath))
                {
                    roots.Add(rows[i].RootPath);
                }
            }

            return roots;
        }

        // ── 選択した Transform が属する Canvas のルート ──

        // selected が属している Canvas の「ルート」(CanvasData.Prefab のルートに当たる実体)を返す。
        //  1) プレハブステージ内ならステージのルート
        //  2) UiManager が開いた実体(確認用プレビュー)なら "[D-Drive] UI Root" / レイヤー / Canvas の 3 段目
        //  3) canvasPrefab が分かれば、その Prefab のインスタンスのルート(確認用シーンへ本配置した実体など)
        // どれでもなければ null(呼び出し側は従来どおりシーン階層の最上位を使う)。
        public static Transform FindCanvasRoot(Transform selected, GameObject canvasPrefab = null)
        {
            if (selected == null)
            {
                return null;
            }

            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null && stage.IsPartOfPrefabContents(selected.gameObject))
            {
                return stage.prefabContentsRoot.transform;
            }

            var top = selected.root;
            if (top != null && top.name == UiManager.RootName)
            {
                for (var t = selected; t != null; t = t.parent)
                {
                    if (t.parent != null && t.parent.parent == top)
                    {
                        return t;
                    }
                }

                return null;
            }

            if (canvasPrefab != null)
            {
                for (var t = selected; t != null; t = t.parent)
                {
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == canvasPrefab)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        // ── 持ち主の解決(選択した要素 → 編集対象の CanvasData) ──

        // 親(外側)から子(内側)への 1 段ぶんのつながり: Data の Prefab ルートから見た、次の CanvasData のルートの位置。
        public readonly struct Link
        {
            public readonly CanvasData Data;
            public readonly string RootPath;

            public Link(CanvasData data, string rootPath)
            {
                Data = data;
                RootPath = rootPath;
            }
        }

        public readonly struct Owner
        {
            public readonly CanvasData Data;
            public readonly string Path;               // Data のルート基準のパス(Data 自身のルートなら空文字)
            public readonly List<Link> Ancestors;      // 外側 → 内側。Data 自身は含まない(空 = view 自身が持ち主)

            public Owner(CanvasData data, string path, List<Link> ancestors)
            {
                Data = data;
                Path = path;
                Ancestors = ancestors;
            }
        }

        // view(Prefab を表示している CanvasData)のルート基準のパス viewPath にある要素を「持っている」CanvasData を返す。
        // 登録済みの埋め込みルートの配下(ルート自身を除く)なら子の CanvasData(さらに入れ子なら再帰)、そうでなければ view。
        // 循環・深さ超過・未解決の子は無視して親側に倒す(UiManager と同じ上限)。
        public static Owner ResolveOwner(CanvasData view, string viewPath, CanvasLookup lookup)
        {
            var ancestors = new List<Link>();
            var current = view;
            var path = viewPath ?? string.Empty;
            var visited = new List<CanvasData> { view };
            while (current != null && ancestors.Count < 8)
            {
                var found = false;
                var embeds = current.EmbeddedCanvases;
                if (embeds != null)
                {
                    // もっとも深い(RootPath が長い)一致を優先する。
                    var bestIndex = -1;
                    var bestChild = string.Empty;
                    for (var i = 0; i < embeds.Length; i++)
                    {
                        if (!EmbeddedCanvasPaths.TryToChildPath(embeds[i].RootPath, path, out var childPath) || childPath.Length == 0)
                        {
                            continue;
                        }

                        var child = lookup?.Find(embeds[i].Canvas);
                        if (child == null || visited.Contains(child))
                        {
                            continue;
                        }

                        if (bestIndex < 0 || embeds[i].RootPath.Length > embeds[bestIndex].RootPath.Length)
                        {
                            bestIndex = i;
                            bestChild = childPath;
                        }
                    }

                    if (bestIndex >= 0)
                    {
                        var child = lookup.Find(embeds[bestIndex].Canvas);
                        ancestors.Add(new Link(current, embeds[bestIndex].RootPath));
                        visited.Add(child);
                        current = child;
                        path = bestChild;
                        found = true;
                    }
                }

                if (!found)
                {
                    break;
                }
            }

            return new Owner(current, path, ancestors);
        }

        // ── ElementFx 一覧のグループ分け ──

        public sealed class EmbedGroup
        {
            public int EmbedIndex;          // 親の EmbeddedCanvases の index
            public string RootPath;
            public CanvasData Child;        // 解決できなければ null
            public List<int> OverrideRows = new(); // 親の ElementEffects のうち、この埋め込みの配下を指す行の index
            public List<ChildRowSummary> ChildRows = new(); // 子の ElementEffects の読み取り表示用
        }

        public readonly struct ChildRowSummary
        {
            public readonly string ElementPath;   // 子ルート基準
            public readonly string Summary;       // "Appear: FadeIn / Idle: なし ..." のような短い説明
            public readonly bool OverriddenByParent;

            public ChildRowSummary(string elementPath, string summary, bool overriddenByParent)
            {
                ElementPath = elementPath;
                Summary = summary;
                OverriddenByParent = overriddenByParent;
            }
        }

        public sealed class FxGroups
        {
            public List<int> ParentRows = new();      // 親の ElementEffects の index(埋め込み配下を指さない行)
            public List<EmbedGroup> Embeds = new();   // EmbeddedCanvases と同じ順序
        }

        // parent の ElementEffects を「親の要素」と「埋め込みごと」に分ける。filter が空でなければ ElementPath に
        // その文字列を含む行だけ(大文字小文字無視)。子の行の読み取り表示にも同じ絞り込みをかける。
        public static FxGroups BuildGroups(CanvasData parent, CanvasLookup lookup, string filter = null)
        {
            var groups = new FxGroups();
            if (parent == null)
            {
                return groups;
            }

            var embeds = parent.EmbeddedCanvases;
            if (embeds != null)
            {
                for (var i = 0; i < embeds.Length; i++)
                {
                    groups.Embeds.Add(new EmbedGroup
                    {
                        EmbedIndex = i,
                        RootPath = embeds[i].RootPath,
                        Child = lookup?.Find(embeds[i].Canvas),
                    });
                }
            }

            var rows = parent.ElementEffects;
            if (rows != null)
            {
                for (var r = 0; r < rows.Length; r++)
                {
                    var path = rows[r].ElementPath ?? string.Empty;
                    if (!MatchesFilter(path, filter))
                    {
                        continue;
                    }

                    var embedGroup = FindEmbedGroupForParentPath(groups, path);
                    if (embedGroup != null)
                    {
                        embedGroup.OverrideRows.Add(r);
                    }
                    else
                    {
                        groups.ParentRows.Add(r);
                    }
                }
            }

            for (var i = 0; i < groups.Embeds.Count; i++)
            {
                var g = groups.Embeds[i];
                if (g.Child?.ElementEffects == null)
                {
                    continue;
                }

                foreach (var fx in g.Child.ElementEffects)
                {
                    var childPath = fx.ElementPath ?? string.Empty;
                    var parentPath = EmbeddedCanvasPaths.Combine(g.RootPath, childPath);
                    if (!MatchesFilter(parentPath, filter) && !MatchesFilter(childPath, filter))
                    {
                        continue;
                    }

                    var overridden = HasParentRow(parent.ElementEffects, parentPath);
                    g.ChildRows.Add(new ChildRowSummary(childPath, Summarize(fx), overridden));
                }
            }

            return groups;
        }

        // 「親でなく子が担当する」配下を指す行はどの埋め込みか(もっとも深い RootPath の一致)。無ければ null。
        private static EmbedGroup FindEmbedGroupForParentPath(FxGroups groups, string parentPath)
        {
            EmbedGroup best = null;
            foreach (var g in groups.Embeds)
            {
                if (string.IsNullOrEmpty(g.RootPath) || g.Child == null)
                {
                    continue;
                }

                if (EmbeddedCanvasPaths.TryToChildPath(g.RootPath, parentPath, out var childPath) && childPath.Length > 0
                    && (best == null || g.RootPath.Length > best.RootPath.Length))
                {
                    best = g;
                }
            }

            return best;
        }

        private static bool HasParentRow(ElementFx[] rows, string parentPath)
        {
            if (rows == null)
            {
                return false;
            }

            for (var i = 0; i < rows.Length; i++)
            {
                if (string.Equals(rows[i].ElementPath ?? string.Empty, parentPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool MatchesFilter(string path, string filter)
            => string.IsNullOrEmpty(filter) || (path ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Summarize(ElementFx fx)
        {
            return $"Appear: {PhaseLabel(fx.AppearPreset, fx.Appear)} / Idle: {PhaseLabel(fx.IdlePreset, fx.Idle)} / Disappear: {PhaseLabel(fx.DisappearPreset, fx.Disappear)}";
        }

        private static string PhaseLabel(UiPresetRef preset, AssetId<UiTweenMarker> id)
            => id.IsValid ? "Tween" : preset.Preset != UiPreset.None ? preset.Preset.ToString() : "なし";

        // ── パスの変換(親基準 ⇔ 子基準) ──

        // ancestors(外側 → 内側)をたどって、最も内側(= 編集対象)のルートから見た elementPath を、
        // index 番目の祖先の Prefab ルート基準のパスにする(index = ancestors.Count なら編集対象自身 = 変換なし)。
        public static string ToAncestorPath(IReadOnlyList<Link> ancestors, int index, string elementPath)
        {
            var prefix = string.Empty;
            for (var i = ancestors.Count - 1; i >= index && i >= 0; i--)
            {
                prefix = EmbeddedCanvasPaths.Combine(ancestors[i].RootPath, prefix);
            }

            return EmbeddedCanvasPaths.Combine(prefix, elementPath);
        }
    }
}
