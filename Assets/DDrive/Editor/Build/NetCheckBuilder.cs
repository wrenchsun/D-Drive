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
        public static BuildResult Build(string outputDirectory = OutputDirectory, bool zip = true)
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
                options = BuildOptions.Development,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                return new BuildResult { Success = false, Error = $"BuildPipeline.BuildPlayer failed: result={summary.result}, errors={summary.totalErrors}" };
            }

            var zipPath = Path.Combine(projectRoot, ZipPath);
            if (zip)
            {
                CreateZip(absoluteOutputDir, zipPath);
            }

            Debug.Log($"[DDrive] NetCheckBuilder: ビルド完了 {executablePath}" + (zip ? $" / zip: {zipPath}" : string.Empty));
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
