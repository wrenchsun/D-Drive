using System;
using UnityEngine;

namespace DDrive.Runtime.Material
{
    // [06_material_texture.md] A-2「相互変換機能」— シェーダー固有パラメータの対応表(チケット 3-6、2026-09-10)。
    // 共通データ(MaterialCommon)は変換不要でそのまま引き継がれる。ここに載るのは Specific(固有)だけ。
    // 「ShaderA の _SpecColor → ShaderB の _F0」のような対応を登録制で持ち、無いものは変換時に破棄(警告)される。
    // ID を持つ Data ではなく設定アセット(1 プロジェクトに複数可。変換エディタが全 Table を集めて探す)。
    [CreateAssetMenu(menuName = "D-Drive/Material/Shader Conversion Table", fileName = "ShaderConversionTable")]
    public sealed class ShaderConversionTable : ScriptableObject
    {
        [Serializable]
        public struct Mapping
        {
            [Tooltip("変換元シェーダーのプロパティ名。")]
            public string FromProperty;

            [Tooltip("変換先シェーダーのプロパティ名。空なら「意図的に破棄」(警告を出さない)。")]
            public string ToProperty;

            [Tooltip("Float / Int のとき: 変換先 = 元 × Scale + Offset(既定 Scale=1, Offset=0)。")]
            public float Scale;

            public float Offset;

            public bool IsDrop => string.IsNullOrEmpty(ToProperty);
        }

        [Serializable]
        public struct Rule
        {
            [Tooltip("変換元シェーダー。")]
            public Shader From;

            [Tooltip("変換先シェーダー。")]
            public Shader To;

            [Tooltip("固有パラメータの対応。ここに無いものは、変換先に同名・同型があればそのまま、無ければ破棄(警告)。")]
            public Mapping[] Mappings;
        }

        [Tooltip("変換ルール一覧。同じ From/To の組は最初の 1 件だけ使われる。")]
        public Rule[] Rules;

        public bool TryFind(Shader from, Shader to, out Rule rule)
        {
            if (Rules != null && from != null && to != null)
            {
                for (var i = 0; i < Rules.Length; i++)
                {
                    if (Rules[i].From == from && Rules[i].To == to)
                    {
                        rule = Rules[i];
                        return true;
                    }
                }
            }

            rule = default;
            return false;
        }
    }
}
