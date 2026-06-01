using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    public enum ShellType
    {
        Cmd,
        PowerShell
    }

    public class TerminalService
    {
        private string _workingDirectory;
        internal static bool KernelLock { get; set; } = false;

        private void EnforceKernelLock()
        {
            if (!KernelLock) throw new InvalidOperationException("🛡️ KERNEL SECURITY VIOLATION: Direct terminal access outside Sandbox is forbidden.");
        }
        private static readonly HashSet<string> AllowedCommands = new() 
        { 
            "dotnet", "git", "npm", "node", "ls", "cd", "mkdir", "echo", "pwd", "dir" 
        };

        public TerminalService(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        private bool IsCommandAllowed(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            var firstWord = command.Trim().Split(' ')[0].ToLowerInvariant();
            return AllowedCommands.Contains(firstWord);
        }

        public void SetWorkingDirectory(string path)
        {
            _workingDirectory = path;
        }

        public event Action<string> OutputReceived;

        /// <summary>
        /// Executes a command in CMD.
        /// </summary>
        public async Task<string> ExecuteCmdAsync(string command, int timeoutMs = 30000)
        {
            return await ExecuteAsync(command, ShellType.Cmd, timeoutMs);
        }

        /// <summary>
        /// Executes a command in PowerShell.
        /// </summary>
        public async Task<string> ExecutePowerShellAsync(string command, int timeoutMs = 30000)
        {
            return await ExecuteAsync(command, ShellType.PowerShell, timeoutMs);
        }

        /// <summary>
        /// Executes a command with specified shell.
        /// </summary>
        public async Task<string> ExecuteAsync(string command, ShellType shell, int timeoutMs = 30000)
        {
            EnforceKernelLock();
            try
            {
                var psi = new ProcessStartInfo
                {
                    WorkingDirectory = _workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (shell == ShellType.PowerShell)
                {
                    psi.FileName = "powershell.exe";
                    psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"";
                }
                else
                {
                    psi.FileName = "cmd.exe";
                    psi.Arguments = $"/c {command}";
                }

                using var process = new Process { StartInfo = psi };
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) 
                    {
                        output.AppendLine(e.Data);
                        OutputReceived?.Invoke(e.Data);
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) 
                    {
                        error.AppendLine(e.Data);
                        OutputReceived?.Invoke(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

                if (!completed)
                {
                    process.Kill();
                    return $"[TIMEOUT after {timeoutMs}ms]\nPartial Output:\n{output}\nPartial Errors:\n{error}";
                }

                var result = new StringBuilder();
                result.AppendLine($"[Exit Code: {process.ExitCode}]");
                
                if (output.Length > 0)
                {
                    result.AppendLine("=== STDOUT ===");
                    result.Append(output);
                }
                
                if (error.Length > 0)
                {
                    result.AppendLine("=== STDERR ===");
                    result.Append(error);
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Execution Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Executes multiple commands in sequence.
        /// </summary>
        public async Task<string> ExecuteBatchAsync(string[] commands, ShellType shell)
        {
            var results = new StringBuilder();
            foreach (var cmd in commands)
            {
                results.AppendLine($"> {cmd}");
                results.AppendLine(await ExecuteAsync(cmd, shell));
                results.AppendLine();
            }
            return results.ToString();
        }
    }
}
