using System.IO;
using System.Text;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Material;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // 実運用規模の確認用サンプル(docs/1010 D-10。「トゥーンの敵キャラ」= シェーダー固有 27 項目〔_Toon* 8 / _Outline* 5 / _Rim* 4 / _Glow* 3 / _Wind* 2 /
    // 単発 3 / Common を上書きする紛れ込み 2〕・Passes(ShadowCaster を無効)・Keywords(2 つ)・常時アニメ 2 件を持つ MaterialData)。
    // 指定のフォルダ(Assets/ 配下)に、固有プロパティだけを持つ検証用シェーダー(LightMode 4 パス + shader_feature 3 つ。_ToonShadeStep / _ToonShadeColor だけが見た目に効く)・
    // ベースの絵の PNG と TextureData・MaterialData 3 つ(うち 1 つが対象)・TextureData(UI 用)1 つを作る。
    // 呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。スクリーンショット・テストが使う。
    internal static class MaterialSample
    {
        public const ulong BaseId = 0xDD800000;

        public sealed class Result
        {
            public MaterialData Material;
            public MaterialData Alt;
            public TextureData Texture;
            public TextureData UiTexture;
            public Shader Shader;
            public string Folder;
            public int SpecificCount;
        }

        private const string ShaderSource = @"Shader ""Hidden/DDriveSample/ToonProps""
{
    Properties
    {
        _BaseColor(""Base Color"", Color) = (1,1,1,1)
        _BaseMap(""Base Map"", 2D) = ""white"" {}
        _Smoothness(""Smoothness"", Range(0,1)) = 0.5
        _Cull(""Cull"", Float) = 2
        _ToonRampMap(""Toon Ramp"", 2D) = ""white"" {}
        _ToonShadeColor(""Toon Shade Color"", Color) = (0.55,0.5,0.7,1)
        _ToonShadeStep(""Toon Shade Step"", Range(0,1)) = 0.5
        _ToonShadeFeather(""Toon Shade Feather"", Range(0.001,1)) = 0.05
        _ToonShade2ndColor(""Toon Shade 2nd Color"", Color) = (0.3,0.25,0.5,1)
        _ToonHighlightColor(""Toon Highlight Color"", Color) = (1,1,0.9,1)
        _ToonHighlightPower(""Toon Highlight Power"", Range(0.1,8)) = 2
        _ToonFlag(""Toon Flag"", Float) = 0
        _OutlineColor(""Outline Color"", Color) = (0.05,0.03,0.08,1)
        _OutlineWidth(""Outline Width"", Range(0,0.1)) = 0.02
        _OutlineZOffset(""Outline Z Offset"", Float) = 0
        _OutlineTex(""Outline Tex"", 2D) = ""white"" {}
        _OutlineMode(""Outline Mode"", Integer) = 1
        _RimColor(""Rim Color"", Color) = (0.8,0.9,1,1)
        _RimPower(""Rim Power"", Range(0.1,8)) = 3
        _RimThreshold(""Rim Threshold"", Range(0,1)) = 0.4
        _RimMask(""Rim Mask"", 2D) = ""white"" {}
        _GlowColor(""Glow Color"", Color) = (1,0.4,0.2,1)
        _GlowStrength(""Glow Strength"", Float) = 1.5
        _GlowSpeed(""Glow Speed"", Float) = 0.8
        _WindDir(""Wind Dir"", Vector) = (1,0,0,0)
        _WindSpeed(""Wind Speed"", Float) = 1
        _NoiseScale(""Noise Scale"", Float) = 4
        _DitherAmount(""Dither Amount"", Range(0,1)) = 0
        _FogBlend(""Fog Blend"", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { ""RenderType"" = ""Opaque"" ""RenderPipeline"" = ""UniversalPipeline"" }

        Pass
        {
            Name ""Forward""
            Tags { ""LightMode"" = ""UniversalForward"" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _TOON_RIM
            #pragma shader_feature_local _TOON_OUTLINE
            #pragma shader_feature_local _TOON_RAMP
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ToonShadeColor;
            half _ToonShadeStep;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i) : SV_Target
            {
                float s = saturate(dot(normalize(i.n), normalize(float3(0.4, 0.8, 0.5))));
                return _BaseColor * lerp(_ToonShadeColor, half4(1, 1, 1, 1), step(_ToonShadeStep, s));
            }
            ENDHLSL
        }

        Pass
        {
            Name ""ShadowCaster""
            Tags { ""LightMode"" = ""ShadowCaster"" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name ""DepthOnly""
            Tags { ""LightMode"" = ""DepthOnly"" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name ""Outline""
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            half _OutlineWidth;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            float4 vert(A i) : SV_POSITION { return TransformObjectToHClip(i.pos.xyz + i.n * _OutlineWidth); }
            half4 frag() : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
";

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }

            var shader = MakeShader(folder);
            var albedoPng = MakeTexture(folder, "T_Enemy_Albedo", new Color32(200, 90, 70, 255));
            var uiPng = MakeTexture(folder, "T_Enemy_Icon_UI", new Color32(70, 140, 200, 255));

            var albedo = New<TextureData>(BaseId + 1, "敵キャラ ベース");
            albedo.Texture = albedoPng;
            albedo.Usage = TextureUsage.Model;
            albedo.Channel = TextureChannel.Albedo;
            AssetDatabase.CreateAsset(albedo, $"{folder}/TEX_Enemy_Albedo_Sample.asset");

            var ui = New<TextureData>(BaseId + 2, "敵キャラ アイコン");
            ui.Texture = uiPng;
            ui.Usage = TextureUsage.UI;
            ui.AllowScale = true;
            AssetDatabase.CreateAsset(ui, $"{folder}/TEX_Enemy_Icon_Sample.asset");

            var mat = New<MaterialData>(BaseId + 100, "敵キャラ トゥーン");
            mat.Shader = shader;
            mat.Common = MaterialCommon.Default;
            mat.Common.Albedo = new AssetId<TextureMarker>(albedo.Id, AssetType.Texture);
            mat.Common.AlbedoTint = new Color(1f, 0.92f, 0.88f, 1f);
            mat.Common.EmissionColor = new Color(1f, 0.4f, 0.2f, 1f);
            mat.Common.EmissionIntensity = 0.6f;

            // Specific = シェーダーの固有プロパティ(既定値つき)+ Common を上書きしてしまう紛れ込み 2 件(分類チップの「共通チャンネル」「描画ステート」)。
            var specific = new System.Collections.Generic.List<ShaderParam>(MaterialSpecificResolver.Resolve(shader));
            specific.Add(new ShaderParam { Property = "_Smoothness", Value = ParamValue.Of(0.8f) });
            specific.Add(new ShaderParam { Property = "_Cull", Value = ParamValue.Of(0f) });
            mat.Specific = specific.ToArray();
            mat.RenderQueueOffset = 0;
            mat.Anims = new[]
            {
                new MaterialAnim { Property = "_GlowStrength", Channel = MaterialAnimChannel.Float, Value = ValueDef.Constant01(1f) },
                new MaterialAnim { Property = "_OutlineTex", Channel = MaterialAnimChannel.OffsetU, Value = ValueDef.Constant01(1f) },
            };
            mat.DisabledPasses = new[] { "ShadowCaster" };
            mat.EnabledKeywords = new[] { "_TOON_RIM", "_TOON_OUTLINE" };
            AssetDatabase.CreateAsset(mat, $"{folder}/MAT_EnemyToon_Sample.asset");

            // 比較・データ切替の確認用。
            var alt = New<MaterialData>(BaseId + 101, "敵キャラ トゥーン(派生)");
            alt.Shader = shader;
            alt.Common = MaterialCommon.Default;
            alt.Common.AlbedoTint = new Color(0.7f, 0.85f, 1f, 1f);
            alt.Specific = MaterialSpecificResolver.Resolve(shader).ToArray();
            AssetDatabase.CreateAsset(alt, $"{folder}/MAT_EnemyToonAlt_Sample.asset");

            var plain = New<MaterialData>(BaseId + 102, "ただの Lit");
            plain.Common = MaterialCommon.Default;
            AssetDatabase.CreateAsset(plain, $"{folder}/MAT_Plain_Sample.asset");

            DDrive.Editor.Versioning.DDriveAssetSave.SaveAllSuppressed();
            return new Result
            {
                Material = mat,
                Alt = alt,
                Texture = albedo,
                UiTexture = ui,
                Shader = shader,
                Folder = folder,
                SpecificCount = mat.Specific.Length,
            };
        }

        // 固有プロパティだけを持つ検証用シェーダーを作って取り込む。
        private static Shader MakeShader(string folder)
        {
            var path = $"{folder}/DDriveSample_ToonProps.shader";
            File.WriteAllText(path, ShaderSource, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Shader>(path);
        }

        // 単色 + 斜めの線の小さな PNG を作って取り込む。
        private static Texture2D MakeTexture(string folder, string name, Color32 color)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var line = Mathf.Abs(x - y) < 4;
                    pixels[y * size + x] = line ? new Color32(255, 255, 255, 255) : color;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            var path = $"{folder}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // 中身を作ってから CreateAsset する(CreateAsset のあとに書き換えると、取り込み直しで中身が消えることがあるため)。
        private static T New<T>(ulong id, string display) where T : AssetDataBase
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            return d;
        }
    }
}
