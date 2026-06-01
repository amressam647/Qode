using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Tools
{
    public class ListDirTool : ITool
    {
        private readonly FileService _fileService;
        public string Name => "LIST_DIR";
        public string Description => "Lists the contents of a directory. Args: path";

        public ListDirTool(FileService fileService)
        {
            _fileService = fileService;
        }

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> args)
        {
            string path = args.ContainsKey("path") && args["path"] is string p ? p : ".";
            var result = _fileService.ListDirectory(path);
            if (result.StartsWith("Error:"))
                return new ToolResult(false, "", result, FailureClassification.ResourceError);

            return new ToolResult(true, result);
        }
    }
}
