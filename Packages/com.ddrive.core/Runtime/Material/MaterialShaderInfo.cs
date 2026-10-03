using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDrive.Runtime.Material
{
    // FC-11(2026-10-03): シェーダーのパス(LightMode タグ値)と宣言キーワードの列挙。
    // MaterialDataValidator と Material Editor の「Passes / Keywords」欄が共有する(どちらも静的検査 / 編集時の用途で、定常経路では呼ばない)。
    public static class MaterialShaderInfo
    {
        // LightMode タグを書いていないパスの扱い。URP は SRPDefaultUnlit として描き、Material.SetShaderPassEnabled("SRPDefaultUnlit", false)
        // で止められる(2026-10-03 実描画で確認、FC-R-06)。T-Drive の輪郭線パスなどがこれに当たる。
        private const string UntaggedPassLightMode = "SRPDefaultUnlit";

        // シェーダーの全パスの LightMode タグ値(重複は除く)を result に足す。LightMode の無いパスは SRPDefaultUnlit として数える。
        public static void CollectLightModes(Shader shader, List<string> result)
        {
            if (shader == null || result == null)
            {
                return;
            }

            // Shader には passCount が無いので、一時 Material で数える(DontSave。すぐ破棄)。
            var temp = new UnityEngine.Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var tag = new ShaderTagId("LightMode");
                var count = temp.passCount;
                for (var i = 0; i < count; i++)
                {
                    var value = shader.FindPassTagValue(i, tag);
                    var name = value.name;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = UntaggedPassLightMode;
                    }

                    if (!ContainsIgnoreCase(result, name))
                    {
                        result.Add(name);
                    }
                }
            }
            finally
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(temp);
                }
                else
                {
                    Object.DestroyImmediate(temp);
                }
            }
        }

        // LightMode 名は大文字小文字を区別しない(Unity は組み込みの値を大文字で返すことがある。例: ShadowCaster → SHADOWCASTER。
        // Material.SetShaderPassEnabled も区別しない)ので、比較はこれで行う。
        private static bool ContainsIgnoreCase(List<string> list, string name)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // シェーダーが宣言しているキーワード(shader_feature / multi_compile のローカルキーワード)を result に足す。
        // グローバルキーワードは列挙されないので、「列挙に無い = 未宣言」とは限らない。
        public static void CollectKeywords(Shader shader, List<string> result)
        {
            if (shader == null || result == null)
            {
                return;
            }

            var space = shader.keywordSpace;
            var keywords = space.keywords; // アクセスのたびに配列が作られるので 1 回だけ取る(FC-R-11)
            for (var i = 0; i < space.keywordCount; i++)
            {
                var name = keywords[i].name;
                if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                {
                    result.Add(name);
                }
            }
        }
    }
}
