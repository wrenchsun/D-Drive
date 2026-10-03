using System;
using System.Collections.Generic;

namespace DDrive.Runtime.Anim
{
    // FC-20 / [docs/51] §4.21。「外部パッケージが所有するブレンドシェイプ」の接頭辞の一覧(定数 1 か所)。
    // これらで始まる名前のシェイプは、外部パッケージ(例: 顔の補正を LateUpdate で書くもの)が重みを書く。
    // D-Drive の AnimData.BlendShapes から同じシェイプを指定すると書き込みが衝突するため、Validator が警告し、
    // AnimEditor は既定で候補から隠す。実行時の挙動は変えない(AnimatorProxy は AnimData が指定した名前だけを書く)。
    // 照合は Ordinal(大文字小文字を区別する前方一致)。外部パッケージの型は参照しない(文字列の既定値だけ)。
    public static class ExternalBlendShapePrefixes
    {
        private static readonly string[] OwnedPrefixes = { "FC_", "fcs_" };

        // 一覧(読み取り専用)。登録口は設けない。
        public static IReadOnlyList<string> All => OwnedPrefixes;

        // shapeName が外部パッケージの所有する接頭辞で始まるなら true。null / 空は false。
        public static bool IsOwnedExternally(string shapeName)
        {
            if (string.IsNullOrEmpty(shapeName))
            {
                return false;
            }

            for (var i = 0; i < OwnedPrefixes.Length; i++)
            {
                if (shapeName.StartsWith(OwnedPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
