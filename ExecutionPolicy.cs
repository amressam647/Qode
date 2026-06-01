using System;
using System.Collections.Generic;

namespace LocalCursor.Services
{
    public enum ToolRiskLevel
    {
        Safe,       // Read-only operations
        Medium,     // Write operations with reversible effects
        Dangerous   // System commands, DB modifications, file deletions
    }

    public enum PolicyExecutionMode
    {
        Autonomous,      // Execute everything, log all
        SemiAutonomous,  // Block Dangerous, allow Medium with logging
        SafeOnly         // Only Safe operations allowed
    }

    public class ExecutionPolicy
    {
        private static readonly Dictionary<string, ToolRiskLevel> ToolRiskMap = new()
        {
            // Safe - Read Only
            { "READ", ToolRiskLevel.Safe },
            { "LIST", ToolRiskLevel.Safe },
            { "DOC", ToolRiskLevel.Safe },
            { "TAIL", ToolRiskLevel.Safe },
            { "DB_SCHEMA", ToolRiskLevel.Safe },
            { "DB_TABLES", ToolRiskLevel.Safe },
            { "DB_QUERY", ToolRiskLevel.Safe },
            { "GIT_STATUS", ToolRiskLevel.Safe },
            { "GIT_DIFF", ToolRiskLevel.Safe },
            { "GIT_DIFF_STAGED", ToolRiskLevel.Safe },
            { "GIT_LOG", ToolRiskLevel.Safe },
            { "GIT_BRANCH", ToolRiskLevel.Safe },
            { "DIFF_PREVIEW", ToolRiskLevel.Safe },
            { "RUN_BUILD", ToolRiskLevel.Safe },  // Build/run commands - always allowed
            { "WEB_SEARCH", ToolRiskLevel.Safe }, // Web search - read-only, no API key

            // Medium - Write with reversible effects (workspace only)
            { "WRITE", ToolRiskLevel.Medium },
            { "CREATE_FOLDER", ToolRiskLevel.Medium },
            { "DELETE_FILE", ToolRiskLevel.Medium },
            { "DELETE_FOLDER", ToolRiskLevel.Medium },
            { "GIT_ADD", ToolRiskLevel.Medium },
            { "GIT_COMMIT", ToolRiskLevel.Medium },
            { "GIT_CHECKOUT", ToolRiskLevel.Medium },
            { "GIT_STASH", ToolRiskLevel.Medium },

            // Dangerous - System/DB modifications
            { "CMD", ToolRiskLevel.Dangerous },
            { "PS", ToolRiskLevel.Dangerous },
            { "DB_CONNECT", ToolRiskLevel.Medium },
            { "DB_EXECUTE", ToolRiskLevel.Dangerous },
            { "GIT_PUSH", ToolRiskLevel.Dangerous },
            { "GIT_PULL", ToolRiskLevel.Medium }
        };

        public PolicyExecutionMode CurrentMode { get; set; } = PolicyExecutionMode.SemiAutonomous;

        public event Action<string, ToolRiskLevel, string> OnDangerousAction;

        /// <summary>
        /// Checks if a tool can be executed under the current policy.
        /// </summary>
        public (bool Allowed, string Reason) CanExecute(string toolType, string command = null)
        {
            var risk = GetRiskLevel(toolType);
            var cmd = (command ?? "").Trim().ToLower();

            switch (CurrentMode)
            {
                case PolicyExecutionMode.Autonomous:
                    if (risk == ToolRiskLevel.Dangerous)
                        OnDangerousAction?.Invoke(toolType, risk, command ?? "");
                    return (true, "Autonomous mode - all actions allowed");

                case PolicyExecutionMode.SemiAutonomous:
                    if (risk == ToolRiskLevel.Dangerous)
                    {
                        // Allow safe build/run commands in Semi-Autonomous
                        if (IsSafeBuildCommand(cmd))
                            return (true, "Semi-autonomous - safe build command allowed");
                        OnDangerousAction?.Invoke(toolType, risk, command ?? "");
                        return (false, $"BLOCKED: {toolType} is Dangerous. Switch to Autonomous mode or execute manually.");
                    }
                    return (true, "Semi-autonomous - safe/medium allowed");

                case PolicyExecutionMode.SafeOnly:
                    if (risk != ToolRiskLevel.Safe)
                    {
                        return (false, $"BLOCKED: {toolType} is {risk}. Only Safe operations allowed.");
                    }
                    return (true, "Safe mode - read-only");

                default:
                    return (false, "Unknown execution mode");
            }
        }

        public static bool IsSafeBuildCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            // Allow compound commands like "cd subdir; dotnet build" - check each part
            var parts = cmd.Split(new[] { ';', '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var c = part.Trim();
                if (c.StartsWith("dotnet build") || c.StartsWith("dotnet run") || c.StartsWith("dotnet restore") ||
                    c.StartsWith("dotnet test") || c.StartsWith("dotnet publish") ||
                    c.StartsWith("npm run") || c.StartsWith("npm start") || c.StartsWith("npm install") ||
                    c.StartsWith("node ") || c.StartsWith("python ") || c.StartsWith("python3 ") ||
                    c.StartsWith("cargo build") || c.StartsWith("cargo run") ||
                    c.StartsWith("go build") || c.StartsWith("go run") ||
                    c.StartsWith("msbuild ") || c.StartsWith("nuget restore"))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the risk level of a tool.
        /// </summary>
        public ToolRiskLevel GetRiskLevel(string toolType)
        {
            return ToolRiskMap.TryGetValue(toolType, out var level) ? level : ToolRiskLevel.Dangerous;
        }

        /// <summary>
        /// Validates a command for dangerous patterns.
        /// </summary>
        public (bool IsSafe, string Warning) ValidateCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return (true, null);

            var cmd = command.ToLower();
            var warnings = new List<string>();

            // File system dangers
            if (cmd.Contains("rm -rf") || cmd.Contains("del /f") || cmd.Contains("rmdir"))
                warnings.Add("⚠️ Destructive file operation detected");

            if (cmd.Contains("format ") || cmd.Contains("diskpart"))
                warnings.Add("🚨 CRITICAL: Disk operation detected");

            // System dangers
            if (cmd.Contains("shutdown") || cmd.Contains("restart") || cmd.Contains("reboot"))
                warnings.Add("⚠️ System shutdown/restart command");

            if (cmd.Contains("reg delete") || cmd.Contains("regedit"))
                warnings.Add("🚨 Registry modification detected");

            // Network dangers
            if (cmd.Contains("curl") || cmd.Contains("wget") || cmd.Contains("invoke-webrequest"))
                warnings.Add("⚠️ Network request detected");

            // Path escape attempts
            if (cmd.Contains("..\\") || cmd.Contains("../") || cmd.Contains("c:\\windows"))
                warnings.Add("⚠️ Path escape attempt detected");

            if (warnings.Count > 0)
            {
                return (false, string.Join("\n", warnings));
            }

            return (true, null);
        }

        /// <summary>
        /// Validates SQL for dangerous operations.
        /// </summary>
        public (bool IsSafe, string Warning) ValidateSql(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return (true, null);

            var sqlUpper = sql.ToUpper().Trim();
            var warnings = new List<string>();

            if (sqlUpper.StartsWith("DROP "))
                warnings.Add("🚨 DROP statement detected - will delete objects");

            if (sqlUpper.StartsWith("TRUNCATE "))
                warnings.Add("🚨 TRUNCATE statement detected - will delete all data");

            if (sqlUpper.StartsWith("DELETE ") && !sqlUpper.Contains("WHERE"))
                warnings.Add("⚠️ DELETE without WHERE - will delete all rows");

            if (sqlUpper.Contains("EXEC ") || sqlUpper.Contains("EXECUTE "))
                warnings.Add("⚠️ Dynamic SQL execution detected");

            if (sqlUpper.Contains("XP_") || sqlUpper.Contains("SP_CONFIGURE"))
                warnings.Add("🚨 System stored procedure detected");

            if (warnings.Count > 0)
            {
                return (false, string.Join("\n", warnings));
            }

            return (true, null);
        }
    }
}
