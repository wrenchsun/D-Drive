using System;
using System.Collections.Generic;
using DDrive.Runtime.Ui;

namespace DDrive.Editor.Ui
{
    // [15_ui_interaction.md] B-3.5「エディタ: プリセットギャラリー」(4-12) — タブ/検索の絞り込みロジック。
    // UiPresetGalleryWindow から切り出した純粋関数(EditorWindow を経由せずテストできるようにする)。
    public enum GalleryTab
    {
        出現,
        常時,
        消滅,
        強調,
        カタログ,
    }

    // 1 枚のカード。組み込みプリセットは Preset!=None かつ CatalogTween==null、
    // 独自カタログ登録は Preset==None かつ CatalogTween!=null。
    public readonly struct GalleryCard : IEquatable<GalleryCard>
    {
        public readonly string Name;
        public readonly GalleryTab Tab;
        public readonly UiPreset Preset;
        public readonly UiTweenData CatalogTween;

        public GalleryCard(string name, GalleryTab tab, UiPreset preset, UiTweenData catalogTween)
        {
            Name = name;
            Tab = tab;
            Preset = preset;
            CatalogTween = catalogTween;
        }

        public bool Equals(GalleryCard other) => Name == other.Name && Tab == other.Tab && Preset == other.Preset && CatalogTween == other.CatalogTween;
        public override bool Equals(object obj) => obj is GalleryCard other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Name, Tab, Preset);
    }

    public static class UiPresetGalleryFilter
    {
        // タブ一致 → 検索語(Name の部分一致、大文字小文字無視)の順で絞り込む。
        // favourites が渡された場合は該当カードを結果の先頭へ寄せる(除外はしない。「お気に入りのみ」は
        // 呼び出し側でさらに Where(c => favourites.Contains(c.Name)) すれば表現できる)。
        public static List<GalleryCard> Filter(IEnumerable<GalleryCard> all, GalleryTab tab, string query, ISet<string> favourites)
        {
            if (all == null)
            {
                return new List<GalleryCard>();
            }

            var q = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
            var matched = new List<GalleryCard>();
            foreach (var card in all)
            {
                if (card.Tab != tab)
                {
                    continue;
                }

                if (q != null && card.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matched.Add(card);
            }

            if (favourites == null || favourites.Count == 0)
            {
                return matched;
            }

            var favored = new List<GalleryCard>();
            var rest = new List<GalleryCard>();
            foreach (var card in matched)
            {
                (favourites.Contains(card.Name) ? favored : rest).Add(card);
            }

            favored.AddRange(rest);
            return favored;
        }

        // UiPreset enum(None を除く) + カタログ収集結果から全カードを構築する。
        public static List<GalleryCard> BuildBuiltinCards()
        {
            var result = new List<GalleryCard>();
            foreach (UiPreset preset in Enum.GetValues(typeof(UiPreset)))
            {
                if (preset == UiPreset.None)
                {
                    continue;
                }

                result.Add(new GalleryCard(preset.ToString(), TabOf(preset), preset, null));
            }

            return result;
        }

        public static List<GalleryCard> BuildCatalogCards(IEnumerable<(string name, UiTweenData tween)> catalogEntries)
        {
            var result = new List<GalleryCard>();
            if (catalogEntries == null)
            {
                return result;
            }

            foreach (var (name, tween) in catalogEntries)
            {
                result.Add(new GalleryCard(name, GalleryTab.カタログ, UiPreset.None, tween));
            }

            return result;
        }

        // [15] B-3.5 の掲載順(出現 / 消滅 / 常時 / 強調)に基づく分類。UiPreset.cs のコメント区切りと 1:1 対応させる
        // (文字列パターンでの推測はしない。新規プリセット追加時はここにも追記する)。
        public static GalleryTab TabOf(UiPreset preset)
        {
            switch (preset)
            {
                case UiPreset.FadeIn:
                case UiPreset.SlideInLeft:
                case UiPreset.SlideInRight:
                case UiPreset.SlideInTop:
                case UiPreset.SlideInBottom:
                case UiPreset.ScaleIn:
                case UiPreset.PopIn:
                case UiPreset.BounceIn:
                case UiPreset.ElasticIn:
                case UiPreset.FlipInX:
                case UiPreset.FlipInY:
                case UiPreset.RotateIn:
                case UiPreset.ZoomInFade:
                case UiPreset.SlideFadeInLeft:
                case UiPreset.SlideFadeInRight:
                case UiPreset.SlideFadeInTop:
                case UiPreset.SlideFadeInBottom:
                case UiPreset.ExpandWidth:
                case UiPreset.ExpandHeight:
                case UiPreset.TypeFillIn:
                    return GalleryTab.出現;

                case UiPreset.FadeOut:
                case UiPreset.SlideOutLeft:
                case UiPreset.SlideOutRight:
                case UiPreset.SlideOutTop:
                case UiPreset.SlideOutBottom:
                case UiPreset.ScaleOut:
                case UiPreset.PopOut:
                case UiPreset.BounceOut:
                case UiPreset.ElasticOut:
                case UiPreset.FlipOutX:
                case UiPreset.FlipOutY:
                case UiPreset.RotateOut:
                case UiPreset.ZoomOutFade:
                case UiPreset.SlideFadeOutLeft:
                case UiPreset.SlideFadeOutRight:
                case UiPreset.SlideFadeOutTop:
                case UiPreset.SlideFadeOutBottom:
                case UiPreset.CollapseWidth:
                case UiPreset.CollapseHeight:
                    return GalleryTab.消滅;

                case UiPreset.Pulse:
                case UiPreset.Blink:
                case UiPreset.Float:
                case UiPreset.Sway:
                case UiPreset.Breathe:
                case UiPreset.RotateLoop:
                case UiPreset.ShimmerAlpha:
                case UiPreset.RainbowTint:
                case UiPreset.WobbleLoop:
                    return GalleryTab.常時;

                default:
                    // PunchScale / PunchRotation / Shake / ShakeHard / Flash / ColorFlash / HeartBeat /
                    // Jelly / Tada / RubberBand / AttentionJump(強調系)。
                    return GalleryTab.強調;
            }
        }
    }
}
