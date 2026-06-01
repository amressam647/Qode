using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Tools
{
    public class FileReadTool : ITool
    {
        private readonly FileService _fileService;
        public string Name => "READ_FILE";
        public string Description => "Reads the content of a file. Args: path";

        public FileReadTool(FileService fileService)
        {
            _fileService = fileService;
        }

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult(false, "", "Missing or invalid argument: path", FailureClassification.LogicError);

            var content = _fileService.ReadFile(path);
            if (content.StartsWith("Error:"))
                return new ToolResult(false, "", content, FailureClassification.ResourceError);

            return new ToolResult(true, content);
        }
    }
}
