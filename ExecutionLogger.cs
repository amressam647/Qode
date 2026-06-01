using System;
using System.IO;
using System.Text;

namespace LocalCursor.Services
{
    /// <summary>
    /// Logs all agent executions for audit and debugging.
    /// </summary>
    public class ExecutionLogger
    {
        private readonly string _logPath;
        private readonly string _dangerousLogPath;
        private readonly object _lock = new();

        public ExecutionLogger(string workspacePath)
        {
            var logsDir = Path.Combine(workspacePath, ".agent_logs");
            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
            _logPath = Path.Combine(logsDir, $"execution_{timestamp}.log");
            _dangerousLogPath = Path.Combine(logsDir, $"dangerous_actions_{timestamp}.log");
        }

        /// <summary>
        /// Logs a tool execution.
        /// </summary>
        public void LogExecution(string toolType, ToolRiskLevel risk, string args, string result, bool wasBlocked = false)
        {
            var entry = new StringBuilder();
            entry.AppendLine($"[{DateTime.Now:HH:mm:ss}] {(wasBlocked ? "BLOCKED" : "EXECUTED")}");
            entry.AppendLine($"  Tool: {toolType}");
            entry.AppendLine($"  Risk: {risk}");
            entry.AppendLine($"  Args: {Truncate(args, 200)}");
            entry.AppendLine($"  Result: {Truncate(result, 500)}");
            entry.AppendLine();

            lock (_lock)
            {
                File.AppendAllText(_logPath, entry.ToString());

                // Also log dangerous actions separately
                if (risk == ToolRiskLevel.Dangerous || wasBlocked)
                {
                    File.AppendAllText(_dangerousLogPath, entry.ToString());
                }
            }
        }

        /// <summary>
        /// Logs a dangerous action attempt.
        /// </summary>
        public void LogDangerousAction(string toolType, ToolRiskLevel risk, string details)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] ⚠️ DANGEROUS ACTION\n  Tool: {toolType}\n  Risk: {risk}\n  Details: {details}\n\n";

            lock (_lock)
            {
                File.AppendAllText(_dangerousLogPath, entry);
            }
        }

        /// <summary>
        /// Logs a security violation.
        /// </summary>
        public void LogSecurityViolation(string type, string details)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] 🚨 SECURITY VIOLATION\n  Type: {type}\n  Details: {details}\n\n";

            lock (_lock)
            {
                File.AppendAllText(_dangerousLogPath, entry);
            }
        }

        /// <summary>
        /// Logs execution preview (dry-run).
        /// </summary>
        public void LogPreview(string toolType, string args, string preview)
        {
            var previewPath = Path.Combine(Path.GetDirectoryName(_logPath), "execution_preview.md");

            var entry = new StringBuilder();
            entry.AppendLine($"## [{DateTime.Now:HH:mm:ss}] {toolType}");
            entry.AppendLine("```");
            entry.AppendLine(args);
            entry.AppendLine("```");
            entry.AppendLine("### Preview:");
            entry.AppendLine("```diff");
            entry.AppendLine(preview);
            entry.AppendLine("```");
            entry.AppendLine();

            lock (_lock)
            {
                File.AppendAllText(previewPath, entry.ToString());
            }
        }

        /// <summary>
        /// Gets today's execution summary.
        /// </summary>
        public string GetTodaySummary()
        {
            if (!File.Exists(_logPath))
                return "No executions logged today.";

            var lines = File.ReadAllLines(_logPath);
            int executed = 0, blocked = 0;

            foreach (var line in lines)
            {
                if (line.Contains("EXECUTED")) executed++;
                if (line.Contains("BLOCKED")) blocked++;
            }

            return $"Today's Activity:\n  Executed: {executed}\n  Blocked: {blocked}";
        }

        private string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}
