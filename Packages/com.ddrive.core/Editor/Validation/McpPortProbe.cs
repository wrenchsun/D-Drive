using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DDrive.Editor.Validation
{
    // [1002_ddrive_mcp.md] §6.1 (c) / §6.2 MCP-8 後半(2026-10-07) — Unity MCP(isuzu 版)のポート規則の「読み取り専用の写し」。
    // isuzu(UnityMCP.Editor)の McpPortPolicy / McpInstanceDescriptor は internal なので、DDrive.Editor(isuzu を参照しない)
    // からは同じ規則をここで再現する。DDrive.Editor.Mcp 側(McpInstanceInfo)はここへ委譲する(写しを 2 つ持たない)。
    // 規則が isuzu とずれていないことは Tests/Editor/Mcp/McpPortPolicyTests が実物とリフレクションで突き合わせて検出する。
    // 何も書かない・トークンは読まない。
    public static class McpPortProbe
    {
        public const int RangeStart = 27200;
        public const int RangeEnd = 27999;

        // テスト用: 記述子フォルダの差し替え(null で既定 = %LOCALAPPDATA%/UnityMCP/instances)。
        public static string DescriptorDirectoryOverride;

        // 記述子 JSON から読むもの(トークンは含めない)。0 / null は「記述子に無い」。
        public readonly struct Descriptor
        {
            public readonly int Port;
            public readonly int PreferredPort;
            public readonly int Pid;
            public readonly string ProjectName;

            public Descriptor(int port, int preferredPort, int pid, string projectName)
            {
                Port = port;
                PreferredPort = preferredPort;
                Pid = pid;
                ProjectName = projectName;
            }

            public bool PortMismatch => Port > 0 && PreferredPort > 0 && Port != PreferredPort;
        }

        [Serializable]
        private sealed class DescriptorJson
        {
            public int port;
            public int preferredPort;
            public int pid;
            public string projectName;
        }

        // SHA256(UTF-8(dataPath)) の先頭 8 バイトを小文字 16 進 16 文字(isuzu McpInstanceDescriptor.HashProjectPath、register-mcp.ps1 と同じ)。
        public static string HashProjectPath(string dataPath)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(dataPath ?? string.Empty));
            var sb = new StringBuilder(16);
            for (var i = 0; i < 8; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }

            return sb.ToString();
        }

        // isuzu McpPortPolicy.Derive: 正規化(GetFullPath → \ を / → 末尾 / を除く → Windows は小文字)→ SHA256 先頭 4 バイト(LE)→ 27200 + v % 800。
        public static int DerivePort(string dataPath)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(dataPath)));
            var value = (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));
            return RangeStart + (int)(value % (uint)(RangeEnd - RangeStart + 1));
        }

        public static string Normalize(string path)
        {
            var full = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFullPath(path);
            full = full.Replace('\\', '/').TrimEnd('/');
            if (Path.DirectorySeparatorChar == '\\')
            {
                full = full.ToLowerInvariant();
            }

            return full;
        }

        public static string DescriptorDirectory()
        {
            if (!string.IsNullOrEmpty(DescriptorDirectoryOverride))
            {
                return DescriptorDirectoryOverride;
            }

            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            }

            return Path.Combine(root, "UnityMCP", "instances");
        }

        public static string DescriptorPath(string dataPath) =>
            Path.Combine(DescriptorDirectory(), HashProjectPath(dataPath) + ".json");

        public static bool TryReadDescriptor(string dataPath, out Descriptor descriptor)
        {
            descriptor = default;
            try
            {
                var path = DescriptorPath(dataPath);
                if (!File.Exists(path))
                {
                    return false;
                }

                var json = JsonUtility.FromJson<DescriptorJson>(File.ReadAllText(path));
                if (json == null)
                {
                    return false;
                }

                descriptor = new Descriptor(json.port, json.preferredPort, json.pid, json.projectName);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ポートが Preferences で固定されているとみなせるか: 記述子の preferredPort が、パスから導いたポートと違う。
        // (固定値がたまたま導出ポートと同じなら検出できない。isuzu の設定そのものは UnityMCP.Editor にしか無いため、
        //  ここでは追加の参照を持たず記述子だけで判断する。ddrive_status は McpSettings を直接読む)
        public static bool LooksFixed(string dataPath) =>
            TryReadDescriptor(dataPath, out var d) && d.PreferredPort > 0 && d.PreferredPort != DerivePort(dataPath);
    }
}
