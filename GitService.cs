using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    public class GitService
    {
        private readonly string _workingDirectory;

        public GitService(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        /// <summary>
        /// Checks if the directory is a git repository.
        /// </summary>
        public async Task<bool> IsGitRepoAsync()
        {
            var result = await RunGitCommandAsync("rev-parse --is-inside-work-tree");
            return result.Contains("true");
        }

        /// <summary>
        /// Initializes a new git repository.
        /// </summary>
        public async Task<string> InitAsync()
        {
            return await RunGitCommandAsync("init");
        }

        /// <summary>
        /// Gets the current git status.
        /// </summary>
        public async Task<string> StatusAsync()
        {
            return await RunGitCommandAsync("status");
        }

        /// <summary>
        /// Gets the diff of staged or unstaged changes.
        /// </summary>
        public async Task<string> DiffAsync(bool staged = false)
        {
            var cmd = staged ? "diff --staged" : "diff";
            return await RunGitCommandAsync(cmd);
        }

        /// <summary>
        /// Gets diff for a specific file.
        /// </summary>
        public async Task<string> DiffFileAsync(string filePath)
        {
            return await RunGitCommandAsync($"diff -- \"{filePath}\"");
        }

        /// <summary>
        /// Stages files for commit.
        /// </summary>
        public async Task<string> AddAsync(string pathSpec = ".")
        {
            return await RunGitCommandAsync($"add {pathSpec}");
        }

        /// <summary>
        /// Commits staged changes.
        /// </summary>
        public async Task<string> CommitAsync(string message)
        {
            // Escape quotes in message
            message = message.Replace("\"", "\\\"");
            return await RunGitCommandAsync($"commit -m \"{message}\"");
        }

        /// <summary>
        /// Gets the commit log.
        /// </summary>
        public async Task<string> LogAsync(int count = 10)
        {
            return await RunGitCommandAsync($"log --oneline -n {count}");
        }

        /// <summary>
        /// Gets the current branch name.
        /// </summary>
        public async Task<string> CurrentBranchAsync()
        {
            return await RunGitCommandAsync("branch --show-current");
        }

        /// <summary>
        /// Lists all branches.
        /// </summary>
        public async Task<string> BranchListAsync()
        {
            return await RunGitCommandAsync("branch -a");
        }

        /// <summary>
        /// Creates a new branch.
        /// </summary>
        public async Task<string> CreateBranchAsync(string branchName)
        {
            return await RunGitCommandAsync($"checkout -b {branchName}");
        }

        /// <summary>
        /// Switches to a branch.
        /// </summary>
        public async Task<string> CheckoutAsync(string branchName)
        {
            return await RunGitCommandAsync($"checkout {branchName}");
        }

        /// <summary>
        /// Pulls from remote.
        /// </summary>
        public async Task<string> PullAsync()
        {
            return await RunGitCommandAsync("pull");
        }

        /// <summary>
        /// Pushes to remote.
        /// </summary>
        public async Task<string> PushAsync()
        {
            return await RunGitCommandAsync("push");
        }

        /// <summary>
        /// Stashes changes.
        /// </summary>
        public async Task<string> StashAsync(string? message = null)
        {
            var cmd = string.IsNullOrEmpty(message) ? "stash" : $"stash push -m \"{message}\"";
            return await RunGitCommandAsync(cmd);
        }

        /// <summary>
        /// Pops stashed changes.
        /// </summary>
        public async Task<string> StashPopAsync()
        {
            return await RunGitCommandAsync("stash pop");
        }

        private async Task<string> RunGitCommandAsync(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = _workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit(30000));

                if (error.Length > 0 && output.Length == 0)
                    return $"Git Error:\n{error}";

                return output.ToString().Trim();
            }
            catch (Exception ex)
            {
                return $"Git Error: {ex.Message}";
            }
        }
    }
}
