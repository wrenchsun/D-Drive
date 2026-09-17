using System.IO;
using System.IO.Compression;
using DDrive.Editor.Menu;
using UnityEditor;
using UnityEngine;

namespace DDrive.Editor.Build
{
    // [11_tasks.md] 6-0(E) — 実機確認用 Windows 開発ビルド。EditorBuildSettings.scenes を恒久的に
    // 書き換えず(BuildPlayerOptions.scenes で明示的に渡す)、出力後に zip 化までを 1 手順で行う。
    // isuzu MCP の build_player やその他のツールから叩けるよう、メニュー本体は static メソッドに分離してある。
    public static class NetCheckBuilder
    {
        // Assets 配下ではなくプロジェクト直下(.gitignore 済み、[docs/29])。
        public const string OutputDirectory = "Builds/DDriveNetCheck";
        public const string ExecutableName = "DDriveNetCheck.exe";
        public const string ZipPath = "Builds/DDriveNetCheck.zip";

        public const string NetCheckScenePath = "Assets/GameData/PreviewScenes/NetCheckScene.unity";

        [MenuItem(DDriveMenu.Build + "実機確認用 Windows 開発ビルド")]
        public static void BuildFromMenu()
        {
            var result = Build();
            ShowResultDialog(result);
        }

        // [11_tasks.md] 6-5 — docs/29 §14「リリースビルド相当(切断)の確認について」の要判断対応
        // (2026-09-18)。既定は従来どおり開発ビルド(BuildFromMenu / Build() 引数省略)のままにし、
        // このメニューだけを新設して呼び出しを分ける。出力先は既定の Builds/DDriveNetCheck のままで、
        // 個別の出力先(v8_release_normal 等)を使うビルドは isuzu MCP の execute_code から
        // Build(outputDirectory: ..., development: false) を直接呼ぶ想定。
        [MenuItem(DDriveMenu.Build + "実機確認用 Windows リリース相当ビルド")]
        public static void BuildReleaseFromMenu()
        {
            var result = Build(development: false);
            ShowResultDialog(result);
        }

        private static void ShowResultDialog(BuildResult result)
        {
            if (result.Success)
            {
                EditorUtility.DisplayDialog("D-Drive", $"ビルド完了: {result.ExecutablePath}\nzip: {result.ZipPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("D-Drive", $"ビルド失敗: {result.Error}", "OK");
            }
        }

        public struct BuildResult
        {
            public bool Success;
            public string ExecutablePath;
            public string ZipPath;
            public string Error;
        }

        // isuzu MCP の execute_code や他ツールからもそのまま呼べる、ダイアログを出さない版。
        // development: true(既定)なら従来どおり BuildOptions.Development(Debug.isDebugBuild=true、
        // CatalogContentHashGate は不一致時に警告のみで継続)。false なら通常のリリース相当ビルド
        // (BuildOptions.None、Debug.isDebugBuild=false、不一致時は Host が切断する側の経路になる。
        // [docs/29_network_device_test.md] §14「リリースビルド相当(切断)の確認について」参照)。
        // zip の出力先は outputDirectory から自動導出する(例: Builds/v8_release_normal →
        // Builds/v8_release_normal.zip)。既定の OutputDirectory/ZipPath の組み合わせもこの規則に沿っている。
        public static BuildResult Build(string outputDirectory = OutputDirectory, bool zip = true, bool development = true)
        {
            if (!File.Exists(NetCheckScenePath))
            {
                return new BuildResult { Success = false, Error = $"確認用シーンが見つかりません: {NetCheckScenePath}" };
            }

            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var absoluteOutputDir = Path.Combine(projectRoot, outputDirectory);
            Directory.CreateDirectory(absoluteOutputDir);
            var executablePath = Path.Combine(absoluteOutputDir, ExecutableName);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { NetCheckScenePath },
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                return new BuildResult { Success = false, Error = $"BuildPipeline.BuildPlayer failed: result={summary.result}, errors={summary.totalErrors}" };
            }

            var zipPath = Path.Combine(projectRoot, outputDirectory + ".zip");
            if (zip)
            {
                CreateZip(absoluteOutputDir, zipPath);
            }

            Debug.Log($"[DDrive] NetCheckBuilder: ビルド完了 {executablePath}" + (zip ? $" / zip: {zipPath}" : string.Empty)
                + (development ? string.Empty : " / release相当(BuildOptions.None)"));
            return new BuildResult { Success = true, ExecutablePath = executablePath, ZipPath = zip ? zipPath : null };
        }

        // System.IO.Compression.FileSystem(ZipFile.CreateFromDirectory)は asmdef の precompiledReferences
        // 追加が必要になる可能性があるため使わず、System.IO.Compression.dll だけで完結する ZipArchive で
        // 手動にディレクトリを圧縮する(asmdef 変更を避ける、[CLAUDE.md] TL;DR 9 と同じ判断)。
        private static void CreateZip(string sourceDir, string zipPath)
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using var zipStream = new FileStream(zipPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

            foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                fileStream.CopyTo(entryStream);
            }
        }
    }
}
