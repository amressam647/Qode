using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalCursor.Services
{
    /// <summary>
    /// Intent Lock - Prevents scope creep during execution.
    /// Locks the plan and rejects out-of-scope operations.
    /// </summary>
    public class IntentLock
    {
        private ExecutionPlan? _lockedPlan;
        private HashSet<string>? _allowedFiles;
        private HashSet<string>? _allowedTools;
        private bool _isLocked;

        public bool IsLocked => _isLocked;
        public string LockedGoal => _lockedPlan?.Goal ?? "";

        /// <summary>
        /// Locks the intent to a specific plan.
        /// </summary>
        public void Lock(ExecutionPlan plan)
        {
            _lockedPlan = plan;
            _allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allowedTools = new HashSet<string>();

            // Extract allowed files and tools from plan
            foreach (var step in plan.Steps)
            {
                // Parse tool type from step description
                if (step.Description.Contains("WRITE_FILE"))
                    _allowedTools.Add("WRITE");
                if (step.Description.Contains("READ_FILE"))
                    _allowedTools.Add("READ");
                if (step.Description.Contains("RUN_CMD"))
                    _allowedTools.Add("CMD");
                if (step.Description.Contains("RUN_PS"))
                    _allowedTools.Add("PS");
                if (step.Description.Contains("DB_"))
                    _allowedTools.Add("DB");
                if (step.Description.Contains("GIT_"))
                    _allowedTools.Add("GIT");

                // Extract file paths mentioned
                var pathMatch = System.Text.RegularExpressions.Regex.Match(
                    step.Description, @"[\w/\\]+\.[\w]+");
                if (pathMatch.Success)
                    _allowedFiles.Add(pathMatch.Value);
            }

            // Always allow safe read operations
            _allowedTools.Add("READ");
            _allowedTools.Add("LIST");
            _allowedTools.Add("DB_QUERY");
            _allowedTools.Add("GIT_STATUS");
            _allowedTools.Add("GIT_DIFF");

            _isLocked = true;
        }

        /// <summary>
        /// Unlocks the intent.
        /// </summary>
        public void Unlock()
        {
            _lockedPlan = null;
            _allowedFiles = null;
            _allowedTools = null;
            _isLocked = false;
        }

        /// <summary>
        /// Validates if an operation is within the locked intent.
        /// </summary>
        public (bool Allowed, string Reason) ValidateOperation(string toolType, string? target = null)
        {
            if (!_isLocked)
                return (true, "No lock active");

            // Check if tool type is allowed
            var toolCategory = GetToolCategory(toolType);
            if (!_allowedTools.Contains(toolType) && !_allowedTools.Contains(toolCategory))
            {
                return (false, $"INTENT_VIOLATION: Tool '{toolType}' not in approved plan. Allowed: {string.Join(", ", _allowedTools)}");
            }

            // For write operations, check if file is in approved list
            if (toolType == "WRITE" && !string.IsNullOrEmpty(target))
            {
                var fileName = System.IO.Path.GetFileName(target);
                if (_allowedFiles.Count > 0 && !_allowedFiles.Any(f => 
                    f.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    f.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, $"INTENT_VIOLATION: File '{target}' not in approved plan. Allowed files: {string.Join(", ", _allowedFiles)}");
                }
            }

            return (true, "Within locked intent");
        }

        /// <summary>
        /// Adds a file to the allowed list dynamically.
        /// Use when plan explicitly mentions creating new files.
        /// </summary>
        public void AllowFile(string filePath)
        {
            _allowedFiles?.Add(filePath);
        }

        /// <summary>
        /// Adds a tool to the allowed list dynamically.
        /// </summary>
        public void AllowTool(string toolType)
        {
            _allowedTools?.Add(toolType);
        }

        /// <summary>
        /// Gets the current intent summary for LLM context.
        /// </summary>
        public string GetIntentContext()
        {
            if (!_isLocked)
                return "[INTENT] No lock active - free execution";

            return $@"[INTENT LOCK ACTIVE]
Goal: {_lockedPlan?.Goal}
Allowed Tools: {string.Join(", ", _allowedTools ?? Enumerable.Empty<string>())}
Allowed Files: {string.Join(", ", _allowedFiles?.Take(5) ?? Enumerable.Empty<string>())}
⚠️ Operations outside this scope will be REJECTED.";
        }

        private string GetToolCategory(string toolType)
        {
            if (toolType.StartsWith("GIT_")) return "GIT";
            if (toolType.StartsWith("DB_")) return "DB";
            return toolType;
        }
    }
}
