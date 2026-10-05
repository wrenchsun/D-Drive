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
        // ── 入力中の判定 ──

        // フォーカス中の要素が「文字や数値を入力する欄」か。Canvas Editor が、入力の途中で編集対象を切り替えないために使う。
        // Unity 6 では、フォーカス中の要素として返るのは欄そのもの(TextField / 数値欄 / 検索欄)で、内側の入力部品ではない。
        // 欄そのもの・内側の入力部品のどちらが返っても判定できるよう、要素とその祖先のクラス名で見る。
        // ボタンなど、入力欄でないものは対象外(ボタンも文字を持つ要素なので、型だけでは見分けない)。
        public static bool IsTextInputElement(UnityEngine.UIElements.VisualElement focused)
        {
            for (var e = focused; e != null; e = e.parent)
            {
                if (e.ClassListContains("unity-base-text-field")
                    || e.ClassListContains("unity-base-text-field__input")
                    || e.ClassListContains("unity-search-field-base"))
                {
                    return true;
                }
            }

            return false;
        }
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

            // 重なる登録を提案しない(2026-10-03、レビュー PC-R-04): 他の候補・登録済みの埋め込みルートの配下にある入れ子 Prefab
            // (= 子 Canvas の Prefab の中にある、入れ子の入れ子)は、その子 Canvas 自身の Canvas Editor で登録するもの。
            // 親でも登録すると同じ要素に子の設定が重なる。手で追加する(+ 手動で追加)ことは妨げない。
            var snapshot = new List<Candidate>(result);
            result.RemoveAll(c => IsInsideOtherRoot(c.RootPath, snapshot, registered));
            return result;
        }

        private static bool IsInsideOtherRoot(string path, List<Candidate> candidates, EmbeddedCanvas[] registered)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].RootPath != path && EmbeddedPaths.TryToChildPath(candidates[i].RootPath, path, out var child) && child.Length > 0)
                {
                    return true;
                }
            }

            if (registered != null)
            {
                for (var i = 0; i < registered.Length; i++)
                {
                    if (registered[i].RootPath != path && EmbeddedPaths.TryToChildPath(registered[i].RootPath, path, out var child) && child.Length > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
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

        // ── 登録時の「親での上書き」の整理(2026-10-06、U-29a) ──
        //
        // 登録の前に親で「要素を自動収集」を押すと、入れ子の中の要素も親の ElementFx の行として集まる。登録したあとそれらの行は
        // 「親での上書き」として残り、子の CanvasData の設定より優先される(規則 A)。中身が空の行でも要素単位で上書きになるため、
        // 子の演出が黙って効かなくなる。登録するとき(と「上書きをまとめて整理」)に、埋め込みルートの配下(ルート自身を除く)の行を整理する:
        //  ・既定のままの行 = 自動収集されただけの行 → 確認なしで取り除く
        //  ・設定が入っている行 → 1 回だけ確認(取り除く / 残す / キャンセル)
        // 親の Buttons / Sliders の配線は自動収集されないので削除せず、件数だけ知らせる。

        // 「中身が既定のまま」の ElementFx か。ElementPath 以外の全欄(Appear / Idle / Disappear の直接指定 Id・プリセット
        // = 種類 / Duration / Distance / EaseOverride / Se、AppearDelay、AppearSe、DisappearSe)が `default` のとき true。
        // ElementFx に欄が増えたら IsDefaultFx のテスト(全欄の網羅)が落ちるようにしてある。
        public static bool IsDefaultFx(in ElementFx fx)
            => !fx.Appear.IsValid && !fx.Idle.IsValid && !fx.Disappear.IsValid
               && IsDefaultPreset(in fx.AppearPreset) && IsDefaultPreset(in fx.IdlePreset) && IsDefaultPreset(in fx.DisappearPreset)
               && fx.AppearDelay == 0f
               && !fx.AppearSe.IsValid && !fx.DisappearSe.IsValid;

        private static bool IsDefaultPreset(in UiPresetRef p)
            => p.Preset == UiPreset.None && p.Duration == 0f && p.Distance == 0f && !p.Se.IsValid
               && p.EaseOverride.Kind == default && p.EaseOverride.Ease == default
               && p.EaseOverride.BezierP1 == Vector2.zero && p.EaseOverride.BezierP2 == Vector2.zero;

        // 埋め込みルート rootPath の配下(ルート自身は含まない)を指す親の ElementEffects の行を、既定のまま / 設定ありに分けて返す。
        public sealed class OverridePlan
        {
            public string RootPath = string.Empty;
            public readonly List<int> DefaultRows = new();   // 既定のまま(取り除いても子の設定が効くだけ)
            public readonly List<int> CustomRows = new();    // 設定が入っている(取り除くと親での指定が失われる)
            public int ButtonWires;                          // 配下を指す親の Buttons の配線数(削除しない)
            public int SliderWires;                          // 同 Sliders

            public int Total => DefaultRows.Count + CustomRows.Count;
            public bool IsEmpty => Total == 0 && ButtonWires == 0 && SliderWires == 0;
        }

        public static OverridePlan PlanOverrides(CanvasData parent, string rootPath)
        {
            var plan = new OverridePlan { RootPath = rootPath ?? string.Empty };
            if (parent == null || string.IsNullOrEmpty(rootPath))
            {
                return plan;
            }

            var rows = parent.ElementEffects;
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                if (!EmbeddedPaths.TryToChildPath(rootPath, rows[i].ElementPath ?? string.Empty, out var child) || child.Length == 0)
                {
                    continue;
                }

                (IsDefaultFx(in rows[i]) ? plan.DefaultRows : plan.CustomRows).Add(i);
            }

            var buttons = parent.Buttons;
            for (var i = 0; buttons != null && i < buttons.Length; i++)
            {
                if (EmbeddedPaths.TryToChildPath(rootPath, buttons[i].ButtonPath ?? string.Empty, out var child) && child.Length > 0)
                {
                    plan.ButtonWires++;
                }
            }

            var sliders = parent.Sliders;
            for (var i = 0; sliders != null && i < sliders.Length; i++)
            {
                if (EmbeddedPaths.TryToChildPath(rootPath, sliders[i].ElementPath ?? string.Empty, out var child) && child.Length > 0)
                {
                    plan.SliderWires++;
                }
            }

            return plan;
        }

        // rows から indices(昇順でなくてよい)の行を除いた配列(順序は保つ)。
        public static ElementFx[] WithoutRows(ElementFx[] rows, IReadOnlyCollection<int> indices)
        {
            if (rows == null)
            {
                return Array.Empty<ElementFx>();
            }

            var remove = new HashSet<int>(indices);
            var next = new List<ElementFx>(rows.Length);
            for (var i = 0; i < rows.Length; i++)
            {
                if (!remove.Contains(i))
                {
                    next.Add(rows[i]);
                }
            }

            return next.ToArray();
        }

        public enum OverrideChoice
        {
            Remove,   // 設定のある行も取り除く(子の CanvasData の設定を使う)
            Keep,     // 設定のある行は親での上書きとして残す(既定のままの行だけ取り除く)
            Cancel,   // 何もしない(登録もしない)
        }

        // テスト用の差し替え口。null なら実ダイアログ(EditorUtility.DisplayDialogComplex)を出す。
        // public(テスト asmdef から差し替えるため)。テストは使い終わったら必ず null に戻すこと。引数 = (ダイアログの題, 本文)。
        public static Func<string, string, OverrideChoice> ConfirmOverrideCleanupForTests;

        private const int MaxListedRows = 5;

        public static string BuildConfirmMessage(CanvasData parent, OverridePlan plan)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"埋め込み '{plan.RootPath}' の配下に、親の ElementFx の行が設定付きで {plan.CustomRows.Count} 件あります。\n");
            sb.Append("親の行は子の CanvasData の同じ要素の設定より優先されるため、残すと子の演出は効きません。\n\n");
            var rows = parent.ElementEffects;
            for (var i = 0; i < plan.CustomRows.Count && i < MaxListedRows; i++)
            {
                sb.Append("・").Append(rows[plan.CustomRows[i]].ElementPath).Append('\n');
            }

            if (plan.CustomRows.Count > MaxListedRows)
            {
                sb.Append($"・ほか {plan.CustomRows.Count - MaxListedRows} 件\n");
            }

            if (plan.DefaultRows.Count > 0)
            {
                sb.Append($"\n(中身が既定のままの行 {plan.DefaultRows.Count} 件は、確認なしで取り除きます)\n");
            }

            sb.Append("\n「取り除く」: 設定のある行も取り除き、子の CanvasData の設定を使います。\n");
            sb.Append("「残す」: 設定のある行は親での上書きとして残します。\n");
            sb.Append("「キャンセル」: 何も変更しません。");
            return sb.ToString();
        }

        // 設定のある行があれば 1 回だけ確認する(無ければ確認なしで Remove)。
        public static OverrideChoice ConfirmOverrides(CanvasData parent, OverridePlan plan, string title)
        {
            if (plan.CustomRows.Count == 0)
            {
                return OverrideChoice.Remove;
            }

            var message = BuildConfirmMessage(parent, plan);
            if (ConfirmOverrideCleanupForTests != null)
            {
                return ConfirmOverrideCleanupForTests(title, message);
            }

            // DisplayDialogComplex の戻り値: 0 = ok、1 = cancel、2 = alt(Esc は 1)。
            switch (EditorUtility.DisplayDialogComplex(title, message,
                "取り除く(子の CanvasData の設定を使う)", "キャンセル(登録しない)", "残す(親での上書きとして残る)"))
            {
                case 0: return OverrideChoice.Remove;
                case 2: return OverrideChoice.Keep;
                default: return OverrideChoice.Cancel;
            }
        }

        public readonly struct CleanupResult
        {
            public readonly bool Cancelled;
            public readonly int RemovedDefault;
            public readonly int RemovedCustom;
            public readonly int KeptCustom;
            public readonly int ButtonWires;
            public readonly int SliderWires;

            public CleanupResult(bool cancelled, int removedDefault, int removedCustom, int keptCustom, int buttonWires, int sliderWires)
            {
                Cancelled = cancelled;
                RemovedDefault = removedDefault;
                RemovedCustom = removedCustom;
                KeptCustom = keptCustom;
                ButtonWires = buttonWires;
                SliderWires = sliderWires;
            }

            public int Removed => RemovedDefault + RemovedCustom;

            // ウィンドウのステータスに出す 1 行(何も整理していなければ空文字)。
            public string Describe()
            {
                var parts = new List<string>();
                if (Removed > 0)
                {
                    parts.Add($"親の ElementFx の行を {Removed} 件取り除きました(子の CanvasData の設定を使います)");
                }

                if (KeptCustom > 0)
                {
                    parts.Add($"設定のある行 {KeptCustom} 件は親での上書きとして残しています");
                }

                if (ButtonWires + SliderWires > 0)
                {
                    parts.Add($"親の配線(Buttons {ButtonWires} 件 / Sliders {SliderWires} 件)がこの配下を指しています(削除していません)");
                }

                return string.Join("。", parts);
            }
        }

        // 登録(または「まとめて整理」)の本体。plan と選択済みの choice から、親の ElementEffects を書き換える
        // (Undo.RecordObject + SetDirty。呼び出し側が作った Undo グループの中で呼ぶ)。
        public static CleanupResult ApplyCleanup(CanvasData parent, OverridePlan plan, OverrideChoice choice)
        {
            if (choice == OverrideChoice.Cancel)
            {
                return new CleanupResult(true, 0, 0, plan.CustomRows.Count, plan.ButtonWires, plan.SliderWires);
            }

            var remove = new List<int>(plan.DefaultRows);
            var removedCustom = 0;
            if (choice == OverrideChoice.Remove)
            {
                remove.AddRange(plan.CustomRows);
                removedCustom = plan.CustomRows.Count;
            }

            if (remove.Count > 0)
            {
                Undo.RecordObject(parent, "Canvas: 親での上書きを整理");
                parent.ElementEffects = WithoutRows(parent.ElementEffects, remove);
                EditorUtility.SetDirty(parent);
            }

            return new CleanupResult(false, plan.DefaultRows.Count, removedCustom,
                choice == OverrideChoice.Keep ? plan.CustomRows.Count : 0, plan.ButtonWires, plan.SliderWires);
        }

        // 登録済み(または登録しようとしている)埋め込みの「親での上書き」を整理する(確認 → 適用)。Undo グループ 1 つ。
        // child が解決できないときは何もしない(子の設定が無いので上書きにならない)。
        public static CleanupResult CleanUpOverrides(CanvasData parent, string rootPath, CanvasData child, string undoName = "Canvas: 親での上書きを整理")
        {
            if (parent == null || child == null || string.IsNullOrEmpty(rootPath))
            {
                return default;
            }

            var plan = PlanOverrides(parent, rootPath);
            if (plan.IsEmpty)
            {
                return default;
            }

            var choice = ConfirmOverrides(parent, plan, "親での上書きの整理");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            var group = Undo.GetCurrentGroup();
            var result = ApplyCleanup(parent, plan, choice);
            Undo.CollapseUndoOperations(group);
            return result;
        }

        // 「埋め込みとして登録」+ 配下の行の整理を 1 つの Undo グループで行う(Ctrl+Z 1 回で登録前に戻る)。
        // 設定のある行の確認でキャンセルしたら、登録もしない(Cancelled = true)。
        public static bool RegisterWithCleanup(CanvasData parent, string rootPath, CanvasData child, out CleanupResult cleanup)
        {
            cleanup = default;
            if (parent == null || child == null || string.IsNullOrEmpty(rootPath) || child == parent)
            {
                return false;
            }

            var plan = PlanOverrides(parent, rootPath);
            var choice = ConfirmOverrides(parent, plan, "埋め込みとして登録");
            if (choice == OverrideChoice.Cancel)
            {
                cleanup = ApplyCleanup(parent, plan, choice);
                return false;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Canvas: 埋め込み Canvas を登録");
            var group = Undo.GetCurrentGroup();
            var registered = Register(parent, rootPath, child);
            cleanup = ApplyCleanup(parent, plan, choice);
            Undo.CollapseUndoOperations(group);
            return registered;
        }

        // 登録済みの行 index の RootPath / 子 CanvasData を書き換える(欄の変更)+ 新しい配下の行の整理を 1 つの Undo グループで。
        // newChild が null なら子の指定を外す(整理なし)。戻り値: 書き換えたか(キャンセルや範囲外は false。cleanup.Cancelled で区別)。
        public static bool ChangeEmbedWithCleanup(CanvasData parent, int index, string newRootPath, CanvasData newChild, out CleanupResult cleanup)
        {
            cleanup = default;
            var rows = parent != null ? parent.EmbeddedCanvases : null;
            if (rows == null || index < 0 || index >= rows.Length)
            {
                return false;
            }

            OverridePlan plan = null;
            var choice = OverrideChoice.Remove;
            if (newChild != null && newChild != parent && !string.IsNullOrEmpty(newRootPath))
            {
                plan = PlanOverrides(parent, newRootPath);
                choice = ConfirmOverrides(parent, plan, "埋め込み Canvas の変更");
                if (choice == OverrideChoice.Cancel)
                {
                    cleanup = ApplyCleanup(parent, plan, choice);
                    return false;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Canvas: 埋め込み Canvas を変更");
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(parent, "Canvas: 埋め込み Canvas を変更");
            parent.EmbeddedCanvases[index] = new EmbeddedCanvas
            {
                RootPath = newRootPath ?? string.Empty,
                Canvas = newChild != null ? new AssetId<CanvasMarker>(newChild.Id, AssetType.Canvas) : default,
            };
            EditorUtility.SetDirty(parent);
            if (plan != null)
            {
                cleanup = ApplyCleanup(parent, plan, choice);
            }

            Undo.CollapseUndoOperations(group);
            return true;
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
                        if (!EmbeddedPaths.TryToChildPath(embeds[i].RootPath, path, out var childPath) || childPath.Length == 0)
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
                    var parentPath = EmbeddedPaths.Combine(g.RootPath, childPath);
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

                if (EmbeddedPaths.TryToChildPath(g.RootPath, parentPath, out var childPath) && childPath.Length > 0
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
                prefix = EmbeddedPaths.Combine(ancestors[i].RootPath, prefix);
            }

            return EmbeddedPaths.Combine(prefix, elementPath);
        }
    }
}
