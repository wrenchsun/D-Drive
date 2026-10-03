using System.Collections.Generic;
using DDrive.Runtime.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // [15_ui_interaction.md] B-4 — CanvasEditorWindow の「要素を自動収集」「一括適用」ボタンの実体(4-9)。
    // CanvasNavigationCollector と同じ「既存の行は保持し、無いものだけ追加する」方針。
    public static class CanvasElementFxCollector
    {
        // Prefab 内の Graphic(Image/Text 等)と UiInteractable(UiButton 等)のパスを ElementEffects へ追加する。
        public static ElementFx[] CollectMerged(GameObject prefab, ElementFx[] existing)
            => CollectMerged(prefab, existing, null);

        // 2026-10-03(Canvas の埋め込み): excludeRoots(登録済みの埋め込みルートのパス)の配下の要素は集めない
        // (子の CanvasData の担当)。埋め込みルート自身は集める(子の行からは指せない、親の要素のため)。
        // 既に existing にある行は(配下でも)消さず、そのまま残る(= 親での上書き)。
        public static ElementFx[] CollectMerged(GameObject prefab, ElementFx[] existing, IReadOnlyList<string> excludeRoots)
        {
            var map = new Dictionary<string, ElementFx>(System.StringComparer.Ordinal);
            var order = new List<string>();
            AddExisting(existing, map, order);

            var graphics = prefab.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                AddPathIfMissing(GetPath(prefab.transform, graphics[i].transform), map, order, excludeRoots);
            }

            var interactables = prefab.GetComponentsInChildren<UiInteractable>(true);
            for (var i = 0; i < interactables.Length; i++)
            {
                AddPathIfMissing(GetPath(prefab.transform, interactables[i].transform), map, order, excludeRoots);
            }

            return ToArray(map, order);
        }

        // Prefab 内の全 UiButton の AppearPreset を preset に設定する(行が無ければ追加する)。
        // excludeRoots: 登録済みの埋め込みルート(配下のボタンは子の CanvasData の担当なので対象外)。
        public static ElementFx[] ApplyPresetToButtons(GameObject prefab, ElementFx[] existing, UiPreset preset, IReadOnlyList<string> excludeRoots = null)
        {
            var map = new Dictionary<string, ElementFx>(System.StringComparer.Ordinal);
            var order = new List<string>();
            AddExisting(existing, map, order);

            var buttons = prefab.GetComponentsInChildren<UiButton>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var path = GetPath(prefab.transform, buttons[i].transform);
                if (IsUnderAny(excludeRoots, path))
                {
                    continue;
                }

                if (!map.TryGetValue(path, out var fx))
                {
                    fx = new ElementFx { ElementPath = path };
                    order.Add(path);
                }

                fx.AppearPreset = new UiPresetRef { Preset = preset };
                map[path] = fx;
            }

            return ToArray(map, order);
        }

        // 4-10: rows[from] の Appear/Idle/Disappear(Preset/Id とも)を、他の全行へコピーする
        // (CanvasEditorWindow「この要素の設定を他の要素へコピー」の実体。テストしやすいよう配列操作のみ切り出す)。
        public static void CopyPhases(ref ElementFx[] rows, int from)
        {
            if (rows == null || from < 0 || from >= rows.Length)
            {
                return;
            }

            var source = rows[from];
            for (var i = 0; i < rows.Length; i++)
            {
                if (i == from)
                {
                    continue;
                }

                var row = rows[i];
                row.AppearPreset = source.AppearPreset;
                row.IdlePreset = source.IdlePreset;
                row.DisappearPreset = source.DisappearPreset;
                row.Appear = source.Appear;
                row.Idle = source.Idle;
                row.Disappear = source.Disappear;
                rows[i] = row;
            }
        }

        private static void AddExisting(ElementFx[] existing, Dictionary<string, ElementFx> map, List<string> order)
        {
            if (existing == null)
            {
                return;
            }

            for (var i = 0; i < existing.Length; i++)
            {
                var path = existing[i].ElementPath ?? string.Empty;
                if (!map.ContainsKey(path))
                {
                    order.Add(path);
                }

                map[path] = existing[i];
            }
        }

        private static void AddPathIfMissing(string path, Dictionary<string, ElementFx> map, List<string> order, IReadOnlyList<string> excludeRoots = null)
        {
            if (map.ContainsKey(path) || IsUnderAny(excludeRoots, path))
            {
                return;
            }

            map[path] = new ElementFx { ElementPath = path };
            order.Add(path);
        }

        // path が excludeRoots のどれかの「配下」(ルート自身は含まない)か。
        private static bool IsUnderAny(IReadOnlyList<string> excludeRoots, string path)
        {
            if (excludeRoots == null)
            {
                return false;
            }

            for (var i = 0; i < excludeRoots.Count; i++)
            {
                if (EmbeddedPaths.TryToChildPath(excludeRoots[i], path, out var childPath) && childPath.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static ElementFx[] ToArray(Dictionary<string, ElementFx> map, List<string> order)
        {
            var result = new ElementFx[order.Count];
            for (var i = 0; i < order.Count; i++)
            {
                result[i] = map[order[i]];
            }

            return result;
        }

        // 共通ヘルパー TransformPath.GetRelative へ集約(レビュー対応 2026-09-14)。
        private static string GetPath(Transform root, Transform target) => TransformPath.GetRelative(root, target);
    }
}
