using System;
using System.Collections.Generic;
using System.Text;

namespace LocalCursor.Services
{
    public class DiffService
    {
        /// <summary>
        /// Generates a unified diff between old and new content.
        /// </summary>
        public string GenerateDiff(string oldContent, string newContent, string fileName = "file")
        {
            var oldLines = oldContent?.Split('\n') ?? Array.Empty<string>();
            var newLines = newContent?.Split('\n') ?? Array.Empty<string>();

            var sb = new StringBuilder();
            sb.AppendLine($"--- a/{fileName}");
            sb.AppendLine($"+++ b/{fileName}");

            var diff = ComputeDiff(oldLines, newLines);
            int oldStart = 1, newStart = 1;
            var hunk = new List<string>();

            for (int i = 0; i < diff.Count; i++)
            {
                var (type, line) = diff[i];
                switch (type)
                {
                    case DiffType.Unchanged:
                        hunk.Add($" {line}");
                        break;
                    case DiffType.Removed:
                        hunk.Add($"-{line}");
                        break;
                    case DiffType.Added:
                        hunk.Add($"+{line}");
                        break;
                }
            }

            if (hunk.Count > 0)
            {
                sb.AppendLine($"@@ -{oldStart},{oldLines.Length} +{newStart},{newLines.Length} @@");
                foreach (var line in hunk)
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a summary of changes (lines added, removed, modified).
        /// </summary>
        public (int Added, int Removed, int Unchanged) GetDiffStats(string oldContent, string newContent)
        {
            var oldLines = oldContent?.Split('\n') ?? Array.Empty<string>();
            var newLines = newContent?.Split('\n') ?? Array.Empty<string>();

            var diff = ComputeDiff(oldLines, newLines);
            int added = 0, removed = 0, unchanged = 0;

            foreach (var (type, _) in diff)
            {
                switch (type)
                {
                    case DiffType.Added: added++; break;
                    case DiffType.Removed: removed++; break;
                    case DiffType.Unchanged: unchanged++; break;
                }
            }

            return (added, removed, unchanged);
        }

        /// <summary>
        /// Simple LCS-based diff algorithm.
        /// </summary>
        private List<(DiffType, string)> ComputeDiff(string[] oldLines, string[] newLines)
        {
            var result = new List<(DiffType, string)>();
            int oldIdx = 0, newIdx = 0;

            // Simple line-by-line comparison (could use LCS for better results)
            while (oldIdx < oldLines.Length || newIdx < newLines.Length)
            {
                if (oldIdx >= oldLines.Length)
                {
                    result.Add((DiffType.Added, newLines[newIdx++]));
                }
                else if (newIdx >= newLines.Length)
                {
                    result.Add((DiffType.Removed, oldLines[oldIdx++]));
                }
                else if (oldLines[oldIdx].TrimEnd('\r') == newLines[newIdx].TrimEnd('\r'))
                {
                    result.Add((DiffType.Unchanged, oldLines[oldIdx]));
                    oldIdx++;
                    newIdx++;
                }
                else
                {
                    // Look ahead to find matching line
                    int lookAhead = FindNextMatch(oldLines, newLines, oldIdx, newIdx, 5);
                    if (lookAhead > 0)
                    {
                        // Add new lines first
                        for (int i = 0; i < lookAhead; i++)
                        {
                            result.Add((DiffType.Added, newLines[newIdx++]));
                        }
                    }
                    else
                    {
                        // Remove old line
                        result.Add((DiffType.Removed, oldLines[oldIdx++]));
                    }
                }
            }

            return result;
        }

        private int FindNextMatch(string[] oldLines, string[] newLines, int oldIdx, int newIdx, int maxLookAhead)
        {
            for (int i = 1; i <= maxLookAhead && newIdx + i < newLines.Length; i++)
            {
                if (oldLines[oldIdx].TrimEnd('\r') == newLines[newIdx + i].TrimEnd('\r'))
                {
                    return i;
                }
            }
            return 0;
        }

        private enum DiffType
        {
            Unchanged,
            Added,
            Removed
        }
    }
}
