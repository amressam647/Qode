using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Tools
{
    public class TerminalTool : ITool
    {
        private readonly TerminalService _terminalService;
        public string Name => "RUN_BUILD";
        public string Description => "Executes build or terminal commands. Args: command";

        public TerminalTool(TerminalService terminalService)
        {
            _terminalService = terminalService;
        }

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("command", out var cmdObj) || cmdObj is not string command)
            {
                return new ToolResult(false, "", "Missing or invalid argument: command", FailureClassification.LogicError);
            }

            var output = await _terminalService.ExecuteCmdAsync(command);
            return new ToolResult(true, output);
        }
    }
}
