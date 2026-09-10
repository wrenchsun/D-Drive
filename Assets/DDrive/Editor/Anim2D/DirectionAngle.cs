using System.Collections.Generic;
using UnityEngine;

namespace DDrive.Editor.Anim2D
{
    // [05_model_animation.md] Part C — 命名規則・BlendTree 登録に使う 8 方向 + 無し + 命名規則参照 の選択肢。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.DirectionMode
    public enum DirectionMode
    {
        /// <summary>画像名の末尾 "_0"〜"_315" を参照して自動決定。</summary>
        FromName,

        /// <summary>方向を命名規則に含めない。</summary>
        None,
        Deg0,
        Deg45,
        Deg90,
        Deg135,
        Deg180,
        Deg225,
        Deg270,
        Deg315,
    }

    // DirectionMode と角度(int)と BlendTree 2D Freeform Directional の座標(Vector2)の相互変換。
    // 移植元: Katsuya.Tools.SpriteAnimation.EditorTools.DirectionAngle(ロジックは同一、名前空間のみ D-Drive 化)。
    public static class DirectionAngle
    {
        // 8 方向の角度と、2D Freeform Directional で使う単位ベクトル座標の対応表。
        // +Y を 0°(前方)、時計回りに 45° 刻み。
        public static readonly (int angle, Vector2 position)[] Map =
        {
            (0, new Vector2(0f, 1f)),
            (45, new Vector2(0.707f, 0.707f)),
            (90, new Vector2(1f, 0f)),
            (135, new Vector2(0.707f, -0.707f)),
            (180, new Vector2(0f, -1f)),
            (225, new Vector2(-0.707f, -0.707f)),
            (270, new Vector2(-1f, 0f)),
            (315, new Vector2(-0.707f, 0.707f)),
        };

        // 4 方向セット(DirectionSet.Four)で使う角度のみの部分集合。
        public static readonly int[] FourAngles = { 0, 90, 180, 270 };
        public static readonly int[] EightAngles = { 0, 45, 90, 135, 180, 225, 270, 315 };

        public static bool HasAngle(DirectionMode mode) => mode != DirectionMode.FromName && mode != DirectionMode.None;

        public static int ToAngle(DirectionMode mode) => mode switch
        {
            DirectionMode.Deg0 => 0,
            DirectionMode.Deg45 => 45,
            DirectionMode.Deg90 => 90,
            DirectionMode.Deg135 => 135,
            DirectionMode.Deg180 => 180,
            DirectionMode.Deg225 => 225,
            DirectionMode.Deg270 => 270,
            DirectionMode.Deg315 => 315,
            _ => -1,
        };

        public static DirectionMode FromAngle(int angle) => angle switch
        {
            0 => DirectionMode.Deg0,
            45 => DirectionMode.Deg45,
            90 => DirectionMode.Deg90,
            135 => DirectionMode.Deg135,
            180 => DirectionMode.Deg180,
            225 => DirectionMode.Deg225,
            270 => DirectionMode.Deg270,
            315 => DirectionMode.Deg315,
            _ => DirectionMode.None,
        };

        public static bool TryGetPosition(int angle, out Vector2 position)
        {
            foreach (var pair in Map)
            {
                if (pair.angle == angle)
                {
                    position = pair.position;
                    return true;
                }
            }

            position = Vector2.zero;
            return false;
        }

        // 再選択 UI で使う、命名規則(FromName)を除いた選択肢リスト。
        public static readonly DirectionMode[] FallbackChoices =
        {
            DirectionMode.None,
            DirectionMode.Deg0,
            DirectionMode.Deg45,
            DirectionMode.Deg90,
            DirectionMode.Deg135,
            DirectionMode.Deg180,
            DirectionMode.Deg225,
            DirectionMode.Deg270,
            DirectionMode.Deg315,
        };

        public static readonly DirectionMode[] AllChoices =
        {
            DirectionMode.FromName,
            DirectionMode.None,
            DirectionMode.Deg0,
            DirectionMode.Deg45,
            DirectionMode.Deg90,
            DirectionMode.Deg135,
            DirectionMode.Deg180,
            DirectionMode.Deg225,
            DirectionMode.Deg270,
            DirectionMode.Deg315,
        };

        public static string ToDisplay(DirectionMode mode) => mode switch
        {
            DirectionMode.FromName => "命名規則",
            DirectionMode.None => "無し",
            _ => ToAngle(mode).ToString(),
        };

        public static string[] BuildDisplayNames(IReadOnlyList<DirectionMode> choices)
        {
            var arr = new string[choices.Count];
            for (var i = 0; i < choices.Count; i++)
            {
                arr[i] = ToDisplay(choices[i]);
            }

            return arr;
        }
    }
}
