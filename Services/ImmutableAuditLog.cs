using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LocalCursor.Services
{
    /// <summary>
    /// Immutable, hash-chained audit log for compliance and forensics.
    /// Append-only with integrity verification.
    /// </summary>
    public class ImmutableAuditLog
    {
        private readonly string _logPath;
        private readonly string _hashPath;
        private string _previousHash;
        private readonly object _lock = new();

        public ImmutableAuditLog(string workspacePath)
        {
            var auditDir = Path.Combine(workspacePath, ".agent_audit");
            if (!Directory.Exists(auditDir))
                Directory.CreateDirectory(auditDir);

            _logPath = Path.Combine(auditDir, "audit.log");
            _hashPath = Path.Combine(auditDir, "audit.hash");

            // Load previous hash
            _previousHash = File.Exists(_hashPath) 
                ? File.ReadAllText(_hashPath).Trim() 
                : "GENESIS";
        }

        /// <summary>
        /// Appends an audit entry with hash chain.
        /// </summary>
        public void Log(AuditEntry entry)
        {
            lock (_lock)
            {
                entry.Timestamp = DateTime.UtcNow;
                entry.PreviousHash = _previousHash;

                // Compute hash of this entry
                var entryJson = System.Text.Json.JsonSerializer.Serialize(entry);
                var entryHash = ComputeHash(entryJson + _previousHash);
                entry.EntryHash = entryHash;

                // Format: ISO_TIMESTAMP|HASH|TYPE|USER|ACTION|TARGET|DETAILS
                var logLine = $"{entry.Timestamp:O}|{entry.EntryHash}|{entry.Type}|{entry.User}|{entry.Action}|{entry.Target}|{Sanitize(entry.Details)}";

                // Append to log (append-only)
                File.AppendAllText(_logPath, logLine + "\n");

                // Update hash chain
                _previousHash = entryHash;
                File.WriteAllText(_hashPath, entryHash);
            }
        }

        /// <summary>
        /// Verifies the integrity of the audit log.
        /// </summary>
        public (bool IsValid, int ValidEntries, int TamperedEntries, string FirstTamperedHash) VerifyIntegrity()
        {
            if (!File.Exists(_logPath))
                return (true, 0, 0, null);

            var lines = File.ReadAllLines(_logPath);
            string previousHash = "GENESIS";
            int valid = 0, tampered = 0;
            string firstTampered = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('|');
                if (parts.Length < 7)
                {
                    tampered++;
                    firstTampered ??= "MALFORMED_LINE";
                    continue;
                }

                var recordedHash = parts[1];
                var entry = new AuditEntry
                {
                    Timestamp = DateTime.Parse(parts[0]),
                    Type = parts[2],
                    User = parts[3],
                    Action = parts[4],
                    Target = parts[5],
                    Details = parts[6],
                    PreviousHash = previousHash
                };

                var entryJson = System.Text.Json.JsonSerializer.Serialize(entry);
                var computedHash = ComputeHash(entryJson + previousHash);

                if (computedHash == recordedHash)
                {
                    valid++;
                    previousHash = recordedHash;
                }
                else
                {
                    tampered++;
                    firstTampered ??= recordedHash;
                }
            }

            return (tampered == 0, valid, tampered, firstTampered);
        }

        /// <summary>
        /// Logs a tool execution.
        /// </summary>
        public void LogToolExecution(string toolType, string target, string result, bool wasBlocked = false)
        {
            Log(new AuditEntry
            {
                Type = wasBlocked ? "BLOCKED" : "TOOL_EXEC",
                User = "AGENT",
                Action = toolType,
                Target = target,
                Details = result?.Length > 100 ? result.Substring(0, 100) + "..." : result
            });
        }

        /// <summary>
        /// Logs a security event.
        /// </summary>
        public void LogSecurityEvent(string eventType, string details)
        {
            Log(new AuditEntry
            {
                Type = "SECURITY",
                User = "SYSTEM",
                Action = eventType,
                Target = "",
                Details = details
            });
        }

        /// <summary>
        /// Logs a configuration change.
        /// </summary>
        public void LogConfigChange(string setting, string oldValue, string newValue)
        {
            Log(new AuditEntry
            {
                Type = "CONFIG",
                User = "ADMIN",
                Action = "CHANGE",
                Target = setting,
                Details = $"{oldValue} -> {newValue}"
            });
        }

        /// <summary>
        /// Exports audit log for compliance.
        /// </summary>
        public string Export(DateTime? from = null, DateTime? to = null)
        {
            if (!File.Exists(_logPath))
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("# Audit Log Export");
            sb.AppendLine($"Generated: {DateTime.UtcNow:O}");
            sb.AppendLine($"Period: {from?.ToString("O") ?? "Beginning"} to {to?.ToString("O") ?? "Now"}");
            sb.AppendLine();
            sb.AppendLine("```");

            foreach (var line in File.ReadLines(_logPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var parts = line.Split('|');
                if (parts.Length >= 7)
                {
                    var timestamp = DateTime.Parse(parts[0]);
                    if (from.HasValue && timestamp < from.Value) continue;
                    if (to.HasValue && timestamp > to.Value) continue;
                    
                    sb.AppendLine(line);
                }
            }

            sb.AppendLine("```");
            return sb.ToString();
        }

        private string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).Substring(0, 16);
        }

        private string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("|", "[PIPE]").Replace("\n", "[NL]").Replace("\r", "");
        }
    }

    public class AuditEntry
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
        public string User { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public string Details { get; set; }
        public string PreviousHash { get; set; }
        public string EntryHash { get; set; }
    }
}
