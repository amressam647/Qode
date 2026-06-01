using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    public class MarkdownAuditReporter
    {
        private readonly string _workspacePath;
        private readonly List<string> _entries = new();

        public MarkdownAuditReporter(string workspacePath)
        {
            _workspacePath = workspacePath;
        }

        public void LogEvent(string component, string action, string details, string status = "Success")
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var row = $"| {timestamp} | **{component}** | {action} | {details.Replace("\n", "<br/>").Replace("\r", "")} | `{status}` |";
            _entries.Add(row);
            WriteReport();
        }

        private void WriteReport()
        {
            try
            {
                var filePath = Path.Combine(_workspacePath, "QODE_ACTIVITY_LOG.md");
                var sb = new StringBuilder();

                if (!File.Exists(filePath))
                {
                    sb.AppendLine("# Qode AI Workspace Activity Audit Log");
                    sb.AppendLine("This is an append-only ledger detailing all multi-agent activities, tool executions, audits, and drift detections in this workspace.");
                    sb.AppendLine();
                    sb.AppendLine("| Timestamp | Component | Action | Details | Status |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                }
                else
                {
                    sb.Append(File.ReadAllText(filePath));
                }

                foreach (var entry in _entries)
                {
                    sb.AppendLine(entry);
                }
                _entries.Clear();

                File.WriteAllText(filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write markdown audit report: {ex.Message}");
            }
        }
    }
}
