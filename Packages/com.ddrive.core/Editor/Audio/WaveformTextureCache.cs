using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Editor.Audio
{
    // タイムラインに重ねる小さな波形テクスチャのキャッシュ(AnimEditor の SE マーカー用。2026-09-10、ITAMI の SE タブから移植)。
    // AudioEditor の大きな波形は WaveformRenderer を直接使う。ここは「どの音がどこまで鳴るか」が分かればよい解像度。
    public static class WaveformTextureCache
    {
        private const int Width = 512;
        private const int Height = 32;
        private static readonly Dictionary<int, Texture2D> Cache = new();
        private static readonly Color Background = new(0f, 0f, 0f, 0f);
        private static readonly Color Wave = new(0.3f, 0.85f, 1f, 1f);

        // 生成できない(圧縮で GetData 不可等)場合は null をキャッシュして再試行しない。
        public static Texture2D Get(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            var key = clip.GetInstanceID();
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            Texture2D texture = null;
            try
            {
                texture = WaveformRenderer.BuildTexture(clip, Width, Height, Background, Wave);
                if (texture != null)
                {
                    texture.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            catch (System.Exception)
            {
                texture = null;
            }

            Cache[key] = texture;
            return texture;
        }

        public static void Clear()
        {
            foreach (var texture in Cache.Values)
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }

            Cache.Clear();
        }
    }
}
