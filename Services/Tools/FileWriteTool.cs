using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Tools
{
    public class FileWriteTool : ITool
    {
        private readonly FileService _fileService;
        public string Name => "WRITE_FILE";
        public string Description => "Writes content to a file. Args: path, content";

        public FileWriteTool(FileService fileService)
        {
            _fileService = fileService;
        }

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult(false, "", "Missing or invalid argument: path", FailureClassification.LogicError);
            
            if (!args.TryGetValue("content", out var contentObj) || contentObj is not string content)
                return new ToolResult(false, "", "Missing or invalid argument: content", FailureClassification.LogicError);

            var result = _fileService.WriteFile(path, content);
            if (result.StartsWith("Error:"))
                return new ToolResult(false, "", result, FailureClassification.ResourceError);

            return new ToolResult(true, result);
        }
    }
}
