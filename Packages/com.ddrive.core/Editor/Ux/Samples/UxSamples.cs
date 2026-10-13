using System;
using DDrive.Editor.AssetBrowser;
using DDrive.Editor.CameraFx;
using DDrive.Editor.Presentation;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Anchoring;
using DDrive.Runtime.Anim;
using DDrive.Runtime.Audio;
using DDrive.Runtime.CameraShake;
using DDrive.Runtime.Haptics;
using DDrive.Runtime.Material;
using DDrive.Runtime.Model;
using DDrive.Runtime.Presentation;
using DDrive.Runtime.Prefab;
using DDrive.Runtime.Ui;
using DDrive.Runtime.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DDrive.Editor.Ux
{
    // 各新エディターの空状態「サンプルを作る」(docs/1010 UX-19)。Assets/GameData/<種別>/Sample/ に実データとして作る
    // (AssetCreationService 経由 = カタログ・Addressables 登録、カテゴリ Sample)。同じ表示名が既にあれば作らずそれを返す。
    // 素材は持ち込み先に依存しないよう、パッケージ同梱の vfx_sample.prefab か、その場で生成した小さな素材(音・画像・Prefab・クリップ)を使う。
    // (実運用規模の確認用サンプル = *Sample.Build(folder) はテスト・撮影用で、実データには作らない。)
    internal static class UxSamples
    {
        public const string Category = "Sample";
        private const string Root = "Assets/GameData";

        // Vfx / Presentation は同梱の vfx_sample.prefab が無いと作れない(無ければボタンを出さない)。
        public static bool CanVfx => Editor.Vfx.VfxSample.FindPrefab() != null;

        public static VfxData Vfx()
        {
            var prefab = Editor.Vfx.VfxSample.FindPrefab();
            if (prefab == null)
            {
                return null;
            }

            return Make<VfxData>(AssetType.Vfx, "Hit", "サンプル: ヒット", v => Editor.Vfx.VfxSample.Configure(v, prefab));
        }

        public static SeData Se()
        {
            var clip = BeepClip("Audio/SE");
            return Make<SeData>(AssetType.Se, "Beep", "サンプル: ピッ", s => s.Clips = new[] { clip });
        }

        public static BgmData Bgm()
        {
            var clip = BeepClip("Audio/BGM");
            return Make<BgmData>(AssetType.Bgm, "Loop", "サンプル: ループ", b => b.LoopBody = clip);
        }

        public static TextureData Texture()
        {
            var tex = GradientTexture("Texture");
            return Make<TextureData>(AssetType.Texture, "Gradient", "サンプル: グラデーション", t => t.Texture = tex);
        }

        public static MaterialData Material()
        {
            var shader = Shader.Find("DDrive/Lit");
            return Make<MaterialData>(AssetType.Material, "Orange", "サンプル: オレンジ", m =>
            {
                m.Shader = shader;
                m.Common = MaterialCommon.Default;
                m.Common.AlbedoTint = new Color(1f, 0.55f, 0.2f, 1f);
            });
        }

        public static ModelData Model()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Editor.Model.ModelSample.UnityChanPrefabPath)
                         ?? SavePrefab("Model", "ModelSample_Capsule", PrimitiveType.Capsule, Vector3.one);
            return Make<ModelData>(AssetType.Model, "Character", "サンプル: キャラクター", m => m.Prefab = prefab);
        }

        public static PrefabData Prefab()
        {
            var prefab = SavePrefab("Prefab", "PrefabSample_Barrel", PrimitiveType.Cylinder, new Vector3(0.8f, 0.6f, 0.8f));
            return Make<PrefabData>(AssetType.Prefab, "Barrel", "サンプル: 樽", p =>
            {
                p.Prefab = prefab;
                p.Kind = PrefabKind.Gimmick;
                p.GameplayTags = new[] { "Destructible", "Explosive" };
            });
        }

        public static AnimData Anim()
        {
            var clip = SampleClip();
            return Make<AnimData>(AssetType.Anim, "Spin", "サンプル: 回転", a => a.Clip = clip);
        }

        public static CanvasData Canvas()
        {
            var prefab = SaveCanvasPrefab();
            return Make<CanvasData>(AssetType.Canvas, "Panel", "サンプル: パネル", c => c.Prefab = prefab);
        }

        public static CameraShakeData Shake() =>
            Make<CameraShakeData>(AssetType.Shake, "Hit", "サンプル: 被弾(小)", s => CameraFxPresets.ApplyShakePreset(s, CameraFxPresetKind.HitSmall));

        public static HapticsData Haptics() =>
            Make<HapticsData>(AssetType.Haptics, "Hit", "サンプル: 被弾(小)", h => CameraFxPresets.ApplyHapticsPreset(h, CameraFxPresetKind.HitSmall));

        public static AnchorData Anchor() =>
            Make<AnchorData>(AssetType.Anchor, "Overhead", "サンプル: 頭上", a =>
            {
                a.Space = AnchorSpace.ContextTarget;
                a.LocalOffset = new Vector3(0f, 2f, 0f);
            });

        public static AnchorGroupData AnchorGroup() =>
            Make<AnchorGroupData>(AssetType.AnchorGroup, "Ring", "サンプル: 輪", g =>
            {
                g.Layout = AnchorLayoutKind.Circle;
                g.CircleCount = 6;
                g.CircleRadius = 1.5f;
            });

        public static UiTweenData UiTween() =>
            Make<UiTweenData>(AssetType.UiTween, "Pop", "サンプル: ポップイン", t =>
            {
                t.Tracks = new[]
                {
                    new TweenTrack
                    {
                        Property = TweenProperty.Scale,
                        Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.OutBack), Time = TimeDef.Duration(0.3f), Loop = LoopMode.Once },
                        From = TweenFromMode.Absolute,
                        FromValue = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(0f, 0f, 1f, 0f) },
                        ToValue = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(1f, 1f, 1f, 0f) },
                    },
                };
            });

        public static ButtonSkinData ButtonSkin() =>
            Make<ButtonSkinData>(AssetType.ControlSkin, "Button", "サンプル: ボタン", s => SkinStates(s));

        public static SliderSkinData SliderSkin() =>
            Make<SliderSkinData>(AssetType.ControlSkin, "Slider", "サンプル: スライダー", s =>
            {
                SkinStates(s);
                var fill = default(StateVisual);
                fill.Tint = new Color(0.29f, 0.55f, 1f, 1f);
                fill.Scale = ValueDef.Constant01(1f);
                s.Fill = fill;
            });

        public static PresentationData Presentation()
        {
            var vfx = Vfx();
            var se = Se();
            var shake = Shake();
            return Make<PresentationData>(AssetType.Presentation, "Hit", "サンプル: ヒット演出", p =>
            {
                p.TotalDuration = 1f;
                p.Interruptible = true;
                p.Tracks = new[]
                {
                    Track(TrackKind.Se, 0f, se, AssetType.Se),
                    Track(TrackKind.Vfx, 0.05f, vfx, AssetType.Vfx),
                    Track(TrackKind.CameraShake, 0.05f, shake, AssetType.Shake),
                };
            });
        }

        // ── 部品 ──

        private static PresentationTrack Track(TrackKind kind, float time, AssetDataBase data, AssetType type)
        {
            var t = PresentationTrackEditOps.CreateTrack(kind, time, null);
            if (data != null)
            {
                t.Asset = new AssetRef { Type = type, Id = data.Id };
            }

            return t;
        }

        private static void SkinStates(ControlSkinData s)
        {
            var hover = StateVisual.Default;
            hover.Tint = new Color(1f, 0.95f, 0.8f, 1f);
            hover.Scale = ValueDef.Constant01(1.06f);
            s.Hover = hover;
            var pressed = StateVisual.Default;
            pressed.Tint = new Color(0.8f, 0.8f, 0.85f, 1f);
            pressed.Scale = ValueDef.Constant01(0.94f);
            s.Pressed = pressed;
        }

        private static T Existing<T>(string display) where T : AssetDataBase
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var d = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && d.GetType() == typeof(T) && d.Category == Category && d.DisplayName == display)
                {
                    return d;
                }
            }

            return null;
        }

        private static T Make<T>(AssetType type, string identifier, string display, Action<T> configure) where T : AssetDataBase
        {
            var existing = Existing<T>(display);
            if (existing != null)
            {
                return existing;
            }

            var created = AssetCreationService.Create(typeof(T), type, display, Category, identifier, a => configure?.Invoke((T)a)) as T;
            if (created != null)
            {
                DDriveAssetSave.SaveAllSuppressed();
            }

            return created;
        }

        // Assets/GameData/<kind>/Sample(素材の置き場)。
        private static string Folder(string kind)
        {
            var path = AssetCreationService.ResolveGameDataRoot(Root);
            foreach (var seg in (kind + "/" + Category).Split('/'))
            {
                var next = path + "/" + seg;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(path, seg);
                }

                path = next;
            }

            return path;
        }

        // 0.3 秒のビープ音(440Hz の WAV)。
        private static AudioClip BeepClip(string kind)
        {
            var path = Folder(kind) + "/Sample_Beep.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
            {
                return clip;
            }

            const int rate = 44100;
            var n = (int)(rate * 0.3f);
            var data = new byte[44 + n * 2];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
            Put32(data, 4, 36 + n * 2);
            System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(data, 8);
            Put32(data, 16, 16);
            Put16(data, 20, 1);
            Put16(data, 22, 1);
            Put32(data, 24, rate);
            Put32(data, 28, rate * 2);
            Put16(data, 32, 2);
            Put16(data, 34, 16);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(data, 36);
            Put32(data, 40, n * 2);
            for (var i = 0; i < n; i++)
            {
                var env = 1f - i / (float)n;
                Put16(data, 44 + i * 2, (short)(Mathf.Sin(2f * Mathf.PI * 440f * i / rate) * 0.4f * env * short.MaxValue));
            }

            System.IO.File.WriteAllBytes(path, data);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void Put32(byte[] d, int o, int v)
        {
            for (var i = 0; i < 4; i++)
            {
                d[o + i] = (byte)(v >> (8 * i));
            }
        }

        private static void Put16(byte[] d, int o, int v)
        {
            d[o] = (byte)v;
            d[o + 1] = (byte)(v >> 8);
        }

        private static Texture2D GradientTexture(string kind)
        {
            var path = Folder(kind) + "/Sample_Gradient.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    t.SetPixel(x, y, Color.HSVToRGB(x / 64f, 0.5f, 0.6f + 0.4f * y / 64f));
                }
            }

            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static GameObject SavePrefab(string kind, string name, PrimitiveType shape, Vector3 scale)
        {
            var path = Folder(kind) + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.localScale = scale;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject SaveCanvasPrefab()
        {
            var path = Folder("Canvas") + "/CanvasSample_Panel.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject("SamplePanel", typeof(RectTransform), typeof(UnityEngine.Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            ((RectTransform)panel.transform).sizeDelta = new Vector2(400f, 200f);
            var button = new GameObject("BtnOk", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(panel.transform, false);
            ((RectTransform)button.transform).sizeDelta = new Vector2(160f, 48f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static AnimationClip SampleClip()
        {
            var path = Folder("Anim") + "/Sample_Spin.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                return existing;
            }

            var clip = new AnimationClip { frameRate = 30f };
            clip.SetCurve(string.Empty, typeof(Transform), "localEulerAngles.y", AnimationCurve.Linear(0f, 0f, 1f, 360f));
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
    }
}
