using System;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2「Maya FBX 自動生成」— インポート規約(チケット 3-7、2026-09-10)。
    // 「DCC からの Export ボタン一つ」を成立させるための対応表。Unity が FBX から生成した Material の
    // どのプロパティを MaterialCommon のどのチャンネルに載せるか、カテゴリをどう決めるか、をデザイナーが編集できる。
    // プロジェクトに無ければ組み込み既定(URP Lit / Standard の標準プロパティ名)を使う。
    [CreateAssetMenu(menuName = "D-Drive/Material/Maya Import Profile", fileName = "MayaImportProfile")]
    public sealed class MayaImportProfile : ScriptableObject
    {
        [Serializable]
        public struct PropertyChannel
        {
            [Tooltip("FBX から生成された Unity Material のテクスチャプロパティ名(例: _BaseMap)。")]
            public string Property;

            [Tooltip("載せ先の MaterialCommon チャンネル。")]
            public TextureChannel Channel;
        }

        [Tooltip("OFF ならモデルのインポート時に自動生成しない(メニューからの手動生成は可)。")]
        public bool AutoImport = true;

        [Tooltip("このパス断片を含むモデルだけを対象にする(空なら Assets/ 配下すべて。DDrive 本体と Tests は常に除外)。")]
        public string[] IncludePathContains = { "Assets/SourceAssets" };

        [Tooltip("生成する MaterialData のシェーダー。未設定なら既定の Lit。")]
        public Shader TargetShader;

        [Tooltip("カテゴリを FBX の親フォルダ名から決める(例: SourceAssets/Player/Body.fbx → Player)。OFF なら FixedCategory。")]
        public bool CategoryFromFolder = true;

        [Tooltip("CategoryFromFolder=OFF のときのカテゴリ。")]
        public string FixedCategory = "Imported";

        [Tooltip("テクスチャプロパティ → チャンネル。上から順に最初に一致したものを使う。")]
        public PropertyChannel[] PropertyChannels = DefaultPropertyChannels();

        [Tooltip("プロパティ名で決まらないテクスチャを、ファイル名の規約(TextureImportProfile: _N / _M / _E)で分類する。")]
        public bool ClassifyByTextureName = true;

        [Tooltip("再インポート時に Specific / Anims / Render 設定を保持する(Common だけ更新する)。")]
        public bool PreserveSpecificOnReimport = true;

        public static PropertyChannel[] DefaultPropertyChannels() => new[]
        {
            new PropertyChannel { Property = "_BaseMap", Channel = TextureChannel.Albedo },
            new PropertyChannel { Property = "_MainTex", Channel = TextureChannel.Albedo },
            new PropertyChannel { Property = "_BumpMap", Channel = TextureChannel.Normal },
            new PropertyChannel { Property = "_MetallicGlossMap", Channel = TextureChannel.Mask },
            new PropertyChannel { Property = "_MaskMap", Channel = TextureChannel.Mask },
            new PropertyChannel { Property = "_OcclusionMap", Channel = TextureChannel.Mask },
            new PropertyChannel { Property = "_EmissionMap", Channel = TextureChannel.Emission },
        };

        private static MayaImportProfile _builtIn;

        public static MayaImportProfile FindOrDefault()
        {
            foreach (var guid in AssetSearch.FindAssets("t:" + nameof(MayaImportProfile)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Tests/"))
                {
                    continue;
                }

                var profile = AssetDatabase.LoadAssetAtPath<MayaImportProfile>(path);
                if (profile != null)
                {
                    return profile;
                }
            }

            if (_builtIn == null)
            {
                _builtIn = CreateInstance<MayaImportProfile>();
                _builtIn.name = "MayaImportProfile (built-in default)";
                _builtIn.hideFlags = HideFlags.HideAndDontSave;
            }

            return _builtIn;
        }

        public bool AppliesTo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            if (assetPath.Contains("/Tests/") || assetPath.StartsWith("Assets/DDrive/", StringComparison.Ordinal))
            {
                return false;
            }

            if (IncludePathContains == null || IncludePathContains.Length == 0)
            {
                return true;
            }

            foreach (var fragment in IncludePathContains)
            {
                if (!string.IsNullOrEmpty(fragment) && assetPath.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        // FBX のパスからカテゴリを決める。
        public string ResolveCategory(string modelPath)
        {
            if (!CategoryFromFolder)
            {
                return FixedCategory ?? string.Empty;
            }

            var dir = System.IO.Path.GetDirectoryName(modelPath ?? string.Empty)?.Replace('\\', '/') ?? string.Empty;
            var name = System.IO.Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name) || name == "Assets" || name == "SourceAssets")
            {
                return FixedCategory ?? string.Empty;
            }

            return name;
        }

        public bool TryGetChannel(string property, out TextureChannel channel)
        {
            if (PropertyChannels != null)
            {
                for (var i = 0; i < PropertyChannels.Length; i++)
                {
                    if (PropertyChannels[i].Property == property)
                    {
                        channel = PropertyChannels[i].Channel;
                        return true;
                    }
                }
            }

            channel = TextureChannel.Other;
            return false;
        }
    }
}
