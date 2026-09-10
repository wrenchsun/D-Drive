using System.IO;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // 長さ指定モード(フレーム数 / 秒数)。
    public enum ClipLengthMode
    {
        Frames,
        Seconds,
    }

    // スプライト配列から AnimationClip (.anim) を生成する。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.AnimationClipBuilder(ロジックは同一)。
    public static class AnimationClipBuilder
    {
        // Sprite 配列を順に切り替えるアニメーションクリップを生成し、ファイル保存する。
        // sprites: 切り替えるスプライト(順序保持)。saveDirectory: 保存先("Assets/..." 形式)。
        // clipName: ファイル名(拡張子不要)。frameRate: 基準フレームレート(sampleRate)。
        // lengthMode/length: 長さの単位と値。loop: ループ有効フラグ。
        public static bool Build(
            Sprite[] sprites,
            string saveDirectory,
            string clipName,
            int frameRate,
            ClipLengthMode lengthMode,
            float length,
            bool loop,
            out AnimationClip outClip)
        {
            outClip = null;

            if (sprites == null || sprites.Length == 0)
            {
                Debug.LogError("[AnimationClipBuilder] Sprite 配列が空です。");
                return false;
            }

            if (string.IsNullOrEmpty(saveDirectory))
            {
                Debug.LogError("[AnimationClipBuilder] saveDirectory が空です。");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(saveDirectory))
            {
                Debug.LogError($"[AnimationClipBuilder] 保存先ディレクトリが無効です: {saveDirectory}");
                return false;
            }

            if (string.IsNullOrEmpty(clipName))
            {
                Debug.LogError("[AnimationClipBuilder] clipName が空です。");
                return false;
            }

            if (frameRate <= 0)
            {
                Debug.LogError("[AnimationClipBuilder] frameRate は 1 以上を指定してください。");
                return false;
            }

            if (length <= 0f)
            {
                Debug.LogError("[AnimationClipBuilder] length は 0 より大きい値を指定してください。");
                return false;
            }

            var totalSeconds = lengthMode == ClipLengthMode.Frames ? length / frameRate : length;

            var clip = new AnimationClip { frameRate = frameRate };

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite",
            };

            var keyframes = BuildKeyframes(sprites, AnimationClipEditorUtility.BuildUniformTimes(sprites.Length), totalSeconds);
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var path = Path.Combine(saveDirectory, clipName + ".anim").Replace('\\', '/');
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            outClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            return outClip != null;
        }

        // 正規化時刻(0..1)を指定して構築するオーバーロード。Anim2DEditorWindow の Retiming プレビューから使う。
        public static bool BuildWithTimes(
            Sprite[] sprites,
            float[] normalizedTimes,
            string saveDirectory,
            string clipName,
            int frameRate,
            float totalSeconds,
            bool loop,
            out AnimationClip outClip)
        {
            outClip = null;
            if (sprites == null || sprites.Length == 0 || normalizedTimes == null || sprites.Length != normalizedTimes.Length)
            {
                Debug.LogError("[AnimationClipBuilder] sprites / normalizedTimes が不正です。");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(saveDirectory))
            {
                Debug.LogError($"[AnimationClipBuilder] 保存先ディレクトリが無効です: {saveDirectory}");
                return false;
            }

            var clip = new AnimationClip { frameRate = frameRate };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite",
            };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, BuildKeyframes(sprites, normalizedTimes, totalSeconds));

            var path = Path.Combine(saveDirectory, clipName + ".anim").Replace('\\', '/');
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            outClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            return outClip != null;
        }

        private static ObjectReferenceKeyframe[] BuildKeyframes(Sprite[] sprites, float[] normalizedTimes, float totalSeconds)
        {
            var keyframes = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = Mathf.Clamp01(normalizedTimes[i]) * totalSeconds,
                    value = sprites[i],
                };
            }

            return keyframes;
        }
    }
}
