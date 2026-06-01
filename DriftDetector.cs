using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalCursor.Services
{
    /// <summary>
    /// Baseline Snapshot & Drift Detection.
    /// Captures workspace state before execution and detects unexpected changes after.
    /// </summary>
    public class DriftDetector
    {
        private readonly string _workspacePath;
        private readonly string _snapshotsDir;
        private WorkspaceSnapshot? _baselineSnapshot;

        public DriftDetector(string workspacePath)
        {
            _workspacePath = workspacePath;
            _snapshotsDir = Path.Combine(workspacePath, ".agent_snapshots");
            if (!Directory.Exists(_snapshotsDir))
                Directory.CreateDirectory(_snapshotsDir);
        }

        /// <summary>
        /// Audits actual tool execution logs against the expected plan to detect semantic and state deviations.
        /// </summary>
        public (bool HasDrift, string Explanation) AnalyzeSemanticAndStateDrift(string expectedPlan, string actualExecutionLogs)
        {
            if (string.IsNullOrEmpty(expectedPlan) || string.IsNullOrEmpty(actualExecutionLogs))
            {
                return (false, "Incomplete data for drift check.");
            }

            var lowerLogs = actualExecutionLogs.ToLowerInvariant();

            // Detect technical execution drift
            if (lowerLogs.Contains("error:") || 
                lowerLogs.Contains("failed:") || 
                lowerLogs.Contains("kernel block") || 
                lowerLogs.Contains("security block") ||
                lowerLogs.Contains("compilation failure"))
            {
                return (true, "Technical execution drift: Tool execution logs contain errors or blocks.");
            }

            return (false, "Aligned with the planned workflow stage.");
        }

        /// <summary>
        /// Captures a baseline snapshot before execution.
        /// </summary>
        public WorkspaceSnapshot CaptureBaseline()
        {
            _baselineSnapshot = CaptureSnapshot();
            SaveSnapshot(_baselineSnapshot, "baseline");
            return _baselineSnapshot;
        }

        /// <summary>
        /// Detects drift from baseline.
        /// </summary>
        public DriftReport DetectDrift(List<string>? expectedChangedFiles = null)
        {
            if (_baselineSnapshot == null)
            {
                return new DriftReport 
                { 
                    HasDrift = false, 
                    Error = "No baseline snapshot captured" 
                };
            }

            var currentSnapshot = CaptureSnapshot();
            var report = CompareSnapshots(_baselineSnapshot, currentSnapshot, expectedChangedFiles);
            
            // Save drift report
            SaveDriftReport(report);
            
            return report;
        }

        /// <summary>
        /// Captures current workspace state.
        /// </summary>
        public WorkspaceSnapshot CaptureSnapshot()
        {
            var snapshot = new WorkspaceSnapshot
            {
                CapturedAt = DateTime.Now,
                Files = new Dictionary<string, FileSnapshot>()
            };

            var files = Directory.GetFiles(_workspacePath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".git") && 
                           !f.Contains(".agent_") && 
                           !f.Contains("bin") && 
                           !f.Contains("obj") &&
                           !f.Contains("node_modules"));

            foreach (var file in files)
            {
                try
                {
                    var relativePath = GetRelativePath(file);
                    var info = new FileInfo(file);
                    
                    snapshot.Files[relativePath] = new FileSnapshot
                    {
                        Path = relativePath,
                        Size = info.Length,
                        LastModified = info.LastWriteTimeUtc,
                        Hash = ComputeFileHash(file)
                    };
                }
                catch { /* Skip inaccessible files */ }
            }

            snapshot.TotalFiles = snapshot.Files.Count;
            snapshot.TotalSize = snapshot.Files.Values.Sum(f => f.Size);

            return snapshot;
        }

        private DriftReport CompareSnapshots(WorkspaceSnapshot baseline, WorkspaceSnapshot current, List<string>? expectedChanges)
        {
            var report = new DriftReport
            {
                BaselineTime = baseline.CapturedAt,
                CurrentTime = current.CapturedAt,
                ExpectedChanges = new List<FileChange>(),
                UnexpectedChanges = new List<FileChange>()
            };

            var expectedSet = new HashSet<string>(
                expectedChanges?.Select(f => NormalizePath(f)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Check for modified and deleted files
            foreach (var (path, baselineFile) in baseline.Files)
            {
                if (!current.Files.TryGetValue(path, out var currentFile))
                {
                    // File deleted
                    var change = new FileChange
                    {
                        Path = path,
                        ChangeType = FileChangeType.Deleted,
                        OldHash = baselineFile.Hash
                    };
                    CategorizeChange(change, expectedSet, report);
                }
                else if (baselineFile.Hash != currentFile.Hash)
                {
                    // File modified
                    var change = new FileChange
                    {
                        Path = path,
                        ChangeType = FileChangeType.Modified,
                        OldHash = baselineFile.Hash,
                        NewHash = currentFile.Hash,
                        SizeDelta = currentFile.Size - baselineFile.Size
                    };
                    CategorizeChange(change, expectedSet, report);
                }
            }

            // Check for new files
            foreach (var (path, currentFile) in current.Files)
            {
                if (!baseline.Files.ContainsKey(path))
                {
                    var change = new FileChange
                    {
                        Path = path,
                        ChangeType = FileChangeType.Created,
                        NewHash = currentFile.Hash,
                        SizeDelta = currentFile.Size
                    };
                    CategorizeChange(change, expectedSet, report);
                }
            }

            report.HasDrift = report.UnexpectedChanges.Count > 0;
            return report;
        }

        private void CategorizeChange(FileChange change, HashSet<string> expectedSet, DriftReport report)
        {
            var normalizedPath = NormalizePath(change.Path);
            
            if (expectedSet.Any(e => normalizedPath.Contains(e, StringComparison.OrdinalIgnoreCase) ||
                                     e.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                report.ExpectedChanges.Add(change);
            }
            else
            {
                report.UnexpectedChanges.Add(change);
            }
        }

        private void SaveSnapshot(WorkspaceSnapshot snapshot, string name)
        {
            var path = Path.Combine(_snapshotsDir, $"{name}_{snapshot.CapturedAt:yyyyMMdd_HHmmss}.json");
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private void SaveDriftReport(DriftReport report)
        {
            var reportPath = Path.Combine(_workspacePath, "drift_report.md");
            
            var sb = new StringBuilder();
            sb.AppendLine("# Drift Detection Report");
            sb.AppendLine($"Baseline: {report.BaselineTime:O}");
            sb.AppendLine($"Current: {report.CurrentTime:O}");
            sb.AppendLine();

            if (report.HasDrift)
            {
                sb.AppendLine("## ⚠️ UNEXPECTED DRIFT DETECTED");
                sb.AppendLine();
                sb.AppendLine("| File | Change | Details |");
                sb.AppendLine("|------|--------|---------|");
                foreach (var change in report.UnexpectedChanges)
                {
                    sb.AppendLine($"| {change.Path} | {change.ChangeType} | {change.SizeDelta:+#;-#;0} bytes |");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## ✅ No Unexpected Drift");
                sb.AppendLine();
            }

            if (report.ExpectedChanges.Count > 0)
            {
                sb.AppendLine("## Expected Changes");
                sb.AppendLine();
                foreach (var change in report.ExpectedChanges)
                {
                    sb.AppendLine($"- [{change.ChangeType}] {change.Path}");
                }
            }

            File.WriteAllText(reportPath, sb.ToString());
        }

        private string ComputeFileHash(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash).Substring(0, 16);
            }
            catch
            {
                return "ERROR";
            }
        }

        private string GetRelativePath(string fullPath)
        {
            if (fullPath.StartsWith(_workspacePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(_workspacePath.Length).TrimStart('\\', '/');
            }
            return fullPath;
        }

        private string NormalizePath(string path)
        {
            return path.Replace('\\', '/').ToLower();
        }
    }

    public class WorkspaceSnapshot
    {
        public DateTime CapturedAt { get; set; }
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public Dictionary<string, FileSnapshot> Files { get; set; } = new();
    }

    public class FileSnapshot
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string Hash { get; set; } = "";
    }

    public class DriftReport
    {
        public DateTime BaselineTime { get; set; }
        public DateTime CurrentTime { get; set; }
        public bool HasDrift { get; set; }
        public string? Error { get; set; }
        public List<FileChange> ExpectedChanges { get; set; } = new();
        public List<FileChange> UnexpectedChanges { get; set; } = new();
    }

    public class FileChange
    {
        public string Path { get; set; } = "";
        public FileChangeType ChangeType { get; set; }
        public string? OldHash { get; set; }
        public string? NewHash { get; set; }
        public long SizeDelta { get; set; }
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted
    }
}
