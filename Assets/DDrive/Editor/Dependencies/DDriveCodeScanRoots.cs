using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

namespace DDrive.Editor.Dependencies
{
    // [42_distribution.md] §2.3-5(P-4、2026-09-20) — CodeReferenceScan(生成 ID 定数のコード参照チェック)と
    // SpecWebSender(TUNING 定数の未使用検出)が共通で使う「コードを走査する対象フォルダ」。
    // 以前はそれぞれが個別に Assets/DDrive・Assets/Generated だけを見ていたため、持ち込み先の
    // ゲームコード(例: Assets/_Project/Scripts)を一切見ておらず、「安全な削除」チェックが使用中の
    // ID を見逃す事故があり得た([42] §2.3 #5)。
    //
    // 対処: Assets 配下を丸ごと走査対象にする(Library・Packages は Application.dataPath の外なので
    // 自動的に対象外)。D-Drive 自身がパッケージ化(P-5)されて Packages/com.ddrive.core/ に移った後は
    // Application.dataPath 側の走査から漏れるため、PackageInfo で解決した自分自身のパスを個別に足す
    // (Packages 配下を丸ごと走査すると外部依存パッケージまで含んでしまうため、これはしない)。
    //
    // パフォーマンス注意(2026-09-14 のレビュー対応の裏返し): Assets 全体走査は当時「重い」として
    // 意図的に絞った経緯がある。今回は正しさ(ゲームコードの参照漏れを防ぐ)を優先してこの絞り込みを
    // 戻すが、呼び出し元は「削除確認ダイアログを開いたとき」等のオンデマンド呼び出しのみで、
    // 毎フレーム走るホットパスではない。呼び出し側のファイルキャッシュ(更新時刻キー)で同一セッション内の
    // 再読み込みコストは抑えている。将来的に重さが問題になれば、大きい非コードフォルダの除外設定を追加する
    // 余地がある(§2.3 #5 の対処案「大きいフォルダは除外設定可」)。
    public static class DDriveCodeScanRoots
    {
        public static IReadOnlyList<string> ResolveAbsoluteRoots()
        {
            var roots = new List<string> { Application.dataPath };

            var packageInfo = PackageInfo.FindForAssembly(typeof(DDriveCodeScanRoots).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath) && Directory.Exists(packageInfo.resolvedPath))
            {
                roots.Add(packageInfo.resolvedPath);
            }

            return roots;
        }
    }
}
