using DDrive.Editor.Anim;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Identity;
using DDrive.Foundation.Values;
using DDrive.Runtime.Audio;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Ui
{
    // 実運用規模の確認用サンプル(docs/1010 D-10 / UX-13)。
    // Button = 状態 6 つがすべて設定済みの Button Skin(色・拡大率・画像・コマ画像 4 枚 + 1 秒 8 コマ・スクロール・演出 Tween / Preset が混在、SE 3 つ、押せる範囲、
    //   透明部分の判定で「Read/Write 無効」の警告が出る)。ButtonEmpty = 何も設定していない既定のままの Button Skin。
    // Slider = 状態 6 つ + パーツ 4 つ(Track / Fill / Handle / Delay Fill)・目盛りの装飾・SE 5 つ・触覚(存在しない Id で警告)・押せる範囲 3 種。SliderEmpty = 既定のまま。
    // ほか: Tween(演出に使う UiTweenData)・SeData 3 つ・コマ画像(Anim2DSample のスプライトシートを借りる)。
    // 指定のフォルダ(Assets/ 配下)に作る。呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。
    // スクリーンショット・テスト・見通しの確認用。
    internal static class SkinSample
    {
        public const ulong BaseId = 0xDDE00000;

        public sealed class Result
        {
            public ButtonSkinData Button;
            public ButtonSkinData ButtonEmpty;
            public SliderSkinData Slider;
            public SliderSkinData SliderEmpty;
            public UiTweenData Tween;
            public SeData[] Ses;
            public Sprite[] Sprites;
            public string Folder;
        }

        public static Result Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = System.IO.Path.GetDirectoryName(folder).Replace((char)92, (char)47);
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
            }

            var r = new Result { Folder = folder };

            // コマ画像(24 コマのスプライトシート)は Anim2DSample のものを借りる(サブフォルダにまとまる)。
            var sheet = Anim2DSample.Build(folder + "/Sheet");
            r.Sprites = sheet.Sprites;

            r.Ses = new[]
            {
                CreateSe(folder, "SE_Hover", BaseId + 1, "ホバー音"),
                CreateSe(folder, "SE_Click", BaseId + 2, "決定音"),
                CreateSe(folder, "SE_Denied", BaseId + 3, "拒否音"),
            };

            var tween = ScriptableObject.CreateInstance<UiTweenData>();
            tween.Id = BaseId + 10;
            tween.DisplayName = "押したときの沈み込み";
            tween.Category = "Sample";
            tween.Tracks = new[]
            {
                new TweenTrack
                {
                    Property = TweenProperty.Scale,
                    Motion = new ValueDef { Mode = ValueMode.Parametric, Parametric = EaseDef.Named(DDrive.Foundation.Easing.Ease.OutBack), Time = TimeDef.Duration(0.2f), Loop = LoopMode.Once },
                    From = TweenFromMode.Absolute,
                    FromValue = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(0.85f, 0.85f, 1f, 0f) },
                    ToValue = new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(1f, 1f, 1f, 0f) },
                },
            };
            AssetDatabase.CreateAsset(tween, $"{folder}/UITWEEN_Sample_Press.asset");
            r.Tween = tween;

            var scroll = AssetDatabase.LoadAssetAtPath<Material>(ControlSkinPreviewSection.DefaultScrollMaterialPath);

            // ── Button Skin(主役) ──
            var button = Skin<ButtonSkinData>(BaseId + 100, "メニューボタン(全部入り)");
            FillStates(button, r, scroll);
            button.HoverSe = SeRef(r.Ses[0]);
            button.ClickSe = SeRef(r.Ses[1]);
            button.DeniedSe = SeRef(r.Ses[2]);
            button.HitAreaExpand = new Vector4(8f, 8f, 8f, 8f);
            button.AlphaHitThreshold = 0.3f; // 画像の Read/Write が無効 → 検証の警告
            AssetDatabase.CreateAsset(button, $"{folder}/SKIN_Sample_Button.asset");
            r.Button = button;

            var buttonEmpty = Skin<ButtonSkinData>(BaseId + 101, "既定のままのボタン");
            AssetDatabase.CreateAsset(buttonEmpty, $"{folder}/SKIN_Sample_ButtonEmpty.asset");
            r.ButtonEmpty = buttonEmpty;

            // ── Slider Skin(主役) ──
            var slider = Skin<SliderSkinData>(BaseId + 200, "音量スライダー(全部入り)");
            FillStates(slider, r, scroll);
            slider.Track = Part(new Color(0.25f, 0.27f, 0.32f, 1f), 1f, null);
            slider.Fill = Part(new Color(0.29f, 0.55f, 1f, 1f), 1f, null);
            slider.Handle = Part(Color.white, 1.1f, r.Sprites[1]);
            slider.DelayFill = Part(new Color(1f, 1f, 1f, 0.45f), 1f, null);
            slider.NotchSprite = r.Sprites[5];
            slider.HideHandleOnGamepad = true;
            slider.ExtraHitPadding = new Vector2(6f, 10f);
            slider.HandleHitAreaExpand = new Vector4(6f, 6f, 6f, 6f);
            slider.HitAreaExpand = new Vector4(4f, 4f, 4f, 4f);
            slider.GrabSe = SeRef(r.Ses[0]);
            slider.ReleaseSe = SeRef(r.Ses[1]);
            slider.NotchSe = SeRef(r.Ses[0]);
            slider.LimitSe = SeRef(r.Ses[1]);
            slider.DeniedSe = SeRef(r.Ses[2]);
            slider.NotchHapticId = 0xDEAD0001; // 存在しない Haptics → 検証の警告
            AssetDatabase.CreateAsset(slider, $"{folder}/SKIN_Sample_Slider.asset");
            r.Slider = slider;

            var sliderEmpty = Skin<SliderSkinData>(BaseId + 201, "既定のままのスライダー");
            AssetDatabase.CreateAsset(sliderEmpty, $"{folder}/SKIN_Sample_SliderEmpty.asset");
            r.SliderEmpty = sliderEmpty;

            DDriveAssetSave.SaveAllSuppressed();
            return r;
        }

        // 状態 6 つ(色・拡大率・画像・コマ・スクロール・演出が混在)。
        private static void FillStates(ControlSkinData skin, Result r, Material scroll)
        {
            skin.ScrollMaterial = scroll;

            // Normal は既定のまま(白・等倍)。
            skin.Hover = State(new Color(1f, 0.95f, 0.8f, 1f), 1.06f);
            skin.Hover.EnterPreset = new UiPresetRef { Preset = UiPreset.PunchScale, Duration = 0.25f };

            skin.Pressed = State(new Color(0.8f, 0.8f, 0.85f, 1f), 0.94f);
            skin.Pressed.EnterTween = new AssetId<UiTweenMarker>(r.Tween.Id, AssetType.UiTween);

            skin.Selected = State(new Color(0.7f, 0.9f, 1f, 1f), 1.02f);
            skin.Selected.OverrideSprite = r.Sprites[3];
            skin.Selected.AnimFrames = new[] { r.Sprites[3], r.Sprites[4], r.Sprites[5], r.Sprites[6] };
            skin.Selected.AnimFps = 8f;
            skin.Selected.AnimLoop = true;

            skin.Disabled = State(new Color(0.5f, 0.5f, 0.5f, 0.6f), 1f);

            skin.Locked = State(new Color(0.4f, 0.4f, 0.5f, 0.85f), 1f);
            skin.Locked.OverrideSprite = r.Sprites[8];
            skin.Locked.ScrollSpeed = new Vector2(0.2f, 0f);
        }

        private static StateVisual State(Color tint, float scale)
        {
            var v = StateVisual.Default;
            v.Tint = tint;
            v.Scale = ValueDef.Constant01(scale);
            return v;
        }

        // パーツ(状態に依らない固定の見た目)。拡大率 0 = 変えない。
        private static StateVisual Part(Color tint, float scale, Sprite sprite)
        {
            var v = default(StateVisual);
            v.Tint = tint;
            v.Scale = ValueDef.Constant01(scale);
            v.OverrideSprite = sprite;
            return v;
        }

        private static T Skin<T>(ulong id, string display) where T : ControlSkinData
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.Id = id;
            d.DisplayName = display;
            d.Category = "Sample";
            return d;
        }

        private static SeData CreateSe(string folder, string name, ulong id, string display)
        {
            var d = ScriptableObject.CreateInstance<SeData>();
            d.Id = id;
            d.DisplayName = display;
            d.Category = "Sample";
            AssetDatabase.CreateAsset(d, $"{folder}/{name}.asset");
            return d;
        }

        private static AssetId<SeMarker> SeRef(SeData d) => new AssetId<SeMarker>(d.Id, AssetType.Se);
    }
}
