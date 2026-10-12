using System;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Ui
{
    // プリセット / カタログ → UiTweenData.Tracks の書き込み(新 UiTweenEditor の「雛形から始める」と UiPresetGallery が共有。docs/1010 UX-12)。
    // 旧 UiTweenEditorWindow の GenerateFromSelection / AppendFromSelection / TryResolveSelectionTracks と同じ規則(置き換え = 丸ごと、追加 = 末尾に足す。
    // 上限 UiTweenManager.MaxTracksPerTween を超える分は足さない)。純関数に近い形にして EditMode テストで確かめる。
    internal static class UiTweenPresetOps
    {
        // カード(組み込みプリセット or カタログ)から Track 列を作る。target はスライド系のプリセットが要素の大きさを見るのに使う
        // (null なら一時の仮要素 200×80 で作る)。作れなければ false。
        public static bool TryResolve(in GalleryCard card, RectTransform target, out TweenTrack[] tracks, out string label)
        {
            tracks = null;
            label = card.Name;
            if (card.Preset != UiPreset.None)
            {
                var scratch = target == null ? CreateScratchRect() : null;
                var rect = target != null ? target : scratch;
                try
                {
                    var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
                    var count = UiPresetFactory.Build(new UiPresetRef { Preset = card.Preset }, rect, buffer);
                    if (count <= 0)
                    {
                        return false;
                    }

                    tracks = new TweenTrack[count];
                    Array.Copy(buffer, tracks, count);
                    label = $"プリセット '{card.Preset}'";
                    return true;
                }
                finally
                {
                    if (scratch != null)
                    {
                        UnityEngine.Object.DestroyImmediate(scratch.gameObject);
                    }
                }
            }

            if (card.CatalogTween != null && card.CatalogTween.Tracks != null && card.CatalogTween.Tracks.Length > 0)
            {
                tracks = (TweenTrack[])card.CatalogTween.Tracks.Clone();
                label = $"カタログ '{card.Name}'";
                return true;
            }

            return false;
        }

        // いまの Tracks を丸ごと置き換える(Undo 1 回)。
        public static void Replace(UiTweenData data, TweenTrack[] source)
        {
            if (data == null || source == null)
            {
                return;
            }

            Undo.RecordObject(data, "UiTweenData: プリセット / カタログから Tracks を生成");
            data.Tracks = (TweenTrack[])source.Clone();
            EditorUtility.SetDirty(data);
        }

        // いまの Tracks は残して末尾に足す。足した本数を返す(上限で切られたら source.Length より少ない。0 = 足せなかった)。
        public static int Append(UiTweenData data, TweenTrack[] source)
        {
            if (data == null || source == null)
            {
                return 0;
            }

            var existing = data.Tracks ?? Array.Empty<TweenTrack>();
            var room = UiTweenManager.MaxTracksPerTween - existing.Length;
            if (room <= 0)
            {
                return 0;
            }

            var add = Mathf.Min(room, source.Length);
            Undo.RecordObject(data, "UiTweenData: プリセット / カタログの Track を追加");
            var merged = new TweenTrack[existing.Length + add];
            Array.Copy(existing, merged, existing.Length);
            Array.Copy(source, 0, merged, existing.Length, add);
            data.Tracks = merged;
            EditorUtility.SetDirty(data);
            return add;
        }

        public static RectTransform CreateScratchRect()
        {
            // TypeFillIn などが Image(Filled)を前提にするので、仮要素にも付けて「FillAmount は反映されない」警告を出さない。
            var go = new GameObject("ScratchRect", typeof(RectTransform), typeof(UnityEngine.UI.Image)) { hideFlags = HideFlags.HideAndDontSave };
            go.GetComponent<UnityEngine.UI.Image>().type = UnityEngine.UI.Image.Type.Filled;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(200f, 80f);
            return rt;
        }

        // 64 サンプルの「形」(0..1)。曲線そのものの形だけを見せる(値域は要素ごとに違うので From / To は使わない)。
        public static float[] ShapeSamples(in GalleryCard card)
        {
            DDrive.Foundation.Values.ValueDef motion;
            if (card.Preset != UiPreset.None)
            {
                var scratch = CreateScratchRect();
                try
                {
                    var buffer = new TweenTrack[UiTweenManager.MaxTracksPerTween];
                    var count = UiPresetFactory.Build(new UiPresetRef { Preset = card.Preset }, scratch, buffer);
                    motion = count > 0 ? buffer[0].Motion : default;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(scratch.gameObject);
                }
            }
            else if (card.CatalogTween != null && card.CatalogTween.Tracks != null && card.CatalogTween.Tracks.Length > 0)
            {
                motion = card.CatalogTween.Tracks[0].Motion;
            }
            else
            {
                return null;
            }

            return ShapeSamples(motion);
        }

        public static float[] ShapeSamples(in DDrive.Foundation.Values.ValueDef motion, int count = 64)
        {
            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)(count - 1);
                switch (motion.Mode)
                {
                    case DDrive.Foundation.Values.ValueMode.Parametric:
                        result[i] = motion.Parametric.Evaluate(t);
                        break;
                    case DDrive.Foundation.Values.ValueMode.Curve:
                        result[i] = motion.Curve != null ? motion.Curve.Evaluate(t) : t;
                        break;
                    default:
                        result[i] = 1f;
                        break;
                }
            }

            return result;
        }
    }
}
