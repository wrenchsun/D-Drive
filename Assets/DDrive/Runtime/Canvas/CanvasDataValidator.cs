using System;
using System.Collections.Generic;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Validation;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Runtime.Ui
{
    // [07_canvas_prefab.md] A-4。Prefab Missing / パス不整合 / 到達不能な Selectable / FirstSelected 未設定 /
    // OpenCanvas の Target 未設定 / SendSignal の SignalKey 未設定 / Popup で ModalBlocksInput OFF を検査する。
    public sealed class CanvasDataValidator : IValidator
    {
        public AssetType Target => AssetType.Canvas;

        public IEnumerable<ValidationResult> Validate(AssetDataBase data, ValidationContext ctx)
        {
            if (data is not CanvasData canvas)
            {
                yield break;
            }

            if (canvas.Prefab == null)
            {
                yield return ValidationResult.Error("Prefab が未設定(または Missing)です");
                yield break; // パス系の検査は Prefab 前提のため打ち切る
            }

            var root = canvas.Prefab.transform;

            foreach (var result in ValidateNavigation(canvas, root))
            {
                yield return result;
            }

            if (!string.IsNullOrEmpty(canvas.FirstSelected) && !ResolvesTo<Transform>(root, canvas.FirstSelected))
            {
                yield return ValidationResult.Error($"FirstSelected '{canvas.FirstSelected}' が Prefab 内で見つかりません");
            }

            foreach (var result in ValidateButtons(canvas, root))
            {
                yield return result;
            }

            if (canvas.Layer == UiLayer.Popup && !canvas.ModalBlocksInput)
            {
                yield return ValidationResult.Info("Layer=Popup ですが ModalBlocksInput が OFF です(背後の入力がブロックされません)");
            }
        }

        private static IEnumerable<ValidationResult> ValidateNavigation(CanvasData canvas, Transform root)
        {
            if (canvas.Navigation == null || canvas.Navigation.Length == 0)
            {
                yield break;
            }

            var reached = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < canvas.Navigation.Length; i++)
            {
                var node = canvas.Navigation[i];
                if (!ResolvesTo<Selectable>(root, node.Element))
                {
                    yield return ValidationResult.Error($"Navigation[{i}] の Element '{node.Element}' が Prefab 内の Selectable として見つかりません");
                }

                foreach (var pair in Directions(node))
                {
                    if (string.IsNullOrEmpty(pair.path))
                    {
                        continue;
                    }

                    if (!ResolvesTo<Selectable>(root, pair.path))
                    {
                        yield return ValidationResult.Error($"Navigation[{i}] の {pair.label} '{pair.path}' が Prefab 内の Selectable として見つかりません");
                    }
                    else
                    {
                        reached.Add(pair.path);
                    }
                }
            }

            var selectables = canvas.Prefab.GetComponentsInChildren<Selectable>(true);
            for (var i = 0; i < selectables.Length; i++)
            {
                var path = GetPath(root, selectables[i].transform);
                if (string.Equals(path, canvas.FirstSelected, StringComparison.Ordinal))
                {
                    continue;
                }

                if (reached.Contains(path))
                {
                    continue;
                }

                yield return ValidationResult.Warning($"Selectable '{path}' は Navigation のどこからも到達できません");
            }

            if (string.IsNullOrEmpty(canvas.FirstSelected))
            {
                yield return ValidationResult.Warning("Navigation が設定されていますが FirstSelected が未設定です");
            }
        }

        private static IEnumerable<(string label, string path)> Directions(NavNode node)
        {
            yield return ("Up", node.Up);
            yield return ("Down", node.Down);
            yield return ("Left", node.Left);
            yield return ("Right", node.Right);
        }

        private static IEnumerable<ValidationResult> ValidateButtons(CanvasData canvas, Transform root)
        {
            if (canvas.Buttons == null)
            {
                yield break;
            }

            // [15] A-4: (ButtonPath, Trigger) の重複配線を検出する。
            var seen = new HashSet<(string path, WireTrigger trigger)>();

            for (var i = 0; i < canvas.Buttons.Length; i++)
            {
                var wire = canvas.Buttons[i];
                if (!ResolvesTo<Transform>(root, wire.ButtonPath))
                {
                    yield return ValidationResult.Error($"ButtonWire[{i}] の ButtonPath '{wire.ButtonPath}' が Prefab 内で見つかりません");
                }

                if (wire.Action == UiAction.OpenCanvas && !wire.Target.IsAssigned)
                {
                    yield return ValidationResult.Error($"ButtonWire[{i}] '{wire.ButtonPath}': Action=OpenCanvas なのに Target が未設定です");
                }

                if (wire.Action == UiAction.SendSignal && string.IsNullOrEmpty(wire.SignalKey))
                {
                    yield return ValidationResult.Error($"ButtonWire[{i}] '{wire.ButtonPath}': Action=SendSignal なのに SignalKey が未設定です");
                }

                if (!seen.Add((wire.ButtonPath ?? string.Empty, wire.Trigger)))
                {
                    yield return ValidationResult.Warning($"ButtonWire[{i}] '{wire.ButtonPath}' の Trigger={wire.Trigger} が重複しています");
                }

                var button = ResolveUiButton(root, wire.ButtonPath);
                if (wire.Trigger == WireTrigger.LongPress && button != null && button.LongPressSec <= 0f)
                {
                    yield return ValidationResult.Error($"ButtonWire[{i}] '{wire.ButtonPath}': Trigger=LongPress なのに対象 UiButton の LongPressSec が 0 以下です");
                }

                if (wire.Action == UiAction.OpenCanvas && button != null && button.CooldownSec <= 0f)
                {
                    yield return ValidationResult.Warning($"ButtonWire[{i}] '{wire.ButtonPath}': CooldownSec=0 の状態で OpenCanvas に配線されています(連打で多重遷移するおそれがあります)");
                }
            }
        }

        private static UiButton ResolveUiButton(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var t = root.Find(path);
            return t != null ? t.GetComponent<UiButton>() : null;
        }

        private static bool ResolvesTo<T>(Transform root, string path) where T : Component
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var t = root.Find(path);
            if (t == null)
            {
                return false;
            }

            return typeof(T) == typeof(Transform) || t.GetComponent<T>() != null;
        }

        // root からの相対パス("/"区切り、root 自身は空文字)。
        private static string GetPath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }
    }
}
