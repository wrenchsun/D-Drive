using System;
using System.Collections.Generic;
using DDrive.Editor.Import;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [51_tdrive_integration.md] §4.15(FC-14、2026-10-03) — 外部パッケージがコードから足せるテクスチャ取り込み規則
    // (例: 接尾辞 `_ToonMask` → sRGB オフ)の拡張点。規則の型は Profile の規則(TextureImportProfile.Rule)と同じ。
    //
    // 評価位置: TextureImportProfile.TryMatch が **Profile の Rules より前**に外部規則を評価する
    // (`T_` 接頭辞の規則より先に効く)。TexturePostprocessor / TextureDataValidator / TextureDataImporterSync /
    // ImportRule(Texture)/ Maya 取り込み / Material エディタ はすべて TryMatch を通るので、一箇所で全てに効く。
    // パス判定(AppliesTo = IncludePathContains)・Enabled は従来どおり Profile 側で行われる(外部規則も同じ対象にだけ効く)。
    //
    // プロジェクトの上書き: **Profile の Rules に、外部規則と同じ条件(Match の種類 + Pattern、大文字小文字無視)の規則が
    // あれば、その外部規則は使われず Profile の規則が効く**(プロジェクトが Profile で明示的に決めたものが勝つ)。
    // 異なる条件の規則は上書きされない(外部規則が先)。
    //
    // 外部規則が 1 つも無いときの挙動は従来と完全に同じ。
    //
    // 発見: `TypeCache`(FC-5 と同じ方式)。public な非 abstract 型 + public な引数なしコンストラクタが必須。
    // `DDrive.Tests*` で始まるアセンブリの実装は除外する。GetRules() は **ドメインリロードごとに 1 回**呼んで結果をキャッシュする
    // (テクスチャ 1 枚ごとに外部コードを呼ばない)。例外は実装ごとに隔離する。型のフルネーム順 → 返した順に評価。
    // 互換: DDrive.Editor の弱い互換面(Editor 契約。docs/42 §5.9 / §5.14 E-22)。追加のみ。
    public interface ITextureImportRuleProvider
    {
        // 追加する規則(Pattern が空のものは無視される)。
        IEnumerable<TextureImportProfile.Rule> GetRules();
    }

    public static class TextureImportRuleProviders
    {
        private static TextureImportProfile.Rule[] _rules;

        // 外部規則(キャッシュ済み。呼び出し順に評価する)。
        public static IReadOnlyList<TextureImportProfile.Rule> Rules => _rules ??= Gather();

        // 発見結果を捨てる(テスト専用。テスト用ダミー提供口が static フラグで切り替わるため)。
        public static void ResetCacheForTests() => _rules = null;

        private static TextureImportProfile.Rule[] Gather()
        {
            var list = new List<TextureImportProfile.Rule>();
            foreach (var provider in ExtensionPointDiscovery.Instantiate<ITextureImportRuleProvider>())
            {
                try
                {
                    var rules = provider.GetRules();
                    if (rules == null)
                    {
                        continue;
                    }

                    foreach (var rule in rules)
                    {
                        if (!string.IsNullOrEmpty(rule.Pattern))
                        {
                            list.Add(rule);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return list.ToArray();
        }
    }
}
