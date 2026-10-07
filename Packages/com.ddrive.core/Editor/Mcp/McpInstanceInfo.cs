using DDrive.Editor.Validation;

namespace DDrive.Editor.Mcp
{
    // [1002_ddrive_mcp.md] §6.2 MCP-8 後半(2026-10-07) — isuzu のポート/記述子規則の入口。
    // 計算そのものは DDrive.Editor の McpPortProbe(ProjectSetupValidator が isuzu を参照せずに使える)にあり、ここは委譲するだけ。
    // トークンは読まない・返さない。
    public static class McpInstanceInfo
    {
        public static string HashProjectPath(string dataPath) => McpPortProbe.HashProjectPath(dataPath);

        public static int DerivePort(string dataPath) => McpPortProbe.DerivePort(dataPath);

        public static string DescriptorPath(string dataPath) => McpPortProbe.DescriptorPath(dataPath);

        // 記述子が読めれば true。port / preferredPort / pid は記述子に無ければ null。
        public static bool TryReadDescriptor(
            string dataPath, out int? port, out int? preferredPort, out bool portMismatch, out int? pid, out string projectName)
        {
            port = null;
            preferredPort = null;
            pid = null;
            projectName = null;
            portMismatch = false;
            if (!McpPortProbe.TryReadDescriptor(dataPath, out var d))
            {
                return false;
            }

            port = d.Port > 0 ? d.Port : (int?)null;
            preferredPort = d.PreferredPort > 0 ? d.PreferredPort : (int?)null;
            pid = d.Pid > 0 ? d.Pid : (int?)null;
            projectName = string.IsNullOrEmpty(d.ProjectName) ? null : d.ProjectName;
            portMismatch = d.PortMismatch;
            return true;
        }
    }
}
