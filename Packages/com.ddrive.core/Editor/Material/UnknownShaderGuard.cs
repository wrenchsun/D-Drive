using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Materials
{
    // [06_material_texture.md] A-2 / [51_tdrive_integration.md] §4.16 FC-15(2026-10-03、U-9 = (c))。
    // 取り込み元 Material のシェーダーが D-Drive の変換表(UnityMaterialMigrator の ShaderMap)にも DDrive/ 接頭辞にも当たらない
    // (= 知らないシェーダー)とき、MaterialData のシェーダーをどう扱うかの方針。Profile(MayaImportProfile)の欄で決める。
    public enum UnknownShaderPolicy
    {
        Ask = 0,          // 対話的な操作では 1 操作 1 回だけ確認する。自動取り込みなど非対話の経路は従来どおり DDrive/Lit に変換する
        KeepSource = 1,   // 確認せず、知らないシェーダーはそのシェーダーのまま MaterialData を作る
        ConvertToLit = 2, // 確認せず、従来どおり DDrive/Lit に変換する
    }

    // 方針を 1 操作分に解決した結果(取り込み処理へ明示的に渡す)。
    public enum UnknownShaderHandling
    {
        Convert = 0, // 知らないシェーダーは DDrive/Lit に変換する(従来どおり)。既存 Data のシェーダーも Lit へ寄せる
        Keep = 1,    // 知らないシェーダーはそのまま保つ
        // 2026-10-03 追記(FC-R-01): Convert だが、既存 MaterialData に既に入っている「知らないシェーダー」は Lit に上書きしない
        // (Ask の既定。一度「保つ」を選んだ Data が、後の非対話の処理で Lit に戻らないようにする)。新規作成は従来どおり Lit。
        ConvertKeepingExisting = 2,
    }

    public enum UnknownShaderChoice
    {
        Keep,
        Convert,
        Cancel,
    }

    // 確認ダイアログに出す内容(テストの差し替え口にも渡す)。
    public sealed class UnknownShaderPrompt
    {
        public string Title;
        public string Message;
        public int MaterialCount;
        public List<string> ShaderNames = new();
        // シェーダーが欠けている(Hidden/InternalErrorShader)Material の数。これらは「保つ」の対象ではなく、常に DDrive/Lit に変換する(FC-R-02)。
        public int MissingShaderMaterialCount;
    }

    public static class UnknownShaderGuard
    {
        // テスト用の差し替え口。null なら実ダイアログ(EditorUtility.DisplayDialogComplex)を出す。
        // public(InternalsVisibleTo 未設定のため、NewAssetDialog.TestGameDataRootOverride と同じくテスト asmdef から差し替えられるようにする)。
        // テストは使い終わったら必ず null に戻すこと。
        public static Func<UnknownShaderPrompt, UnknownShaderChoice> PromptOverrideForTests;

        private const int MaxListedShaders = 5;

        // シェーダー参照が欠けた Material(パッケージ未導入・GUID 切れ)の shader。Unity は Hidden/InternalErrorShader を返す
        // (2026-10-03 実機確認、FC-R-02)。常にピンクになるので「保つ」対象にしない。
        public const string MissingShaderName = "Hidden/InternalErrorShader";

        public static bool IsMissing(Shader shader)
            => shader != null && string.Equals(shader.name, MissingShaderName, StringComparison.Ordinal);

        // 知らないシェーダーか。null と、欠けたシェーダー(IsMissing)は対象外(保つものが無い)。
        public static bool IsUnknown(Shader shader)
            => shader != null
               && !IsMissing(shader)
               && !UnityMaterialMigrator.IsSupported(shader)
               && !shader.name.StartsWith("DDrive/", StringComparison.Ordinal);

        // ユーザーが Editor で直接起こした操作(メニュー・ウィンドウのボタン)から呼ぶ側が「対話的」として渡す値。
        // バッチモードではダイアログを出せないので非対話にする。
        public static bool IsInteractiveSession() => !Application.isBatchMode;

        // 非対話の経路での扱い。Ask は Lit に変換するが、既存 Data の知らないシェーダーは上書きしない(ConvertKeepingExisting。FC-R-01)。
        // ConvertToLit は明示の指定なので既存 Data も従来どおり Lit に寄せる。
        public static UnknownShaderHandling HandlingFor(MayaImportProfile profile)
        {
            var policy = profile != null ? profile.UnknownShaderPolicy : UnknownShaderPolicy.Ask;
            switch (policy)
            {
                case UnknownShaderPolicy.KeepSource: return UnknownShaderHandling.Keep;
                case UnknownShaderPolicy.ConvertToLit: return UnknownShaderHandling.Convert;
                default: return UnknownShaderHandling.ConvertKeepingExisting;
            }
        }

        // 1 操作につき 1 回だけ呼ぶ。方針を解決して handling を返す。false = ユーザーがキャンセルした(呼び出し側は何も書き換えずに中断する)。
        // sources は操作の対象になる Material 全部(先に走査して判断を得てから適用する 2 段構えの 1 段目)。
        public static bool TryResolve(MayaImportProfile profile, IEnumerable<UnityEngine.Material> sources, bool interactive,
            out UnknownShaderHandling handling)
        {
            handling = HandlingFor(profile);
            var policy = profile != null ? profile.UnknownShaderPolicy : UnknownShaderPolicy.Ask;
            if (policy != UnknownShaderPolicy.Ask || !interactive || sources == null)
            {
                return true;
            }

            var prompt = BuildPrompt(sources);
            if (prompt == null)
            {
                return true; // 知らないシェーダーが無ければ聞かない
            }

            var choice = (PromptOverrideForTests ?? ShowDialog)(prompt);
            switch (choice)
            {
                case UnknownShaderChoice.Keep:
                    handling = UnknownShaderHandling.Keep;
                    return true;
                case UnknownShaderChoice.Convert:
                    handling = UnknownShaderHandling.Convert;
                    return true;
                default:
                    return false;
            }
        }

        // 知らないシェーダーを使う Material を集めて確認内容を作る。無ければ null。
        public static UnknownShaderPrompt BuildPrompt(IEnumerable<UnityEngine.Material> sources)
        {
            var seen = new HashSet<UnityEngine.Material>();
            var perShader = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var count = 0;
            var missing = 0;
            foreach (var m in sources)
            {
                if (m == null || !seen.Add(m))
                {
                    continue;
                }

                if (IsMissing(m.shader))
                {
                    missing++;
                    continue;
                }

                if (!IsUnknown(m.shader))
                {
                    continue;
                }

                count++;
                var name = m.shader.name;
                perShader.TryGetValue(name, out var n);
                perShader[name] = n + 1;
            }

            if (count == 0)
            {
                return null;
            }

            var prompt = new UnknownShaderPrompt { Title = "知らないシェーダーが見つかりました", MaterialCount = count, MissingShaderMaterialCount = missing };
            var sb = new StringBuilder();
            sb.Append("D-Drive の標準シェーダー(DDrive/Lit・Unlit)に対応づけられないシェーダーを使う Material が ")
              .Append(count).Append(" 件あります。\n\n");
            var listed = 0;
            foreach (var kv in perShader)
            {
                prompt.ShaderNames.Add(kv.Key);
                if (listed < MaxListedShaders)
                {
                    sb.Append("  ・").Append(kv.Key).Append("(").Append(kv.Value).Append(" 件)\n");
                    listed++;
                }
            }

            if (perShader.Count > listed)
            {
                sb.Append("  ほか ").Append(perShader.Count - listed).Append(" 種類\n");
            }

            if (missing > 0)
            {
                sb.Append("\nほかに、シェーダーが見つからない(欠けている)Material が ").Append(missing)
                  .Append(" 件あります。これらはどちらを選んでも DDrive/Lit に変換します。\n");
            }

            sb.Append("\n「元のシェーダーのまま保つ」: そのシェーダーで MaterialData を作ります(既存の MaterialData のシェーダーは変えません)。\n");
            sb.Append("「DDrive/Lit に変換」: 従来どおり DDrive/Lit にします。\n");
            sb.Append("「キャンセル」: 何も変更せず中断します。\n\n");
            sb.Append("この確認を出さず常に同じ扱いにするには、MayaImportProfile の「Unknown Shader Policy」を KeepSource / ConvertToLit にしてください。");
            prompt.Message = sb.ToString();
            return prompt;
        }

        private static UnknownShaderChoice ShowDialog(UnknownShaderPrompt prompt)
        {
            // DisplayDialogComplex の戻り値: 0 = ok、1 = cancel、2 = alt(Esc は 1)。
            var result = EditorUtility.DisplayDialogComplex(prompt.Title, prompt.Message,
                "元のシェーダーのまま保つ", "キャンセル(何もしない)", "DDrive/Lit に変換");
            switch (result)
            {
                case 0: return UnknownShaderChoice.Keep;
                case 2: return UnknownShaderChoice.Convert;
                default: return UnknownShaderChoice.Cancel;
            }
        }
    }
}
