using System;
using DDrive.Editor.Versioning;
using DDrive.Foundation.Data;
using DDrive.Foundation.Easing;
using DDrive.Foundation.Values;
using DDrive.Runtime.Ui;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Ui
{
    // 実運用規模の確認用サンプル(docs/1010 D-10 / UX-12)。
    // 主役 Main = 「HUD の入場演出」: 12 トラック(位置・サイズ・拡縮・回転・透明度・色・色相・塗り・経路・回転 X)で、イージングが OutSine / OutCubic / OutBack / OutQuad / Linear /
    //   InOutSine / InOutBack / OutElastic / InBounce / ベジェ / カーブに混在し、ループ(無限・回数指定・往復)・開始の遅れ・8 本を超えた分の警告も入る。
    // ほか: Big = 22 トラック(20 行を超えるので一覧は ListView)・Pop(3 トラック)・Simple(1 トラック)・空の Tween。
    // Catalog = UiPresetCatalog(登録 24 件。ギャラリーの「カタログ」タブが 20 件を超えて ListView になる)。
    // 指定のフォルダ(Assets/ 配下)に作る。呼び出し側が使い終わったらフォルダごと消す(実データ Assets/GameData には作らない。Addressables にも登録しない)。
    // スクリーンショット・テスト・見通しの確認用。
    internal static class UiTweenSample
    {
        public const ulong BaseId = 0xDDD00000;

        public sealed class Result
        {
            public UiTweenData Main;       // 12 トラック
            public UiTweenData Big;        // 22 トラック
            public UiTweenData Pop;        // 3 トラック
            public UiTweenData Simple;     // 1 トラック
            public UiTweenData Empty;      // 0 トラック
            public UiPresetCatalog Catalog;
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
            r.Main = Create(folder, "UITWEEN_Sample_HudEnter", BaseId + 1, "HUD の入場演出", MainTracks());
            r.Big = Create(folder, "UITWEEN_Sample_Big", BaseId + 2, "全部入り(22 トラック)", BigTracks());
            r.Pop = Create(folder, "UITWEEN_Sample_Pop", BaseId + 3, "ポップイン", new[]
            {
                Track(TweenProperty.Scale, Vec(0f, 0f, 0f), Vec(1f, 1f, 1f), 0.3f, Ease.OutBack, 0f),
                Track(TweenProperty.Alpha, F(0f), F(1f), 0.2f, Ease.OutSine, 0f),
                Track(TweenProperty.Rotation, F(-12f), F(0f), 0.3f, Ease.OutBack, 0.05f),
            });
            r.Simple = Create(folder, "UITWEEN_Sample_Fade", BaseId + 4, "フェードイン", new[] { Track(TweenProperty.Alpha, F(0f), F(1f), 0.25f, Ease.OutSine, 0f) });
            r.Empty = Create(folder, "UITWEEN_Sample_Empty", BaseId + 5, "空の Tween", Array.Empty<TweenTrack>());

            var catalog = ScriptableObject.CreateInstance<UiPresetCatalog>();
            var names = new[] { "HUD 入場", "ポップ", "フェード", "全部入り" };
            var tweens = new[] { r.Main, r.Pop, r.Simple, r.Big };
            var entries = new UiPresetCatalog.Entry[24];
            for (var i = 0; i < entries.Length; i++)
            {
                entries[i] = new UiPresetCatalog.Entry
                {
                    Name = i < names.Length ? names[i] : $"{names[i % names.Length]} {i / names.Length + 1}",
                    Tween = tweens[i % tweens.Length],
                    Category = "Sample",
                };
            }

            catalog.Entries = entries;
            AssetDatabase.CreateAsset(catalog, $"{folder}/UIPRESETCATALOG_Sample.asset");
            r.Catalog = catalog;

            DDriveAssetSave.SaveAllSuppressed();
            return r;
        }

        private static UiTweenData Create(string folder, string name, ulong id, string display, TweenTrack[] tracks)
        {
            var d = ScriptableObject.CreateInstance<UiTweenData>();
            d.Id = id;
            d.DisplayName = display;
            d.Category = "Sample";
            d.Tracks = tracks;
            AssetDatabase.CreateAsset(d, $"{folder}/{name}.asset");
            return d;
        }

        private static TweenTrack[] MainTracks()
        {
            var bez = Track(TweenProperty.AnchoredPosition, Vec(0f, -40f, 0f), Vec(0f, 0f, 0f), 0.5f, Ease.Linear, 0.3f);
            bez.Motion.Parametric = EaseDef.Bezier(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1.4f));

            var curve = Track(TweenProperty.Rotation, F(0f), F(360f), 1.2f, Ease.Linear, 1.0f);
            curve.Motion.Mode = ValueMode.Curve;
            curve.Motion.Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.2f), new Keyframe(1f, 1f));
            curve.Motion.Normalized = true;

            var path = Track(TweenProperty.PathMove, default, default, 0.9f, Ease.InOutSine, 0.5f);
            path.From = TweenFromMode.Current;
            path.Path = new SplinePathDef
            {
                Type = SplineType.CatmullRom,
                Points = new[] { new Vector3(0f, 0f, 0f), new Vector3(40f, 30f, 0f), new Vector3(90f, -10f, 0f), new Vector3(140f, 20f, 0f) },
            };

            var slide = Track(TweenProperty.AnchoredPosition, Vec(-300f, 0f, 0f), Vec(0f, 0f, 0f), 0.4f, Ease.OutCubic, 0f);
            slide.From = TweenFromMode.OffScreen;
            slide.OffScreen = OffScreenDirection.Left;

            return new[]
            {
                Track(TweenProperty.Alpha, F(0f), F(1f), 0.25f, Ease.OutSine, 0f),
                slide,
                Track(TweenProperty.Scale, Vec(0f, 0f, 0f), Vec(1f, 1f, 1f), 0.35f, Ease.OutBack, 0.1f),
                Track(TweenProperty.Rotation, F(-15f), F(0f), 0.35f, Ease.OutBack, 0.1f),
                Track(TweenProperty.Color, Col(Color.white), Col(new Color(1f, 0.82f, 0.25f)), 0.3f, Ease.OutQuad, 0.2f),
                Track(TweenProperty.SizeDelta, Vec(0f, 80f, 0f), Vec(240f, 80f, 0f), 0.5f, Ease.OutCubic, 0.15f),
                Track(TweenProperty.FillAmount, F(0f), F(1f), 0.8f, Ease.Linear, 0.4f),
                path,
                Loop(Track(TweenProperty.Scale, Vec(1f, 1f, 1f), Vec(1.08f, 1.08f, 1f), 1.2f, Ease.InOutSine, 0.6f), LoopMode.PingPong, 0),
                Loop(Track(TweenProperty.Alpha, F(1f), F(0.4f), 0.6f, Ease.InOutSine, 0.8f), LoopMode.PingPong, 3),
                Loop(Track(TweenProperty.ColorHue, F(0f), F(1f), 2f, Ease.Linear, 1.0f), LoopMode.Loop, 0),
                Track(TweenProperty.RotationX, F(0f), F(90f), 0.5f, Ease.InOutBack, 1.2f),
                bez,
                curve,
            };
        }

        // 22 トラック(種類・イージングを順に変えたもの)。8 本を超えると警告が出ることと、20 行を超える一覧が ListView になることの確認用。
        private static TweenTrack[] BigTracks()
        {
            var props = new[]
            {
                TweenProperty.Alpha, TweenProperty.Scale, TweenProperty.Rotation, TweenProperty.Color, TweenProperty.AnchoredPosition,
                TweenProperty.SizeDelta, TweenProperty.FillAmount, TweenProperty.RotationX, TweenProperty.RotationY, TweenProperty.ColorHue, TweenProperty.Scale,
            };
            var eases = new[] { Ease.OutSine, Ease.OutBack, Ease.InOutCubic, Ease.OutElastic, Ease.InBounce, Ease.Linear, Ease.OutQuad, Ease.InOutExpo };
            var result = new TweenTrack[22];
            for (var i = 0; i < result.Length; i++)
            {
                var p = props[i % props.Length];
                var t = NewFor(p);
                t.Motion.Parametric = EaseDef.Named(eases[i % eases.Length]);
                t.Motion.Time = TimeDef.Duration(0.2f + 0.05f * (i % 7));
                t.Delay = 0.05f * i;
                if (i % 5 == 4)
                {
                    t.Motion.Loop = LoopMode.PingPong;
                    t.Motion.LoopCount = i % 2 == 0 ? 0 : 2;
                }

                result[i] = t;
            }

            return result;
        }

        private static TweenTrack NewFor(TweenProperty p)
        {
            switch (p)
            {
                case TweenProperty.Alpha: return Track(p, F(0f), F(1f), 0.3f, Ease.OutSine, 0f);
                case TweenProperty.Scale: return Track(p, Vec(0.5f, 0.5f, 0.5f), Vec(1f, 1f, 1f), 0.3f, Ease.OutBack, 0f);
                case TweenProperty.Color: return Track(p, Col(Color.white), Col(Color.red), 0.3f, Ease.OutSine, 0f);
                case TweenProperty.AnchoredPosition: return Track(p, Vec(-120f, 0f, 0f), Vec(0f, 0f, 0f), 0.3f, Ease.OutCubic, 0f);
                case TweenProperty.SizeDelta: return Track(p, Vec(0f, 60f, 0f), Vec(200f, 60f, 0f), 0.3f, Ease.OutCubic, 0f);
                case TweenProperty.FillAmount: return Track(p, F(0f), F(1f), 0.3f, Ease.Linear, 0f);
                default: return Track(p, F(0f), F(45f), 0.3f, Ease.OutSine, 0f);
            }
        }

        private static TweenTrack Track(TweenProperty p, ParamValue from, ParamValue to, float duration, Ease ease, float delay)
        {
            return new TweenTrack
            {
                Property = p,
                Motion = new ValueDef
                {
                    Mode = ValueMode.Parametric,
                    Parametric = EaseDef.Named(ease),
                    Time = TimeDef.Duration(duration),
                    Loop = LoopMode.Once,
                },
                Delay = delay,
                From = TweenFromMode.Absolute,
                FromValue = from,
                ToValue = to,
            };
        }

        private static TweenTrack Loop(TweenTrack t, LoopMode mode, int count)
        {
            t.Motion.Loop = mode;
            t.Motion.LoopCount = count;
            return t;
        }

        private static ParamValue F(float v) => ParamValue.Of(v);

        private static ParamValue Vec(float x, float y, float z) => new ParamValue { Type = ParamValueType.Vector, VectorValue = new Vector4(x, y, z, 0f) };

        private static ParamValue Col(Color c) => new ParamValue { Type = ParamValueType.Color, ColorValue = c };
    }
}
