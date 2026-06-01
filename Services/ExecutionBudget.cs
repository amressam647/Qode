using System;
using System.IO;
using System.Text.Json;

namespace LocalCursor.Services
{
    /// <summary>
    /// Global execution budget to prevent runaway agents.
    /// Tracks and limits resource usage per session.
    /// </summary>
    public class ExecutionBudget
    {
        private readonly string _configPath;
        
        // Budget limits
        public int MaxIterations { get; set; } = 50;
        public int MaxFileWrites { get; set; } = 20;
        public int MaxCommands { get; set; } = 10;
        public int MaxDbExecutes { get; set; } = 5;
        public int MaxFileReads { get; set; } = 100;
        public int MaxGitOperations { get; set; } = 20;
        public long MaxOutputBytes { get; set; } = 10 * 1024 * 1024; // 10MB

        // Current usage
        private int _iterations;
        private int _fileWrites;
        private int _commands;
        private int _dbExecutes;
        private int _fileReads;
        private int _gitOperations;
        private long _outputBytes;

        public event Action<string, int, int> OnBudgetWarning; // resource, current, max
        public event Action<string> OnBudgetExhausted;

        public ExecutionBudget(string workspacePath)
        {
            _configPath = Path.Combine(workspacePath, ".agent_config", "budget.json");
            LoadConfig();
        }

        /// <summary>
        /// Attempts to consume a resource. Returns false if budget exhausted.
        /// </summary>
        public (bool Allowed, string Reason) TryConsume(string resourceType, int amount = 1)
        {
            switch (resourceType.ToUpper())
            {
                case "ITERATION":
                    _iterations += amount;
                    if (_iterations > MaxIterations)
                    {
                        OnBudgetExhausted?.Invoke("iterations");
                        return (false, $"Max iterations ({MaxIterations}) exceeded. Agent stopped.");
                    }
                    CheckWarning("iterations", _iterations, MaxIterations);
                    break;

                case "FILE_WRITE":
                case "WRITE":
                    _fileWrites += amount;
                    if (_fileWrites > MaxFileWrites)
                    {
                        OnBudgetExhausted?.Invoke("file_writes");
                        return (false, $"Max file writes ({MaxFileWrites}) exceeded.");
                    }
                    CheckWarning("file_writes", _fileWrites, MaxFileWrites);
                    break;

                case "CMD":
                case "PS":
                case "COMMAND":
                    _commands += amount;
                    if (_commands > MaxCommands)
                    {
                        OnBudgetExhausted?.Invoke("commands");
                        return (false, $"Max commands ({MaxCommands}) exceeded.");
                    }
                    CheckWarning("commands", _commands, MaxCommands);
                    break;

                case "DB_EXECUTE":
                    _dbExecutes += amount;
                    if (_dbExecutes > MaxDbExecutes)
                    {
                        OnBudgetExhausted?.Invoke("db_executes");
                        return (false, $"Max DB executions ({MaxDbExecutes}) exceeded.");
                    }
                    CheckWarning("db_executes", _dbExecutes, MaxDbExecutes);
                    break;

                case "READ":
                case "FILE_READ":
                    _fileReads += amount;
                    if (_fileReads > MaxFileReads)
                    {
                        OnBudgetExhausted?.Invoke("file_reads");
                        return (false, $"Max file reads ({MaxFileReads}) exceeded.");
                    }
                    break;

                case "GIT":
                    _gitOperations += amount;
                    if (_gitOperations > MaxGitOperations)
                    {
                        OnBudgetExhausted?.Invoke("git_operations");
                        return (false, $"Max Git operations ({MaxGitOperations}) exceeded.");
                    }
                    break;

                case "OUTPUT_BYTES":
                    _outputBytes += amount;
                    if (_outputBytes > MaxOutputBytes)
                    {
                        OnBudgetExhausted?.Invoke("output_bytes");
                        return (false, $"Max output size ({MaxOutputBytes / 1024 / 1024}MB) exceeded.");
                    }
                    break;
            }

            return (true, null);
        }

        /// <summary>
        /// Gets the current budget status.
        /// </summary>
        public BudgetStatus GetStatus()
        {
            return new BudgetStatus
            {
                Iterations = $"{_iterations}/{MaxIterations}",
                FileWrites = $"{_fileWrites}/{MaxFileWrites}",
                Commands = $"{_commands}/{MaxCommands}",
                DbExecutes = $"{_dbExecutes}/{MaxDbExecutes}",
                FileReads = $"{_fileReads}/{MaxFileReads}",
                OutputMB = $"{_outputBytes / 1024.0 / 1024.0:F1}/{MaxOutputBytes / 1024.0 / 1024.0:F1}"
            };
        }

        /// <summary>
        /// Resets the budget for a new session.
        /// </summary>
        public void Reset()
        {
            _iterations = 0;
            _fileWrites = 0;
            _commands = 0;
            _dbExecutes = 0;
            _fileReads = 0;
            _gitOperations = 0;
            _outputBytes = 0;
        }

        /// <summary>
        /// Returns a summary for LLM context.
        /// </summary>
        public string GetBudgetContext()
        {
            var remaining = new
            {
                iterations = MaxIterations - _iterations,
                fileWrites = MaxFileWrites - _fileWrites,
                commands = MaxCommands - _commands,
                dbExecutes = MaxDbExecutes - _dbExecutes
            };

            return $"[BUDGET] Remaining: {remaining.iterations} iterations, {remaining.fileWrites} writes, {remaining.commands} commands, {remaining.dbExecutes} DB ops";
        }

        private void CheckWarning(string resource, int current, int max)
        {
            // Warn at 80% usage
            if (current >= max * 0.8 && current < max)
            {
                OnBudgetWarning?.Invoke(resource, current, max);
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<BudgetConfig>(json);
                    if (config != null)
                    {
                        MaxIterations = config.MaxIterations;
                        MaxFileWrites = config.MaxFileWrites;
                        MaxCommands = config.MaxCommands;
                        MaxDbExecutes = config.MaxDbExecutes;
                    }
                }
            }
            catch { /* Use defaults */ }
        }

        public void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

                var config = new BudgetConfig
                {
                    MaxIterations = MaxIterations,
                    MaxFileWrites = MaxFileWrites,
                    MaxCommands = MaxCommands,
                    MaxDbExecutes = MaxDbExecutes
                };

                File.WriteAllText(_configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private class BudgetConfig
        {
            public int MaxIterations { get; set; }
            public int MaxFileWrites { get; set; }
            public int MaxCommands { get; set; }
            public int MaxDbExecutes { get; set; }
        }

        public class BudgetStatus
        {
            public string Iterations { get; set; }
            public string FileWrites { get; set; }
            public string Commands { get; set; }
            public string DbExecutes { get; set; }
            public string FileReads { get; set; }
            public string OutputMB { get; set; }

            public override string ToString()
            {
                return $"Iterations: {Iterations}, Writes: {FileWrites}, Commands: {Commands}, DB: {DbExecutes}";
            }
        }
    }
}
