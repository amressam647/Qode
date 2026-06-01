using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalCursor.Services
{
    /// <summary>
    /// Failure pattern memory to avoid repeating the same mistakes.
    /// Learns from errors and suggests fixes.
    /// </summary>
    public class FailureMemory
    {
        private readonly string _patternsPath;
        private Dictionary<string, FailurePattern> _patterns = new();

        public FailureMemory(string workspacePath)
        {
            var memoryDir = Path.Combine(workspacePath, ".agent_memory");
            if (!Directory.Exists(memoryDir))
                Directory.CreateDirectory(memoryDir);

            _patternsPath = Path.Combine(memoryDir, "failure_patterns.json");
            Load();
        }

        /// <summary>
        /// Records a failure and how it was fixed.
        /// </summary>
        public void RecordFailure(string errorMessage, string context, string fixApplied = null)
        {
            var hash = ComputeErrorHash(errorMessage);

            if (_patterns.TryGetValue(hash, out var existing))
            {
                existing.OccurrenceCount++;
                existing.LastSeen = DateTime.Now;
                if (!string.IsNullOrEmpty(fixApplied))
                {
                    existing.Fixes.Add(fixApplied);
                    existing.FixSuccessCount++;
                }
            }
            else
            {
                _patterns[hash] = new FailurePattern
                {
                    Hash = hash,
                    ErrorMessage = TruncateError(errorMessage),
                    Context = context,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now,
                    OccurrenceCount = 1,
                    Fixes = new List<string>()
                };
                
                if (!string.IsNullOrEmpty(fixApplied))
                    _patterns[hash].Fixes.Add(fixApplied);
            }

            Save();
        }

        /// <summary>
        /// Checks if we've seen this error before and returns suggested fix.
        /// </summary>
        public (bool Seen, string SuggestedFix, int PreviousOccurrences) CheckError(string errorMessage)
        {
            var hash = ComputeErrorHash(errorMessage);

            if (_patterns.TryGetValue(hash, out var pattern))
            {
                var suggestedFix = pattern.Fixes.Count > 0 
                    ? pattern.Fixes[^1] // Most recent fix
                    : null;

                return (true, suggestedFix, pattern.OccurrenceCount);
            }

            return (false, null, 0);
        }

        /// <summary>
        /// Gets context for LLM about known failures.
        /// </summary>
        public string GetFailureContext()
        {
            if (_patterns.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("=== KNOWN FAILURE PATTERNS ===");

            foreach (var pattern in _patterns.Values)
            {
                if (pattern.OccurrenceCount > 1) // Only show recurring issues
                {
                    sb.AppendLine($"- {pattern.ErrorMessage} (seen {pattern.OccurrenceCount}x)");
                    if (pattern.Fixes.Count > 0)
                        sb.AppendLine($"  Fix: {pattern.Fixes[^1]}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Detects if agent is in a failure loop (same error 3+ times in short period).
        /// </summary>
        public bool IsInFailureLoop(string errorMessage)
        {
            var hash = ComputeErrorHash(errorMessage);

            if (_patterns.TryGetValue(hash, out var pattern))
            {
                // Check if error occurred more than 3 times in last 5 minutes
                if (pattern.OccurrenceCount >= 3 && 
                    (DateTime.Now - pattern.LastSeen).TotalMinutes < 5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets statistics about failures.
        /// </summary>
        public FailureStats GetStats()
        {
            int total = 0, fixedCount = 0, recurring = 0;

            foreach (var pattern in _patterns.Values)
            {
                total += pattern.OccurrenceCount;
                fixedCount += pattern.FixSuccessCount;
                if (pattern.OccurrenceCount > 1) recurring++;
            }

            return new FailureStats
            {
                TotalFailures = total,
                FixedCount = fixedCount,
                RecurringPatterns = recurring,
                UniquePatterns = _patterns.Count
            };
        }

        private string ComputeErrorHash(string errorMessage)
        {
            // Normalize error message (remove line numbers, paths, etc.)
            var normalized = System.Text.RegularExpressions.Regex.Replace(
                errorMessage, @"\d+", "#");
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, @"[A-Za-z]:\\[^\s]+", "[PATH]");

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).Substring(0, 16);
        }

        private string TruncateError(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";
            return error.Length > 200 ? error.Substring(0, 200) + "..." : error;
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_patternsPath))
                {
                    var json = File.ReadAllText(_patternsPath);
                    _patterns = JsonSerializer.Deserialize<Dictionary<string, FailurePattern>>(json)
                                ?? new Dictionary<string, FailurePattern>();
                }
            }
            catch { _patterns = new Dictionary<string, FailurePattern>(); }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_patterns, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_patternsPath, json);
            }
            catch { }
        }

        public class FailurePattern
        {
            public string Hash { get; set; }
            public string ErrorMessage { get; set; }
            public string Context { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public int OccurrenceCount { get; set; }
            public int FixSuccessCount { get; set; }
            public List<string> Fixes { get; set; } = new();
        }

        public class FailureStats
        {
            public int TotalFailures { get; set; }
            public int FixedCount { get; set; }
            public int RecurringPatterns { get; set; }
            public int UniquePatterns { get; set; }
        }
    }
}
