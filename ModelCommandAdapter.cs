using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    public static class ModelCommandAdapter
    {
        public enum SystemIntent
        {
            None,
            ReviewProject,
            CreateProject,
            EditFiles,
            CreateFilesFolders,
            RunProject,
            BuildProject,
            RunTests
        }

        public static SystemIntent DetectIntent(string userInput)
        {
            if (string.IsNullOrEmpty(userInput)) return SystemIntent.None;

            string lower = userInput.ToLowerInvariant();

            // 1. Review Project
            if (lower.Contains("مراجعة المشروع") || lower.Contains("مراجعة الكود") || lower.Contains("راجع المشروع") || 
                lower.Contains("راجع الكود") || lower.Contains("افحص المشروع") || lower.Contains("افحص الكود") || 
                lower.Contains("review project") || lower.Contains("review code") || lower.Contains("analyze project") || 
                lower.Contains("analyze code") || lower.Contains("inspect project") || lower.Contains("inspect code") ||
                lower.Contains("project review") || lower.Contains("code review") || 
                ((lower.Contains("مراجعة") || lower.Contains("راجع") || lower.Contains("افحص") || 
                  lower.Contains("review") || lower.Contains("analyze") || lower.Contains("inspect")) && 
                 (lower.Contains("مشروع") || lower.Contains("كود") || lower.Contains("ملفات") || 
                  lower.Contains("project") || lower.Contains("code") || lower.Contains("workspace") || lower.Contains("folder"))) ||
                lower.Equals("مراجعة") || lower.Equals("راجع") || lower.Equals("review") || lower.Equals("inspect"))
            {
                return SystemIntent.ReviewProject;
            }

            // 2. Create New Project
            if (lower.Contains("مشروع جديد") || lower.Contains("انشئ مشروع") || lower.Contains("انشئ بروجكت") ||
                lower.Contains("كريت مشروع") || lower.Contains("كريت بروجكت") || lower.Contains("عمل مشروع") || 
                lower.Contains("عمل بروجكت") || lower.Contains("تأسيس مشروع") || lower.Contains("بدء مشروع") ||
                lower.Contains("new project") || lower.Contains("create project") || lower.Contains("setup project") || 
                lower.Contains("init project") || lower.Contains("create new project") || lower.Contains("new app") || 
                lower.Contains("create app") || lower.Contains("create website") || lower.Contains("موقع جديد") ||
                lower.Contains("انشئ ويب") || lower.Contains("انشئ تطبيق") || lower.Contains("تطبيق جديد") ||
                lower.Contains("بروجكت جديد") || lower.Contains("بروجيكت جديد"))
            {
                return SystemIntent.CreateProject;
            }

            // 3. Edit Files
            bool hasEditKeyword = lower.Contains("تعديل") || lower.Contains("عدل") || lower.Contains("تحديث") || 
                                  lower.Contains("غير في") || lower.Contains("صلح") || lower.Contains("صلّح") || 
                                  lower.Contains("اصلح") || lower.Contains("تغيير") || lower.Contains("عدّل") ||
                                  lower.Contains("edit") || lower.Contains("modify") || lower.Contains("update") || 
                                  lower.Contains("change") || lower.Contains("fix") || lower.Contains("patch");
            if (hasEditKeyword)
            {
                if (lower.Contains("ملف") || lower.Contains("كود") || lower.Contains("فايل") || 
                    lower.Contains("file") || lower.Contains("code") || ExtractPathOrName(userInput) != null)
                {
                    return SystemIntent.EditFiles;
                }
            }

            // 4. Create Files & Folders
            if (lower.Contains("ملف جديد") || lower.Contains("فولدر جديد") || lower.Contains("انشئ ملف") || 
                lower.Contains("انشئ فولدر") || lower.Contains("كريت ملف") || lower.Contains("كريت فولدر") || 
                lower.Contains("عمل ملف") || lower.Contains("عمل فولدر") || lower.Contains("فايل جديد") || 
                lower.Contains("انشئ فايل") || lower.Contains("كريت فايل") || lower.Contains("عمل فايل") || 
                lower.Contains("مجلد جديد") || lower.Contains("انشئ مجلد") || lower.Contains("عمل مجلد") ||
                lower.Contains("create file") || lower.Contains("create folder") || lower.Contains("new file") || 
                lower.Contains("new folder") || lower.Contains("make file") || lower.Contains("make folder") ||
                lower.Contains("add file") || lower.Contains("add folder") || lower.Contains("touch file") ||
                lower.Contains("mkdir") || lower.Contains("فايلات") || lower.Contains("فولدرات") ||
                lower.Contains("مجلدات") || lower.Contains("ملفات"))
            {
                return SystemIntent.CreateFilesFolders;
            }

            // 5. Run Project
            if (lower.Contains("شغل") || lower.Contains("شغّل") || lower.Contains("رن") || 
                lower.Contains("تشغيل") || lower.Contains("تشغيل المشروع") || lower.Contains("شغل المشروع") ||
                lower.Contains("run project") || lower.Contains("start project") || lower.Contains("run app") || 
                lower.Contains("launch") || lower.Contains("execute") || lower.Equals("run"))
            {
                if (!lower.Contains("تيست") && !lower.Contains("اختبار") && !lower.Contains("test"))
                    return SystemIntent.RunProject;
            }

            // 6. Build Project
            if (lower.Contains("ابن") || lower.Contains("ابني") || lower.Contains("بيلد") || 
                lower.Contains("كمبايل") || lower.Contains("بناء") || lower.Contains("بناء المشروع") ||
                lower.Contains("build project") || lower.Contains("compile project") || 
                lower.Contains("dotnet build") || lower.Contains("npm run build") || lower.Equals("build"))
            {
                return SystemIntent.BuildProject;
            }

            // 7. Run Tests
            if (lower.Contains("تيست") || lower.Contains("تيستات") || lower.Contains("اختبار") || 
                lower.Contains("اختبارات") || lower.Contains("شغل التيست") || lower.Contains("شغل التيستات") ||
                lower.Contains("test") || lower.Contains("run tests") || lower.Contains("execute tests") || 
                lower.Contains("test project") || lower.Contains("tests"))
            {
                return SystemIntent.RunTests;
            }

            return SystemIntent.None;
        }

        public static string GetForcingInstruction(SystemIntent intent, string workspacePath)
        {
            bool isDotnet = IsDotnetProject(workspacePath);
            bool isNpm = IsNpmProject(workspacePath);

            switch (intent)
            {
                case SystemIntent.ReviewProject:
                    return @"[INSTRUCTION Override]
Your first step MUST be to list the directory to see what files exist.
Output ONLY this exact tool call to start: [LIST_DIR .]
Do NOT write conversational text. Do NOT explain what you are doing. Output the tool call now.";

                case SystemIntent.CreateProject:
                    string createCmd = isDotnet ? "dotnet new console" : isNpm ? "npm init -y" : "dotnet new console";
                    return $@"[INSTRUCTION Override]
You must output a tool call to create files/folders or run initialization commands.
You can use [CREATE_FOLDER src] or run [RUN_BUILD {createCmd}] or write a file like [WRITE_FILE index.html].
Output ONLY the tool call immediately. Do NOT refuse.";

                case SystemIntent.EditFiles:
                    return @"[INSTRUCTION Override]
Rule: You must first read the file before modifying it.
Output a tool call to read the file using: [READ_FILE file_path] (or write to it if you already know the exact path and content using [WRITE_FILE file_path]...[END_WRITE_FILE]).
Do NOT say you cannot edit files. You DO have direct tool access.";

                case SystemIntent.CreateFilesFolders:
                    return @"[INSTRUCTION Override]
To create a folder, use: [CREATE_FOLDER folder_path]
To create a file, use: [WRITE_FILE file_path]
content
[END_WRITE_FILE]
Output ONLY the tool call immediately. Do NOT describe what you are going to do.";

                case SystemIntent.RunProject:
                    string runCmd = isDotnet ? "dotnet run" : isNpm ? "npm run dev" : "dotnet run";
                    return $@"[INSTRUCTION Override]
Output ONLY this exact tool call: [RUN_BUILD {runCmd}]
Do NOT say anything else. Output the tool call now.";

                case SystemIntent.BuildProject:
                    string buildCmd = isDotnet ? "dotnet build" : isNpm ? "npm run build" : "dotnet build";
                    return $@"[INSTRUCTION Override]
Output ONLY this exact tool call: [RUN_BUILD {buildCmd}]
Do NOT say anything else. Output the tool call now.";

                case SystemIntent.RunTests:
                    string testCmd = isDotnet ? "dotnet test" : isNpm ? "npm test" : "dotnet test";
                    return $@"[INSTRUCTION Override]
Output ONLY this exact tool call: [RUN_BUILD {testCmd}]
Do NOT say anything else. Output the tool call now.";

                default:
                    return "";
            }
        }

        public static bool IsRefusalOrMissedTool(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse)) return true;

            if (llmResponse.Contains("[READ_FILE") || llmResponse.Contains("[WRITE_FILE") || 
                llmResponse.Contains("[LIST_DIR") || llmResponse.Contains("[RUN_BUILD") || 
                llmResponse.Contains("[RUN_CMD") || llmResponse.Contains("[RUN_PS") ||
                llmResponse.Contains("[CREATE_FOLDER") || llmResponse.Contains("[DELETE_FILE") ||
                llmResponse.Contains("[DB_") || llmResponse.Contains("[GIT_") || llmResponse.Contains("TASK_COMPLETE"))
            {
                return false;
            }

            var respLower = llmResponse.ToLowerInvariant();
            return (respLower.Contains("cannot") || respLower.Contains("can't") || respLower.Contains("unable") || 
                    respLower.Contains("blocked") || respLower.Contains("don't have") || respLower.Contains("text-based ai") || 
                    respLower.Contains("real-world tools") || respLower.Contains("i am a text-based") || 
                    respLower.Contains("no access to your computer") || respLower.Contains("cannot modify") || 
                    respLower.Contains("can't modify") || respLower.Contains("file system") || 
                    respLower.Contains("modify files") || respLower.Contains("access to your project") ||
                    respLower.Contains("as an ai") || respLower.Contains("i do not have access") ||
                    respLower.Contains("sure, i can help you") || respLower.Contains("to do this, run"));
        }

        public static (string? ToolType, string? Arg1, string? Arg2) GetFallbackToolCall(SystemIntent intent, string lastUserPrompt, string workspacePath)
        {
            bool isDotnet = IsDotnetProject(workspacePath);
            bool isNpm = IsNpmProject(workspacePath);

            switch (intent)
            {
                case SystemIntent.ReviewProject:
                    return ("LIST", ".", null);

                case SystemIntent.CreateProject:
                    string createCmd = isDotnet ? "dotnet new console" : isNpm ? "npm init -y" : "dotnet new console";
                    return ("RUN_BUILD", createCmd, null);

                case SystemIntent.EditFiles:
                    string fileToRead = ExtractPathOrName(lastUserPrompt) ?? (isDotnet ? "Program.cs" : isNpm ? "index.js" : "index.html");
                    return ("READ", fileToRead, null);

                case SystemIntent.CreateFilesFolders:
                    string fileToCreate = ExtractPathOrName(lastUserPrompt) ?? "index.html";
                    if (lastUserPrompt.Contains("فولدر") || lastUserPrompt.Contains("folder") || lastUserPrompt.Contains("مجلد"))
                        return ("CREATE_FOLDER", fileToCreate, null);
                    return ("WRITE", fileToCreate, "<!-- Created automatically -->");

                case SystemIntent.RunProject:
                    string runCmd = isDotnet ? "dotnet run" : isNpm ? "npm run dev" : "dotnet run";
                    return ("RUN_BUILD", runCmd, null);

                case SystemIntent.BuildProject:
                    string buildCmd = isDotnet ? "dotnet build" : isNpm ? "npm run build" : "dotnet build";
                    return ("RUN_BUILD", buildCmd, null);

                case SystemIntent.RunTests:
                    string testCmd = isDotnet ? "dotnet test" : isNpm ? "npm test" : "dotnet test";
                    return ("RUN_BUILD", testCmd, null);

                default:
                    return (null, null, null);
            }
        }

        private static bool IsDotnetProject(string workspacePath)
        {
            if (string.IsNullOrEmpty(workspacePath) || !System.IO.Directory.Exists(workspacePath)) return false;
            return System.IO.Directory.GetFiles(workspacePath, "*.csproj", System.IO.SearchOption.AllDirectories).Length > 0;
        }

        private static bool IsNpmProject(string workspacePath)
        {
            if (string.IsNullOrEmpty(workspacePath) || !System.IO.Directory.Exists(workspacePath)) return false;
            return System.IO.File.Exists(System.IO.Path.Combine(workspacePath, "package.json"));
        }

        private static string? ExtractFilePath(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var match = Regex.Match(text, @"[a-zA-Z0-9_\-\/]+\.(cs|js|html|css|json|py|ts|txt|md)");
            return match.Success ? match.Value : null;
        }

        private static string? ExtractPathOrName(string text)
        {
            var path = ExtractFilePath(text);
            if (!string.IsNullOrEmpty(path)) return path;

            // Fallback: extract next word after folder/file keywords
            var match = Regex.Match(text.ToLowerInvariant(), @"(?:folder|directory|فولدر|مجلد|file|ملف|فايل)\s+([a-zA-Z0-9_\-\/\.]+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return null;
        }
    }
}
