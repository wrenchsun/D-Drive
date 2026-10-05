using System;
using System.Collections.Generic;
using DDrive.Foundation.Handle;
using DDrive.Foundation.Identity;
using DDrive.Runtime.Ui;
using UnityEngine;

namespace DDrive.Editor.CanvasTool
{
    // 2026-10-06(U-29b) — Canvas Editor の「Idle を流す」: プレハブモード(Prefab ステージ)で、編集対象の CanvasData(と
    // 埋め込まれた子)の Idle が割り当てられた全要素の Idle を流し続ける。確認用シーンのプレビュー(実 UiManager が開いた実体)では
    // 元から Idle が流れるので、これはプレハブモード用。
    //
    // ・再生の実体は新しく作らない(ADR-4): ウィンドウ専用の実 UiTweenManager に、UiManager.StartIdle と同じ形
    //   (直接指定 Id → UiTweenData、なければ UiPresetFactory.Build したトラック)で流す。Tick は呼び出し側(ウィンドウの EditorApplication.update)。
    // ・プレハブを汚さない: 流す前の値(位置・サイズ・スケール・回転・alpha・色・fillAmount)を ElementFxStateSnapshot に控え、
    //   Stop() で必ず戻す(Undo には積まない)。止める契機の管理(保存の直前・プレハブモードを閉じる・対象切り替え・Play Mode・
    //   ウィンドウを閉じる)は呼び出し側(CanvasEditorWindow)が Stop() を呼ぶ。
    // ・編集とぶつからない: SuspendFor(選択)で、選択した要素(とその祖先)の Idle だけ止めて元の値へ戻す。選択が外れたら取り直して再開する。
    public sealed class CanvasIdleFlow
    {
        // Idle が割り当てられた要素 1 件(パスは「Prefab ステージのルート」基準)。
        public readonly struct Entry
        {
            public readonly string Path;
            public readonly UiPresetRef Preset;
            public readonly AssetId<UiTweenMarker> Id;

            public Entry(string path, UiPresetRef preset, AssetId<UiTweenMarker> id)
            {
                Path = path ?? string.Empty;
                Preset = preset;
                Id = id;
            }
        }

        private sealed class Item
        {
            public RectTransform Target;
            public UiPresetRef Preset;
            public AssetId<UiTweenMarker> Id;
            public Handle<UiTweenMarker> Handle = Handle<UiTweenMarker>.Invalid;
            public bool Suspended;
        }

        private const int MaxEmbedDepth = 8; // UiManager.MaxEmbedDepth と同じ

        private readonly UiTweenManager _tweens;
        private readonly Func<ulong, UiTweenData> _findData;
        private readonly ElementFxStateSnapshot _states = new();
        private readonly List<Item> _items = new();
        private readonly List<RectTransform> _targetScratch = new();
        private readonly TweenTrack[] _trackScratch = new TweenTrack[UiTweenManager.MaxTracksPerTween];

        public CanvasIdleFlow(UiTweenManager tweens, Func<ulong, UiTweenData> findData)
        {
            _tweens = tweens;
            _findData = findData;
        }

        // 流している(または選択のため一時停止している)要素の数。
        public int Count => _items.Count;

        public bool IsActive => _items.Count > 0;

        public int SuspendedCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Suspended)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        // いま実際に動いている(一時停止中でない)要素があるか。SceneView の再描画が要るかの判定に使う。
        public bool HasRunning
        {
            get
            {
                for (var i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].Suspended && _items[i].Target != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // 要素が破棄された(プレハブの中身が作り直された等)項目があるか。あれば呼び出し側は作り直す。
        public bool HasDestroyedTargets
        {
            get
            {
                for (var i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Target == null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // ── 何を流すか(純ロジック) ──

        // root(ステージの Prefab を持つ CanvasData)の中で「有効な」ElementFx のうち、Idle が割り当てられたものを集める。
        // 有効 = 実行時(UiManager.AppendElementFx / SetupEmbeddedCanvases)と同じ規則: 先に担当した行が勝つ
        // (Open した CanvasData 自身 > 浅い入れ子の子 > 深い入れ子の子。同じ要素の行は 1 つだけ。行が空でも要素単位で勝つ)。
        // 子の行のパスは埋め込みルートのパスを前置して root 基準にする。循環・深さ超過・未解決の子は無視する。
        public static void CollectEntries(CanvasData root, CanvasEmbeddedEditing.CanvasLookup lookup, List<Entry> into)
        {
            into.Clear();
            if (root == null)
            {
                return;
            }

            var claims = new HashSet<string>(StringComparer.Ordinal);
            AddRows(root.ElementEffects, string.Empty, claims, into);

            var level = new List<(CanvasData data, string prefix, List<CanvasData> chain)> { (root, string.Empty, new List<CanvasData> { root }) };
            var next = new List<(CanvasData data, string prefix, List<CanvasData> chain)>();
            var depth = 1;
            while (level.Count > 0 && depth < MaxEmbedDepth)
            {
                next.Clear();
                foreach (var (data, prefix, chain) in level)
                {
                    var embeds = data.EmbeddedCanvases;
                    if (embeds == null)
                    {
                        continue;
                    }

                    // 実行時と同じ順序: RootPath が深い登録を先に(同じ深さは配列順)。
                    var order = new List<int>(embeds.Length);
                    for (var i = 0; i < embeds.Length; i++)
                    {
                        order.Add(i);
                    }

                    order.Sort((a, b) =>
                    {
                        var d = PathDepth(embeds[b].RootPath).CompareTo(PathDepth(embeds[a].RootPath));
                        return d != 0 ? d : a.CompareTo(b);
                    });

                    foreach (var i in order)
                    {
                        var child = lookup?.Find(embeds[i].Canvas);
                        if (child == null || chain.Contains(child) || string.IsNullOrEmpty(embeds[i].RootPath))
                        {
                            continue;
                        }

                        var childPrefix = EmbeddedPaths.Combine(prefix, embeds[i].RootPath);
                        AddRows(child.ElementEffects, childPrefix, claims, into);
                        next.Add((child, childPrefix, new List<CanvasData>(chain) { child }));
                    }
                }

                (level, next) = (next, level);
                depth++;
            }
        }

        private static void AddRows(ElementFx[] rows, string prefix, HashSet<string> claims, List<Entry> into)
        {
            for (var i = 0; rows != null && i < rows.Length; i++)
            {
                var path = EmbeddedPaths.Combine(prefix, rows[i].ElementPath ?? string.Empty);
                if (!claims.Add(path))
                {
                    continue; // 先に担当した側の行が優先
                }

                if (rows[i].Idle.IsValid || rows[i].IdlePreset.Preset != UiPreset.None)
                {
                    into.Add(new Entry(path, rows[i].IdlePreset, rows[i].Idle));
                }
            }
        }

        private static int PathDepth(string path)
        {
            var count = 0;
            for (var i = 0; !string.IsNullOrEmpty(path) && i < path.Length; i++)
            {
                if (path[i] == '/')
                {
                    count++;
                }
            }

            return count;
        }

        // 一覧の内容が変わったか(作り直しの判定用)。ステージの識別子 + 各エントリの内容。
        public static int Signature(int stageId, List<Entry> entries)
        {
            var h = new HashCode();
            h.Add(stageId);
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                h.Add(e.Path, StringComparer.Ordinal);
                h.Add((int)e.Preset.Preset);
                h.Add(e.Preset.Duration);
                h.Add(e.Preset.Distance);
                h.Add(e.Id.Value);
            }

            return h.ToHashCode();
        }

        // ── 流す / 止める ──

        // stageRoot 基準のパスで要素を探して Idle を流し始める。戻り値 = 流し始めた件数。既に流していれば先に Stop() してよい(呼び出し側の責務)。
        public int Start(Transform stageRoot, IReadOnlyList<Entry> entries)
        {
            if (stageRoot == null || entries == null || _tweens == null)
            {
                return 0;
            }

            var started = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var target = (string.IsNullOrEmpty(e.Path) ? stageRoot : stageRoot.Find(e.Path)) as RectTransform;
                if (target == null || ContainsTarget(target))
                {
                    continue;
                }

                var item = new Item { Target = target, Preset = e.Preset, Id = e.Id };
                _items.Add(item);
                if (StartItem(item))
                {
                    started++;
                }
                else
                {
                    _items.Remove(item); // 流せない指定(Tween が見つからない等)は一覧に残さない
                }
            }

            return started;
        }

        private bool ContainsTarget(RectTransform target)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Target == target)
                {
                    return true;
                }
            }

            return false;
        }

        // 再生前の値を控えてから流す。流せなかったら控えを捨てる(Items には残す = 次に再試行できる)。
        private bool StartItem(Item item)
        {
            var target = item.Target;
            if (target == null)
            {
                return false;
            }

            _states.Capture(target);
            _tweens.StopAll(target);

            var handle = Handle<UiTweenMarker>.Invalid;
            if (item.Id.IsValid)
            {
                var tween = _findData?.Invoke(item.Id.Value);
                if (tween != null)
                {
                    handle = _tweens.PlayData(tween, target);
                }
            }
            else if (item.Preset.Preset != UiPreset.None)
            {
                var count = UiPresetFactory.Build(in item.Preset, target, _trackScratch);
                if (count > 0)
                {
                    handle = _tweens.PlayTracks(_trackScratch, count, target);
                }
            }

            item.Handle = handle;
            if (handle == Handle<UiTweenMarker>.Invalid)
            {
                _states.Restore(target);
                _states.Remove(target);
                return false;
            }

            item.Suspended = false;
            return true;
        }

        private void SuspendItem(Item item)
        {
            if (item.Suspended)
            {
                return;
            }

            var target = item.Target;
            if (target != null)
            {
                _tweens?.StopAll(target);
                _states.Restore(target);
                _states.Remove(target); // 再開時にユーザーの編集後の値を取り直す
            }

            item.Handle = Handle<UiTweenMarker>.Invalid;
            item.Suspended = true;
        }

        // 選択した要素(とその祖先)の Idle だけ止めて元の値へ戻す(編集の値と Idle の値が混ざらないように)。選択が外れた要素は
        // 取り直して再開する。selection は Selection.transforms など(null / 空なら全部再開)。戻り値 = 状態が変わった件数。
        public int SuspendFor(IReadOnlyList<Transform> selection)
        {
            var changed = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.Target == null)
                {
                    continue;
                }

                var hit = false;
                for (var s = 0; selection != null && s < selection.Count; s++)
                {
                    if (selection[s] != null && (selection[s] == item.Target || selection[s].IsChildOf(item.Target)))
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit && !item.Suspended)
                {
                    SuspendItem(item);
                    changed++;
                }
                else if (!hit && item.Suspended)
                {
                    StartItem(item);
                    changed++;
                }
            }

            return changed;
        }

        // 止めて、流す前の値へ戻し、記録を捨てる。流していなければ何もしない(何度呼んでもよい)。
        public void Stop()
        {
            if (_items.Count == 0 && _states.Count == 0)
            {
                return;
            }

            if (_tweens != null)
            {
                _states.CollectTargets(_targetScratch);
                for (var i = 0; i < _targetScratch.Count; i++)
                {
                    _tweens.StopAll(_targetScratch[i]);
                }

                _targetScratch.Clear();
            }

            _states.RestoreAllAndClear();
            _items.Clear();
        }
    }
}
