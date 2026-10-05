using DDrive.Editor.Menu;
using DDrive.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Validation
{
    // [11_tasks.md] M-4(2026-10-05) — 禁止 API の許可(行単位の許可コメント + 設定の許可リスト)の一覧を
    // Console に出す。レビューで「何を許可しているか」を確認するための入口。CI.ValidateAll と同じ走査ルート・
    // 同じ設定を使う(書き込みはしない)。
    public static class ForbiddenApiAllowMenu
    {
        [MenuItem(DDriveMenu.Validation + "Forbidden API 許可一覧")]
        public static void LogAllowedList()
        {
            var report = ForbiddenApiScanner.ScanDetailed(
                CI.ResolveForbiddenApiScanRoot(), DDriveProjectSettings.instance.ForbiddenApiAllowEntries);

            Debug.Log("[DDrive][ForbiddenApi] " + ForbiddenApiScanner.FormatAllowedList(report));
            foreach (var notice in report.Notices)
            {
                if (notice.Severity == DDrive.Foundation.Validation.ValidationSeverity.Warning)
                {
                    Debug.LogWarning($"[DDrive][ForbiddenApi] {notice.FilePath}:{notice.Line}: {notice.Message} ({notice.Code})");
                }
            }

            if (report.Violations.Count > 0)
            {
                Debug.Log($"[DDrive][ForbiddenApi] 許可されていない禁止 API の当たり: {report.Violations.Count} 件(CI.ValidateAll で Error になります)。一覧は Tools > D-Drive > Validation > 禁止 API の検査 で見られます。");
            }
        }
    }
}
