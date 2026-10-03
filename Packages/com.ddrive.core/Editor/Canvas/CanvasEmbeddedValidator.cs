using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.CanvasTool
{
    // [07_canvas_prefab.md] A-4 追記(2026-10-03、Canvas の埋め込み) — EmbeddedCanvases のうち、他のアセットを引く検査。
    // 他のアセットを引かずに判定できるもの(RootPath が Prefab に無い・子が未設定・自己参照・RootPath の重複)は
    // Runtime の CanvasDataValidator 側にある。新規検査なので Warning / Info のみ(既存の重さは変えない)。
    // 子の CanvasData は ctx.AllAssets(Run All)から引き、無ければ AssetDatabase から引く
    // (Canvas Editor の個別検証は 1 件だけの ctx を渡すため)。EmbeddedCanvases が空の CanvasData では何もしない。
    public sealed class CanvasEmbeddedValidator : IValidator
    {
        public AssetType Target => AssetType.Canvas;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CanvasData canvas || canvas.EmbeddedCanvases == null || canvas.EmbeddedCanvases.Length == 0)
            {
                yield break;
            }

            var lookup = BuildLookup(ctx);
            for (var i = 0; i < canvas.EmbeddedCanvases.Length; i++)
            {
                var embed = canvas.EmbeddedCanvases[i];
                if (!embed.Canvas.IsValid)
                {
                    continue; // 未設定は CanvasDataValidator が報告する
                }

                var child = lookup.Find(embed.Canvas);
                if (child == null)
                {
                    yield return ValidationResult.Warning(
                        $"EmbeddedCanvases[{i}] '{embed.RootPath}': 子の CanvasData(Id 0x{embed.Canvas.Value:X})が見つかりません(この埋め込みは無視されます)",
                        code: "DD-CANVAS-EMBED-MISSING");
                    continue;
                }

                if (child == canvas)
                {
                    continue; // 自己参照は CanvasDataValidator が報告する
                }

                if (ReachesBack(child, canvas, lookup, 0))
                {
                    yield return ValidationResult.Warning(
                        $"EmbeddedCanvases[{i}] '{embed.RootPath}': 子 Canvas '{child.DisplayName}' が(入れ子をたどると)この Canvas を埋め込んでいます(循環。この埋め込みは無視されます)",
                        code: "DD-CANVAS-EMBED-CYCLE");
                    continue;
                }

                if (canvas.Prefab == null || string.IsNullOrEmpty(embed.RootPath))
                {
                    continue;
                }

                var rootTransform = canvas.Prefab.transform.Find(embed.RootPath);
                if (rootTransform == null)
                {
                    continue; // RootPath 不正は CanvasDataValidator が報告する
                }

                if (child.Prefab != null && !IsInstanceOf(rootTransform.gameObject, child.Prefab))
                {
                    yield return ValidationResult.Warning(
                        $"EmbeddedCanvases[{i}] '{embed.RootPath}': この場所の実体が、子 Canvas '{child.DisplayName}' の Prefab のインスタンスではありません(パスや子の CanvasData の指定違いかもしれません)",
                        code: "DD-CANVAS-EMBED-PREFAB");
                }

                foreach (var message in OverrideMessages(canvas, embed.RootPath, child))
                {
                    yield return ValidationResult.Info(
                        $"EmbeddedCanvases[{i}] '{embed.RootPath}': {message}",
                        code: "DD-CANVAS-EMBED-OVERRIDE");
                }

                // 子 Canvas が自分で埋め込んでいる場所(入れ子の入れ子)が、この親でも別の登録として重なっていないか。
                var overlapJ = FindOverlappingRegistration(canvas, i, child, lookup);
                if (overlapJ >= 0)
                {
                    yield return ValidationResult.Warning(
                        $"EmbeddedCanvases[{i}] '{embed.RootPath}': 子 Canvas '{child.DisplayName}' が自分で埋め込んでいる場所が、EmbeddedCanvases[{overlapJ}] '{canvas.EmbeddedCanvases[overlapJ].RootPath}' としても登録されています(埋め込みが重なっています)。" +
                        "同じ要素は 1 回だけ適用され、先に担当した側(浅い入れ子の側 / 内側の登録)が優先されます。どちらかの登録を外してください",
                        code: "DD-CANVAS-EMBED-NESTED-ROOT");
                }
            }
        }

        // embeds[i] の子 Canvas(child)が入れ子で埋め込む場所(embeds[i].RootPath からの連結パス)のうち、親の別の登録(j != i)の
        // 配下または同じ場所にあるものがあれば、その j を返す(無ければ -1)。深さ 8 で打ち切り。
        private static int FindOverlappingRegistration(CanvasData canvas, int i, CanvasData child, CanvasEmbeddedEditing.CanvasLookup lookup)
        {
            var visited = new HashSet<CanvasData> { canvas };
            return FindOverlap(canvas.EmbeddedCanvases, i, canvas.EmbeddedCanvases[i].RootPath, child, lookup, visited, 0);
        }

        private static int FindOverlap(EmbeddedCanvas[] parentEmbeds, int selfIndex, string prefix, CanvasData node, CanvasEmbeddedEditing.CanvasLookup lookup, HashSet<CanvasData> visited, int depth)
        {
            if (depth >= 8 || node?.EmbeddedCanvases == null || !visited.Add(node))
            {
                return -1;
            }

            foreach (var inner in node.EmbeddedCanvases)
            {
                if (string.IsNullOrEmpty(inner.RootPath))
                {
                    continue;
                }

                var joined = EmbeddedPaths.Combine(prefix, inner.RootPath);
                for (var j = 0; j < parentEmbeds.Length; j++)
                {
                    if (j != selfIndex && EmbeddedPaths.TryToChildPath(parentEmbeds[j].RootPath, joined, out _))
                    {
                        return j;
                    }
                }

                var found = FindOverlap(parentEmbeds, selfIndex, joined, lookup.Find(inner.Canvas), lookup, visited, depth + 1);
                if (found >= 0)
                {
                    return found;
                }
            }

            visited.Remove(node);
            return -1;
        }

        private static CanvasEmbeddedEditing.CanvasLookup BuildLookup(ValidationContext ctx)
        {
            var list = new List<CanvasData>();
            if (ctx?.AllAssets != null)
            {
                for (var i = 0; i < ctx.AllAssets.Count; i++)
                {
                    if (ctx.AllAssets[i] is CanvasData c)
                    {
                        list.Add(c);
                    }
                }
            }

            // ctx に含まれない子も引けるよう、プロジェクト内の CanvasData も足す(同じ Id は ctx 側が優先される)。
            var project = CanvasEmbeddedEditing.CanvasLookup.Build();
            list.AddRange(project.All);
            return CanvasEmbeddedEditing.CanvasLookup.From(list);
        }

        // from から入れ子をたどって target に着くか(深さ 8 で打ち切り。UiManager と同じ上限)。
        private static bool ReachesBack(CanvasData from, CanvasData target, CanvasEmbeddedEditing.CanvasLookup lookup, int depth)
        {
            if (depth >= 8 || from.EmbeddedCanvases == null)
            {
                return false;
            }

            for (var i = 0; i < from.EmbeddedCanvases.Length; i++)
            {
                var next = lookup.Find(from.EmbeddedCanvases[i].Canvas);
                if (next == null)
                {
                    continue;
                }

                if (next == target || ReachesBack(next, target, lookup, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInstanceOf(GameObject go, GameObject sourcePrefab)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                return false;
            }

            return PrefabUtility.GetCorrespondingObjectFromSource(go) == sourcePrefab
                   || PrefabUtility.GetCorrespondingObjectFromOriginalSource(go) == sourcePrefab;
        }

        // 親の ElementEffects / Buttons / Sliders のうち、埋め込みルート配下の、子の行と同じものを指している行がある場合の案内文。
        // 担当の単位(2026-10-03、レビュー PC-R-08): ElementFx = 要素単位 / ボタン・スライダーの配線 = (要素, トリガー)単位。
        private static IEnumerable<string> OverrideMessages(CanvasData parent, string rootPath, CanvasData child)
        {
            var seen = new HashSet<string>();
            if (child.ElementEffects != null && parent.ElementEffects != null)
            {
                foreach (var c in child.ElementEffects)
                {
                    var joined = EmbeddedPaths.Combine(rootPath, c.ElementPath);
                    foreach (var p in parent.ElementEffects)
                    {
                        if (string.Equals(p.ElementPath, joined, System.StringComparison.Ordinal) && seen.Add("fx|" + joined))
                        {
                            yield return $"親に '{joined}' の ElementFx の行があるため、子 Canvas '{child.DisplayName}' の同じ要素の ElementFx は使われません(ElementFx は要素単位)";
                        }
                    }
                }
            }

            if (child.Buttons != null && parent.Buttons != null)
            {
                foreach (var c in child.Buttons)
                {
                    var joined = EmbeddedPaths.Combine(rootPath, c.ButtonPath);
                    foreach (var p in parent.Buttons)
                    {
                        if (p.Trigger == c.Trigger && string.Equals(p.ButtonPath, joined, System.StringComparison.Ordinal) && seen.Add("btn|" + joined + "|" + c.Trigger))
                        {
                            yield return $"親に '{joined}' の {c.Trigger} 配線があるため、子 Canvas '{child.DisplayName}' の同じ要素・同じトリガーの配線は使われません(別のトリガーの配線は子の設定が使われます)";
                        }
                    }
                }
            }

            if (child.Sliders != null && parent.Sliders != null)
            {
                foreach (var c in child.Sliders)
                {
                    var joined = EmbeddedPaths.Combine(rootPath, c.ElementPath);
                    foreach (var p in parent.Sliders)
                    {
                        if (p.Trigger == c.Trigger && string.Equals(p.ElementPath, joined, System.StringComparison.Ordinal) && seen.Add("sld|" + joined + "|" + c.Trigger))
                        {
                            yield return $"親に '{joined}' の {c.Trigger} 配線(スライダー)があるため、子 Canvas '{child.DisplayName}' の同じ要素・同じトリガーの配線は使われません(別のトリガーの配線は子の設定が使われます)";
                        }
                    }
                }
            }
        }
    }
}
