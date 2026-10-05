using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.CanvasTool
{
    // Canvas Editor の ElementFx プレビュー用に、UiTweenManager が触る値(位置・サイズ・スケール・回転・
    // alpha・色・fillAmount)の「再生前の状態」を控えて、再生のたびに / 停止・終了・プレハブモードを閉じるときに
    // 元へ戻すための小さな記録帳(2026-09-29)。
    // トゥイーンは「再生開始時の現在値」を基準(SlideIn の To = 現在位置 など)に組み立てるため、Stop して
    // 途中値や Disappear の終端値が残ったまま次を再生すると基準がずれていく。実行中の同要素を止めてから
    // ここで控えた初期状態へ戻すことで、何度押しても 1 回目と同じ再生になる。
    // Undo には積まない(ユーザーの編集ではないため)。UnityEditor を使わない純粋なロジックなのでテストしやすい。
    public sealed class ElementFxStateSnapshot
    {
        private struct State
        {
            public Vector2 AnchoredPosition;
            public Vector2 SizeDelta;
            public Vector3 LocalScale;
            public Quaternion LocalRotation;
            public bool HadGroup;
            public float Alpha;
            public bool HadGraphic;
            public Color Color;
            public bool HadImage;
            public float FillAmount;
        }

        private readonly Dictionary<RectTransform, State> _states = new();

        public int Count => _states.Count;

        public bool Contains(RectTransform target) => target != null && _states.ContainsKey(target);

        // まだ控えていない要素だけ記録する(既に控えてあれば何もしない = 最初の状態が初期状態)。
        public bool Capture(RectTransform target)
        {
            if (target == null || _states.ContainsKey(target))
            {
                return false;
            }

            var state = new State
            {
                AnchoredPosition = target.anchoredPosition,
                SizeDelta = target.sizeDelta,
                LocalScale = target.localScale,
                LocalRotation = target.localRotation,
            };

            var group = target.GetComponent<CanvasGroup>();
            state.HadGroup = group != null;
            state.Alpha = group != null ? group.alpha : 1f;

            var graphic = target.GetComponent<Graphic>();
            state.HadGraphic = graphic != null;
            state.Color = graphic != null ? graphic.color : Color.white;

            var image = target.GetComponent<Image>();
            state.HadImage = image != null;
            state.FillAmount = image != null ? image.fillAmount : 1f;

            _states[target] = state;
            return true;
        }

        // 控えた状態へ戻す(記録は残す = 次の再生でも同じ初期状態に戻せる)。記録が無い / 破棄済みなら何もしない。
        public void Restore(RectTransform target)
        {
            if (target == null || !_states.TryGetValue(target, out var state))
            {
                return;
            }

            target.anchoredPosition = state.AnchoredPosition;
            target.sizeDelta = state.SizeDelta;
            target.localScale = state.LocalScale;
            target.localRotation = state.LocalRotation;

            var group = target.GetComponent<CanvasGroup>();
            if (group != null)
            {
                if (state.HadGroup)
                {
                    group.alpha = state.Alpha;
                }
                else
                {
                    // Alpha トラックの再生で UiTweenManager が足した CanvasGroup(プレハブに残さない)。
                    Object.DestroyImmediate(group);
                }
            }

            if (state.HadGraphic)
            {
                var graphic = target.GetComponent<Graphic>();
                if (graphic != null)
                {
                    graphic.color = state.Color;
                }
            }

            if (state.HadImage)
            {
                var image = target.GetComponent<Image>();
                if (image != null)
                {
                    image.fillAmount = state.FillAmount;
                }
            }
        }

        // 全要素を戻す(記録は残す)。
        public void RestoreAll()
        {
            foreach (var target in _states.Keys)
            {
                Restore(target);
            }
        }

        // 全要素を戻して記録を捨てる(プレハブステージ用。ユーザーが後から動かした値を上書きしないよう再生ごとに取り直す)。
        public void RestoreAllAndClear()
        {
            RestoreAll();
            _states.Clear();
        }

        // 1 要素だけ記録を捨てる(戻さない。Idle を流す機能が、選択した要素だけ止めて取り直すのに使う)。
        public bool Remove(RectTransform target) => target != null && _states.Remove(target);

        // 戻さずに捨てる(実体ごと破棄するとき)。
        public void Clear() => _states.Clear();

        // 記録済みの要素(破棄済みは含まない)。呼び出し側が先にトゥイーンを止めるために使う。
        public void CollectTargets(List<RectTransform> into)
        {
            into.Clear();
            foreach (var target in _states.Keys)
            {
                if (target != null)
                {
                    into.Add(target);
                }
            }
        }
    }
}
