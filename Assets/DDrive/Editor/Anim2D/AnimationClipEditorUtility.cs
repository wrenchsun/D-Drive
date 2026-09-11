using System.Collections.Generic;
using DDrive.Foundation.Values;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // Edit モードでのスプライト時間配置方法。
    public enum PlacementMode
    {
        /// <summary>等間隔に配置(既存の Build と同じ)。</summary>
        Uniform,

        /// <summary>ValueDef(Foundation の Ease / Curve)による非線形配置。</summary>
        Retiming,
    }

    // 既存 AnimationClip のスプライトキーフレームを読み書きするユーティリティ。Edit モード専用。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.AnimationClipEditorUtility。
    // [05] C-2: EasingType/EasingFunction/CubicBezierEvaluator は Foundation の EasingCore + ValueDef に置換
    // ([15_foundation_extras.md] §B-2 で EasingCore へ昇格済み)。Anim2DData.Retiming(ValueDef)をそのまま評価する。
    public static class AnimationClipEditorUtility
    {
        // ── 読み込み ──

        // AnimationClip から SpriteRenderer の m_Sprite カーブを読み込む。
        public static bool LoadSprites(AnimationClip clip, out Sprite[] sprites, out float[] times)
        {
            sprites = null;
            times = null;
            if (clip == null)
            {
                return false;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite")
                {
                    continue;
                }

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var sprList = new List<Sprite>();
                var timeList = new List<float>();
                foreach (var kf in keyframes)
                {
                    if (kf.value is Sprite s)
                    {
                        sprList.Add(s);
                        timeList.Add(kf.time);
                    }
                }

                if (sprList.Count == 0)
                {
                    return false;
                }

                sprites = sprList.ToArray();
                times = timeList.ToArray();
                return true;
            }

            return false;
        }

        // ── 書き込み ──

        // 既存クリップの m_Sprite カーブを新しいキーフレームで上書きする(Undo は呼び出し側で RecordObject 済み前提)。
        public static bool RebuildClip(
            AnimationClip clip,
            Sprite[] sprites,
            float[] normalizedTimes,
            float totalSeconds)
        {
            if (clip == null || sprites == null || normalizedTimes == null)
            {
                return false;
            }

            if (sprites.Length != normalizedTimes.Length || totalSeconds <= 0f)
            {
                return false;
            }

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite",
            };

            var kfs = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                kfs[i] = new ObjectReferenceKeyframe
                {
                    time = Mathf.Clamp01(normalizedTimes[i]) * totalSeconds,
                    value = sprites[i],
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, kfs);
            EditorUtility.SetDirty(clip);
            // AssetDatabase.SaveAssets() は呼ばない。方向 Clip まで含めると 1 回の操作で 9 回保存されるため、
            // 保存は呼び出し側(ApplyRetiming の最後)で 1 回だけ行う(2026-09-11 レビュー対応)。
            return true;
        }

        // ── 正規化時刻配列の生成 ──

        // 等間隔(Uniform)配置の正規化時刻配列。
        public static float[] BuildUniformTimes(int count)
        {
            var t = new float[count];
            for (var i = 0; i < count; i++)
            {
                t[i] = count > 0 ? (float)i / count : 0f;
            }

            return t;
        }

        // ValueDef(Ease / Curve)による正規化時刻配列。retiming.Evaluate(0..1) をそのまま各フレーム位置に使う。
        public static float[] BuildRetimingTimes(int count, ValueDef retiming)
        {
            var t = new float[count];
            for (var i = 0; i < count; i++)
            {
                var u = count > 0 ? (float)i / count : 0f;
                t[i] = Mathf.Clamp01(retiming.Evaluate(u));
            }

            return t;
        }

        // 配置モードに応じた正規化時刻配列を返す。
        public static float[] BuildTimes(int count, PlacementMode mode, ValueDef retiming)
            => mode == PlacementMode.Retiming ? BuildRetimingTimes(count, retiming) : BuildUniformTimes(count);

        // 時刻が単調増加(狭義)かどうか。Retiming カーブが定数(既定の Constant01(1))だと全フレームが同じ時刻に潰れ、
        // Clip が 1 枚のアニメになってしまうため、焼き込み前にこれで弾く(2026-09-11 レビュー対応)。
        public static bool IsStrictlyIncreasing(float[] times)
        {
            if (times == null || times.Length == 0)
            {
                return false;
            }

            for (var i = 1; i < times.Length; i++)
            {
                if (times[i] <= times[i - 1])
                {
                    return false;
                }
            }

            return true;
        }

        // 配置モードに応じた時刻配列を作り、焼き込んで良い形(狭義単調増加)かを検証する。
        // 不正なら警告 + false(何も書き換えない)。例外は投げない。
        public static bool TryBuildTimes(int count, PlacementMode mode, ValueDef retiming, out float[] times)
        {
            times = BuildTimes(count, mode, retiming);
            if (IsStrictlyIncreasing(times))
            {
                return true;
            }

            times = null;
            Debug.LogWarning(
                "[Anim2D] リタイミングのカーブが 0 → 1 へ単調増加していないため適用しませんでした。" +
                "Retiming(ValueDef)は「正規化時間 0..1 を入れて 0..1 の位置を返す」カーブです。" +
                "既定の Constant(1) のままだと全フレームが最後に潰れます。Ease(EaseInOut 等)やカーブを設定してください。");
            return false;
        }
    }
}
