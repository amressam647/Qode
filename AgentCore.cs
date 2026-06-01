
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    public class AgentCore
    {
        private readonly LlmService _llmService;
        private readonly FileService _fileService;
        private readonly TerminalService _terminalService;
        private readonly DocumentReaderService _documentReader;
        private readonly DatabaseService _databaseService;
        private readonly GitService _gitService;
        private readonly DiffService _diffService;
        private readonly WebSearchService _webSearch = new();
        
        // Security Services
        private readonly ExecutionPolicy _policy;
        private readonly SandboxService _sandbox;
        private readonly ExecutionLogger _logger;
        
        private const int MAX_ITERATIONS = 50;
        
        public PolicyExecutionMode CurrentMode 
        { 
            get => _policy.CurrentMode; 
            set => _policy.CurrentMode = value; 
        }

        public AgentCore(LlmService llmService, FileService fileService)
        {
            _llmService = llmService;
            _fileService = fileService;
            
            var workspacePath = fileService.GetWorkspacePath();
            
            // Initialize security first
            _policy = new ExecutionPolicy();
            var tools = new List<ITool>();
            var executor = new ToolExecutor(tools);
            _sandbox = new SandboxService(workspacePath, executor);
            _logger = new ExecutionLogger(workspacePath);
            
            // Hook dangerous action logging
            _policy.OnDangerousAction += (tool, risk, cmd) => 
                _logger.LogDangerousAction(tool, risk, cmd);
            
            // Initialize tool services
            _terminalService = new TerminalService(workspacePath);
            _documentReader = new DocumentReaderService();
            _databaseService = new DatabaseService();
            _gitService = new GitService(workspacePath);
            _diffService = new DiffService();
        }


        public string GetSystemPrompt(string workspacePath = "")
        {
            return GetCoreSection(workspacePath) + GetToolsSection() + GetWorkflowSection();
        }

        private string GetCoreSection(string workspacePath)
        {
            var wsLine = string.IsNullOrEmpty(workspacePath) ? "" : $"\nWORKSPACE: {workspacePath}";
            return $@"
=== QODE AI CODING ASSISTANT ===
You are an expert software engineer AI embedded inside a desktop IDE called Qode.
You have DIRECT ACCESS to the file system, terminal, database, and git via tool calls.
You MUST use tool calls to accomplish tasks - you cannot just describe what to do.
{wsLine}

CRITICAL RULES:
- ALWAYS use tools to read/write/execute. Never say 'I can't access files' - you CAN.
- Use RELATIVE paths (e.g. src/Program.cs) NOT absolute paths unless absolutely needed.
- After writing files, always verify with [READ_FILE] to confirm success.
- For build errors: read the file, fix the bug, write it back, build again.
- Respond in the SAME LANGUAGE the user writes in (Arabic → Arabic, English → English).
";
        }

        private string GetToolsSection()
        {
            return @"
=== AVAILABLE TOOLS ===

[FILE OPERATIONS]
[READ_FILE path]              → Read any file in the workspace
[LIST_DIR path]               → List directory contents (use . for root)
[WRITE_FILE path]             → Create or overwrite a file
content goes here
[END_WRITE_FILE]
[CREATE_FOLDER path]          → Create a directory
[DELETE_FILE path]            → Delete a file
[DELETE_FOLDER path]          → Delete a directory recursively
[READ_DOC path]               → Read PDF, Word, Excel, CSV

[TERMINAL]
[RUN_BUILD command]           → Safe build commands: dotnet build, dotnet run, npm install, npm run dev, pip install, etc.
[RUN_CMD command]             → Run CMD command
[RUN_PS command]              → Run PowerShell command

[DATABASE]
[DB_CONNECT server=X;database=Y;integrated=true]  → Connect to SQL Server
[DB_CONNECT server=X;database=Y;user=U;password=P] → Connect with credentials
[DB_QUERY SELECT ...]         → Run SELECT query
[DB_EXECUTE INSERT/UPDATE/DELETE ...]  → Run data-modification query
[DB_SCHEMA]                   → Get full schema
[DB_TABLES]                   → List all tables

[GIT]
[GIT_STATUS]  [GIT_DIFF]  [GIT_LOG]  [GIT_BRANCH]
[GIT_ADD path]  [GIT_COMMIT message]  [GIT_PULL]  [GIT_PUSH]
[GIT_CHECKOUT branch]

[OTHER]
[TAIL_LOG path lines=50]      → Read last N lines of a log file
[WEB_SEARCH query]            → Search the web for information
[DIFF_PREVIEW path]           → Preview file diff before writing

";
        }

        private string GetWorkflowSection()
        {
            return @"
=== WORKFLOW RULES ===
1. THINK first (in <thinking> tags), then output ONE tool call per response.
2. ALWAYS start new tasks with [LIST_DIR .] to understand the project.
3. To edit a file: [READ_FILE] → modify → [WRITE_FILE] → [READ_FILE] to verify.
4. For dotnet projects: [RUN_BUILD dotnet build] to check errors.
5. For npm projects: [RUN_BUILD npm install] then [RUN_BUILD npm run dev].
6. For database: [DB_CONNECT ...] first, then [DB_TABLES], then query.
7. If a tool returns an error, analyze it and try a different approach.
8. Only give a text response (no tool call) when you have FULLY completed the task.

EXAMPLES:
- User: 'اعمل فايل Program.cs' → [WRITE_FILE Program.cs]\nusing System;\n...[END_WRITE_FILE]
- User: 'ابن المشروع' → [RUN_BUILD dotnet build]
- User: 'شوف الاخطاء' → [RUN_BUILD dotnet build]
- User: 'اتصل بالداتابيز' → [DB_CONNECT server=localhost;database=MyDb;integrated=true]
";
        }


        public async Task<string> RunAgentLoopAsync(List<SimpleMessage> history, Action<string> onPartialResponse, Action<string, string> onToolExecuted = null, CancellationToken cancellationToken = default)
        {
            var workspacePath = _fileService.GetWorkspacePath();
            var lastUserMsg = history.LastOrDefault(m => m.Role == "user" && !m.Content.StartsWith("["))?.Content ?? "";

            // Ensure System Prompt is first - always refresh it with current workspace
            if (history.Count == 0 || history[0].Role != "system")
                history.Insert(0, new SimpleMessage { Role = "system", Content = GetSystemPrompt(workspacePath) });
            else
                history[0].Content = GetSystemPrompt(workspacePath); // keep it fresh

            // Inject workspace context once
            if (!history.Any(m => m.Content.Contains("[WORKSPACE_CTX]")))
            {
                var dirListing = _fileService.ListDirectory(".");
                var ctx = $"[WORKSPACE_CTX]\nWorkspace: {workspacePath}\nFiles:\n{dirListing}";
                history.Insert(1, new SimpleMessage { Role = "user", Content = ctx });
                history.Insert(2, new SimpleMessage { Role = "assistant", Content = "Understood. I have reviewed the workspace structure and I am ready to help." });
            }

            // Model Command Adapter - Intent-based prompting & fallback for smaller models (e.g. Gemma)
            var currentIntent = ModelCommandAdapter.DetectIntent(lastUserMsg);
            if (currentIntent != ModelCommandAdapter.SystemIntent.None)
            {
                var forcePrompt = ModelCommandAdapter.GetForcingInstruction(currentIntent, workspacePath);
                if (!string.IsNullOrEmpty(forcePrompt))
                {
                    history.Add(new SimpleMessage { Role = "system", Content = forcePrompt });
                }
            }
            else
            {
                // Fallback to legacy checks if no specific intent is found
                bool isFirstInteraction = history.Count <= 6;
                bool wantsBuildOrRun = lastUserMsg.Contains("build") || lastUserMsg.Contains("run") ||
                                       lastUserMsg.Contains("ابن") || lastUserMsg.Contains("شغّل") || lastUserMsg.Contains("شغل") ||
                                       lastUserMsg.Contains("compile") || lastUserMsg.Contains("كمبايل");

                bool wantsFixErrors = lastUserMsg.Contains("fix") || lastUserMsg.Contains("اصلح") || lastUserMsg.Contains("صلح") ||
                                      lastUserMsg.Contains("correct") || lastUserMsg.Contains("repair") || lastUserMsg.Contains("عدل");
                if (wantsBuildOrRun)
                {
                    bool isDotnet = lastUserMsg.Contains("dotnet") || lastUserMsg.Contains(".net") || System.IO.File.Exists(System.IO.Path.Combine(workspacePath, "*.csproj")) ||
                                    System.IO.Directory.GetFiles(workspacePath, "*.csproj", System.IO.SearchOption.TopDirectoryOnly).Length > 0;
                    bool isNpm = lastUserMsg.Contains("npm") || lastUserMsg.Contains("node") || System.IO.File.Exists(System.IO.Path.Combine(workspacePath, "package.json"));
                    string buildCmd = isDotnet ? "dotnet build" : isNpm ? "npm run dev" : "dotnet build";
                    history.Add(new SimpleMessage { Role = "system", Content = $"Output ONLY this tool call now: [RUN_BUILD {buildCmd}]" });
                }
                else if (wantsFixErrors)
                {
                    history.Add(new SimpleMessage { Role = "system", Content =
                        "Fix the errors. Use [READ_FILE path] to read each affected file, then [WRITE_FILE path]...[END_WRITE_FILE] to write the fix. Start with a tool call now." });
                }
                else if (isFirstInteraction)
                {
                    history.Add(new SimpleMessage { Role = "system", Content = "Start with a tool call. Use [LIST_DIR .] if you need to explore the project." });
                }
            }

            int iterations = 0;
            string finalResponse = "";

            while (iterations < MAX_ITERATIONS)
            {
                iterations++;
                
                // 1. Call LLM
                cancellationToken.ThrowIfCancellationRequested();
                var response = await _llmService.ChatAsync(history, cancellationToken);
                history.Add(new SimpleMessage { Role = "assistant", Content = response });
                onPartialResponse?.Invoke(response); // Update UI
                finalResponse = response;

                // 2. Parse Tools
                var toolCmd = ParseTool(response);
                if (toolCmd == null)
                {
                    if (currentIntent != ModelCommandAdapter.SystemIntent.None)
                    {
                        var (fallbackType, fallbackArg1, fallbackArg2) = ModelCommandAdapter.GetFallbackToolCall(currentIntent, lastUserMsg, workspacePath);
                        if (fallbackType != null)
                        {
                            toolCmd = new ToolCommand { Type = fallbackType, Arg1 = fallbackArg1, Arg2 = fallbackArg2 };
                        }
                    }

                    if (toolCmd == null)
                    {
                        // Model refused to use tools? (e.g. Gemma: "I can't modify files... I don't have access to file system")
                        var lastUser = history.LastOrDefault(m => m.Role == "user")?.Content?.ToLower() ?? "";
                        bool wantsBuild = lastUser.Contains("build") || lastUser.Contains("ابن") || lastUser.Contains("compile");
                        bool wantsRun = lastUser.Contains("run") || lastUser.Contains("شغّل") || lastUser.Contains("شغل") || lastUser.Contains("execute");
                        bool wantsFix = lastUser.Contains("fix") || lastUser.Contains("modify") || lastUser.Contains("edit") || lastUser.Contains("اصلح") || lastUser.Contains("عدّل") || lastUser.Contains("build error");
                        var respLower = response.ToLowerInvariant();
                        bool modelRefused = (respLower.Contains("cannot") || respLower.Contains("can't") || respLower.Contains("unable") || respLower.Contains("blocked")
                            || respLower.Contains("don't have") || respLower.Contains("text-based ai") || respLower.Contains("real-world tools")
                            || respLower.Contains("i am a text-based") || respLower.Contains("no access to your computer")
                            || respLower.Contains("cannot modify") || respLower.Contains("can't modify") || respLower.Contains("file system")
                            || respLower.Contains("modify files") || respLower.Contains("access to your project"));
                        if ((wantsBuild || wantsRun) && modelRefused)
                        {
                            toolCmd = new ToolCommand { Type = "RUN_BUILD", Arg1 = wantsBuild ? "dotnet build" : "dotnet run" };
                        }
                        else if (wantsFix && modelRefused)
                        {
                            history.Add(new SimpleMessage { Role = "system", Content =
                                @"[CRITICAL] You CAN modify files. Use [READ_FILE path] to read, then [WRITE_FILE path]...content...[END_WRITE_FILE] to write. The system WILL execute these. Check [TOOL_OUTPUT] in the chat for error locations. Output a tool call NOW." });
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // 3. SECURITY CHECK - Policy and Sandbox validation
                var risk = _policy.GetRiskLevel(toolCmd.Type);
                var (allowed, reason) = _policy.CanExecute(toolCmd.Type, toolCmd.Arg1);
                
                // RUN_BUILD must contain only safe build commands
                if (allowed && toolCmd.Type == "RUN_BUILD" && !ExecutionPolicy.IsSafeBuildCommand(toolCmd.Arg1 ?? ""))
                {
                    allowed = false;
                    reason = "BLOCKED: RUN_BUILD only allows dotnet build, dotnet run, npm run, etc.";
                }

                if (!allowed)
                {
                    // Log blocked action
                    _logger.LogExecution(toolCmd.Type, risk, toolCmd.Arg1 ?? "", reason, wasBlocked: true);
                    
                    // Add blocking message to history
                    history.Add(new SimpleMessage { 
                        Role = "user", 
                        Content = $"[SECURITY_BLOCK]\n⚠️ {reason}\nSwitch to Autonomous mode or use a safer alternative." 
                    });
                    continue; // Try next iteration with feedback
                }

                // 4. Sandbox validation for file operations
                if (toolCmd.Type == "READ" || toolCmd.Type == "WRITE" || toolCmd.Type == "LIST" || 
                    toolCmd.Type == "DOC" || toolCmd.Type == "TAIL" || toolCmd.Type == "CREATE_FOLDER" ||
                    toolCmd.Type == "DELETE_FILE" || toolCmd.Type == "DELETE_FOLDER")
                {
                    var (isValid, normalizedPath, error) = _sandbox.ValidatePath(toolCmd.Arg1);
                    if (!isValid)
                    {
                        _logger.LogSecurityViolation("PATH_ESCAPE", error);
                        history.Add(new SimpleMessage { 
                            Role = "user", 
                            Content = $"[SECURITY_BLOCK]\n{error}" 
                        });
                        continue;
                    }
                    toolCmd.Arg1 = normalizedPath; // Use sanitized path
                }

                // 5. Command validation for terminal operations
                if (toolCmd.Type == "CMD" || toolCmd.Type == "PS" || toolCmd.Type == "RUN_BUILD")
                {
                    var (cmdValid, cmdError) = _sandbox.ValidateCommand(toolCmd.Arg1);
                    if (!cmdValid)
                    {
                        _logger.LogSecurityViolation("BLOCKED_COMMAND", cmdError);
                        history.Add(new SimpleMessage { 
                            Role = "user", 
                            Content = $"[SECURITY_BLOCK]\n{cmdError}" 
                        });
                        continue;
                    }
                }

                // 6. SQL validation for DB operations
                if (toolCmd.Type == "DB_EXECUTE")
                {
                    var (sqlValid, sqlWarning) = _policy.ValidateSql(toolCmd.Arg1);
                    if (!sqlValid)
                    {
                        // Log but still execute if in Autonomous mode
                        _logger.LogDangerousAction("DB_EXECUTE", ToolRiskLevel.Dangerous, sqlWarning);
                        if (_policy.CurrentMode != PolicyExecutionMode.Autonomous)
                        {
                            history.Add(new SimpleMessage { 
                                Role = "user", 
                                Content = $"[SECURITY_WARNING]\n{sqlWarning}\nOperation blocked in Semi-Autonomous mode." 
                            });
                            continue;
                        }
                    }
                }

                // 7. Execute Tool (passed all security checks)
                onToolExecuted?.Invoke($"{toolCmd.Type} {toolCmd.Arg1 ?? ""}".Trim(), "");
                var toolOutput = await ExecuteToolAsync(toolCmd);
                onToolExecuted?.Invoke($"{toolCmd.Type} {toolCmd.Arg1 ?? ""}".Trim(), toolOutput);
                
                // Log execution (full output)
                _logger.LogExecution(toolCmd.Type, risk, toolCmd.Arg1 ?? "", toolOutput);
                
                // 8. Add Tool Output to History - truncate to avoid "prompt too long"
                var outputForHistory = TruncateToolOutputForHistory(toolCmd.Type, toolOutput);
                history.Add(new SimpleMessage { Role = "user", Content = $"[TOOL_OUTPUT]\n{outputForHistory}" });
                
                // 8b. If build failed with errors, add instruction so agent can fix them
                if (toolCmd.Type == "RUN_BUILD" && 
                    (toolOutput.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || toolOutput.Contains("Error(s)")))
                {
                    history.Add(new SimpleMessage { Role = "system", Content = 
                        @"[CONTEXT] Build failed. The errors are in [TOOL_OUTPUT] above. Parse them (format: path(line,col): error CSxxx: message).
When user asks to fix, use READ_FILE on each affected file, then WRITE_FILE to apply fixes. Fix one file at a time. You CAN and MUST fix these errors." });
                }
                
                cancellationToken.ThrowIfCancellationRequested();
            }


            return finalResponse;
        }

        private bool CheckMemoryFiles()
        {
            string root = _fileService.GetWorkspacePath();
            bool plan = System.IO.File.Exists(System.IO.Path.Combine(root, "project_plan.md"));
            // We only check one to determine if initialization is needed strictly
            return plan;
        }

        /// <summary>Truncate tool output to avoid exceeding LLM token limit (prompt too long).</summary>
        private static string TruncateToolOutputForHistory(string toolType, string output)
        {
            const int maxChars = 6000;
            if (string.IsNullOrEmpty(output) || output.Length <= maxChars) return output;

            // For build/run commands: keep error lines + summary, drop verbose warnings
            if (toolType == "RUN_BUILD" || toolType == "PS" || toolType == "CMD")
            {
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                var errorLines = new List<string>();
                string summaryLine = null;
                foreach (var line in lines)
                {
                    if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 && 
                        (line.Contains("): error ") || line.Contains("): error CS")))
                        errorLines.Add(line);
                    else if (line.Contains("Warning(s)") || line.Contains("Error(s)"))
                        summaryLine = line;
                }
                if (errorLines.Count > 0)
                {
                    sb.AppendLine("--- Errors (full output truncated) ---");
                    foreach (var err in errorLines) sb.AppendLine(err);
                }
                if (summaryLine != null) sb.AppendLine(summaryLine);
                if (sb.Length == 0)
                    sb.Append(output.Substring(0, Math.Min(1500, output.Length))).Append("\n... [output truncated]");
                else
                    sb.Append("\n... [warnings and rest truncated to save tokens]");
                return sb.ToString();
            }
            return output.Substring(0, maxChars) + "\n... [truncated]";
        }


        private ToolCommand ParseTool(string content)
        {
            // READ_FILE
            var readMatch = Regex.Match(content, @"\[READ_FILE\s+(.+?)\]");
            if (readMatch.Success) return new ToolCommand { Type = "READ", Arg1 = readMatch.Groups[1].Value.Trim() };

            // LIST_DIR - support [LIST_DIR .] and [LIST_DIR: path]
            var listMatch = Regex.Match(content, @"\[LIST_DIR\s*:?\s*([^\]]+)\]", RegexOptions.Singleline);
            if (listMatch.Success) return new ToolCommand { Type = "LIST", Arg1 = listMatch.Groups[1].Value.Trim() };

            // RUN_BUILD - for dotnet build, npm run, etc. (must be before RUN_CMD/RUN_PS)
            var buildMatch = Regex.Match(content, @"\[RUN_BUILD\s*:?\s*([^\]]+)\]", RegexOptions.Singleline);
            if (buildMatch.Success) return new ToolCommand { Type = "RUN_BUILD", Arg1 = buildMatch.Groups[1].Value.Trim() };

            // RUN_CMD - support [RUN_CMD cmd] and [RUN_CMD: cmd]
            var cmdMatch = Regex.Match(content, @"\[RUN_CMD\s*:?\s*([^\]]+)\]", RegexOptions.Singleline);
            if (cmdMatch.Success) return new ToolCommand { Type = "CMD", Arg1 = cmdMatch.Groups[1].Value.Trim() };

            // RUN_PS (PowerShell) - support [RUN_PS cmd] and [RUN_PS: cmd]
            var psMatch = Regex.Match(content, @"\[RUN_PS\s*:?\s*([^\]]+)\]", RegexOptions.Singleline);
            if (psMatch.Success) return new ToolCommand { Type = "PS", Arg1 = psMatch.Groups[1].Value.Trim() };

            // WRITE_FILE
            var writeMatch = Regex.Match(content, @"\[WRITE_FILE\s+(.+?)\]\s*([\s\S]*?)\s*\[END_WRITE_FILE\]");
            if (writeMatch.Success) return new ToolCommand { Type = "WRITE", Arg1 = writeMatch.Groups[1].Value.Trim(), Arg2 = writeMatch.Groups[2].Value };

            // CREATE_FOLDER, DELETE_FILE, DELETE_FOLDER
            var createFolderMatch = Regex.Match(content, @"\[CREATE_FOLDER\s*:?\s*([^\]]+)\]");
            if (createFolderMatch.Success) return new ToolCommand { Type = "CREATE_FOLDER", Arg1 = createFolderMatch.Groups[1].Value.Trim() };
            var deleteFileMatch = Regex.Match(content, @"\[DELETE_FILE\s*:?\s*([^\]]+)\]");
            if (deleteFileMatch.Success) return new ToolCommand { Type = "DELETE_FILE", Arg1 = deleteFileMatch.Groups[1].Value.Trim() };
            var deleteFolderMatch = Regex.Match(content, @"\[DELETE_FOLDER\s*:?\s*([^\]]+)\]");
            if (deleteFolderMatch.Success) return new ToolCommand { Type = "DELETE_FOLDER", Arg1 = deleteFolderMatch.Groups[1].Value.Trim() };

            // READ_DOC (PDF, Excel, etc.)
            var docMatch = Regex.Match(content, @"\[READ_DOC\s+(.+?)\]");
            if (docMatch.Success) return new ToolCommand { Type = "DOC", Arg1 = docMatch.Groups[1].Value.Trim() };

            // TAIL_LOG
            var tailMatch = Regex.Match(content, @"\[TAIL_LOG\s+(.+?)\s+lines=(\d+)\]");
            if (tailMatch.Success) return new ToolCommand { Type = "TAIL", Arg1 = tailMatch.Groups[1].Value.Trim(), Arg2 = tailMatch.Groups[2].Value };

            // DB_CONNECT
            var dbConnMatch = Regex.Match(content, @"\[DB_CONNECT\s+(.+?)\]");
            if (dbConnMatch.Success) return new ToolCommand { Type = "DB_CONNECT", Arg1 = dbConnMatch.Groups[1].Value.Trim() };

            // DB_QUERY
            var dbQueryMatch = Regex.Match(content, @"\[DB_QUERY\s+(.+?)\]");
            if (dbQueryMatch.Success) return new ToolCommand { Type = "DB_QUERY", Arg1 = dbQueryMatch.Groups[1].Value.Trim() };

            // DB_EXECUTE
            var dbExecMatch = Regex.Match(content, @"\[DB_EXECUTE\s+(.+?)\]");
            if (dbExecMatch.Success) return new ToolCommand { Type = "DB_EXECUTE", Arg1 = dbExecMatch.Groups[1].Value.Trim() };

            // DB_SCHEMA
            if (content.Contains("[DB_SCHEMA]")) return new ToolCommand { Type = "DB_SCHEMA" };

            // DB_TABLES
            if (content.Contains("[DB_TABLES]")) return new ToolCommand { Type = "DB_TABLES" };

            // GIT TOOLS
            if (content.Contains("[GIT_STATUS]")) return new ToolCommand { Type = "GIT_STATUS" };
            if (content.Contains("[GIT_DIFF_STAGED]")) return new ToolCommand { Type = "GIT_DIFF_STAGED" };
            if (content.Contains("[GIT_DIFF]")) return new ToolCommand { Type = "GIT_DIFF" };
            if (content.Contains("[GIT_LOG]")) return new ToolCommand { Type = "GIT_LOG" };
            if (content.Contains("[GIT_BRANCH]")) return new ToolCommand { Type = "GIT_BRANCH" };
            if (content.Contains("[GIT_PULL]")) return new ToolCommand { Type = "GIT_PULL" };
            if (content.Contains("[GIT_PUSH]")) return new ToolCommand { Type = "GIT_PUSH" };

            var gitAddMatch = Regex.Match(content, @"\[GIT_ADD\s+(.+?)\]");
            if (gitAddMatch.Success) return new ToolCommand { Type = "GIT_ADD", Arg1 = gitAddMatch.Groups[1].Value.Trim() };

            var gitCommitMatch = Regex.Match(content, @"\[GIT_COMMIT\s+(.+?)\]");
            if (gitCommitMatch.Success) return new ToolCommand { Type = "GIT_COMMIT", Arg1 = gitCommitMatch.Groups[1].Value.Trim() };

            var gitCheckoutMatch = Regex.Match(content, @"\[GIT_CHECKOUT\s+(.+?)\]");
            if (gitCheckoutMatch.Success) return new ToolCommand { Type = "GIT_CHECKOUT", Arg1 = gitCheckoutMatch.Groups[1].Value.Trim() };

            // DIFF_PREVIEW
            var diffMatch = Regex.Match(content, @"\[DIFF_PREVIEW\s+(.+?)\s*\]");
            if (diffMatch.Success) return new ToolCommand { Type = "DIFF_PREVIEW", Arg1 = diffMatch.Groups[1].Value.Trim() };

            // WEB_SEARCH - when agent doesn't know something, search the web
            var webMatch = Regex.Match(content, @"\[WEB_SEARCH\s*:?\s*([^\]]+)\]", RegexOptions.Singleline);
            if (webMatch.Success) return new ToolCommand { Type = "WEB_SEARCH", Arg1 = webMatch.Groups[1].Value.Trim() };

            return null;
        }



        private async Task<string> ExecuteToolAsync(ToolCommand cmd)
        {
            try 
            {
                switch (cmd.Type)
                {
                    case "READ":
                        return _fileService.ReadFile(cmd.Arg1);
                    case "LIST":
                        return _fileService.ListDirectory(cmd.Arg1);
                    case "WRITE":
                        return _fileService.WriteFile(cmd.Arg1, cmd.Arg2);
                    case "CREATE_FOLDER":
                        return _fileService.CreateFolder(cmd.Arg1);
                    case "DELETE_FILE":
                        return _fileService.DeleteFile(cmd.Arg1);
                    case "DELETE_FOLDER":
                        return _fileService.DeleteFolder(cmd.Arg1);
                    case "RUN_BUILD":
                        return await _terminalService.ExecutePowerShellAsync(cmd.Arg1 ?? "");
                    case "CMD":
                        return await _terminalService.ExecuteCmdAsync(cmd.Arg1 ?? "");
                    case "PS":
                        return await _terminalService.ExecutePowerShellAsync(cmd.Arg1 ?? "");
                    case "DOC":
                        return _documentReader.ReadDocument(System.IO.Path.Combine(_fileService.GetWorkspacePath(), cmd.Arg1));
                    case "TAIL":
                        int lines = int.TryParse(cmd.Arg2, out var n) ? n : 50;
                        return _documentReader.TailLog(System.IO.Path.Combine(_fileService.GetWorkspacePath(), cmd.Arg1), lines);
                    case "DB_CONNECT":
                        ParseAndConfigureDb(cmd.Arg1);
                        return await _databaseService.TestConnectionAsync();
                    case "DB_QUERY":
                        return await _databaseService.ExecuteQueryAsync(cmd.Arg1);
                    case "DB_EXECUTE":
                        return await _databaseService.ExecuteNonQueryAsync(cmd.Arg1);
                    case "DB_SCHEMA":
                        return await _databaseService.GetSchemaAsync();
                    case "DB_TABLES":
                        return await _databaseService.ListTablesAsync();
                    
                    // GIT TOOLS
                    case "GIT_STATUS":
                        return await _gitService.StatusAsync();
                    case "GIT_DIFF":
                        return await _gitService.DiffAsync(false);
                    case "GIT_DIFF_STAGED":
                        return await _gitService.DiffAsync(true);
                    case "GIT_ADD":
                        return await _gitService.AddAsync(cmd.Arg1);
                    case "GIT_COMMIT":
                        return await _gitService.CommitAsync(cmd.Arg1);
                    case "GIT_LOG":
                        return await _gitService.LogAsync();
                    case "GIT_BRANCH":
                        return await _gitService.BranchListAsync();
                    case "GIT_CHECKOUT":
                        return await _gitService.CheckoutAsync(cmd.Arg1);
                    case "GIT_PULL":
                        return await _gitService.PullAsync();
                    case "GIT_PUSH":
                        return await _gitService.PushAsync();

                    // DIFF PREVIEW
                    case "DIFF_PREVIEW":
                        var oldContent = _fileService.ReadFile(cmd.Arg1);
                        return _diffService.GenerateDiff(oldContent, cmd.Arg2 ?? "[New File]", cmd.Arg1);

                    // WEB SEARCH
                    case "WEB_SEARCH":
                        return await _webSearch.SearchAsync(cmd.Arg1 ?? "");

                    default:
                        return "Unknown Tool";
                }
            }
            catch (Exception ex)
            {
                return $"Tool Execution Error: {ex.Message}";
            }
        }

        private void ParseAndConfigureDb(string connStr)
        {
            // Parse: server=localhost;database=MyDb;integrated=true
            var parts = connStr.Split(';');
            string server = "localhost", database = "", userId = "", password = "";
            bool integrated = true;

            foreach (var part in parts)
            {
                var kv = part.Split('=');
                if (kv.Length == 2)
                {
                    var key = kv[0].Trim().ToLower();
                    var val = kv[1].Trim();
                    switch (key)
                    {
                        case "server": server = val; break;
                        case "database": database = val; break;
                        case "integrated": integrated = val.ToLower() == "true"; break;
                        case "user": userId = val; break;
                        case "password": password = val; break;
                    }
                }
            }
            _databaseService.Configure(server, database, userId, password, integrated);
        }

        private class ToolCommand
        {
            public string Type { get; set; }
            public string Arg1 { get; set; }
            public string Arg2 { get; set; }
        }
    }
}
