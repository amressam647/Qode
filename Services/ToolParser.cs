using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    public class ToolParser
    {
        public ToolCall? Parse(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            // 1. Try WRITE_FILE (multi-line special format)
            // Format: [WRITE_FILE path=test.cs]content...[END_WRITE_FILE]
            var writeMatch = Regex.Match(content, @"\[WRITE_FILE\s+path=([^\]]+)\](.*?)\[END_WRITE_FILE\]", RegexOptions.Singleline);
            if (writeMatch.Success)
            {
                return new ToolCall
                {
                    ToolName = "WRITE_FILE",
                    StartIndex = writeMatch.Index,
                    EndIndex = writeMatch.Index + writeMatch.Length,
                    Arguments = new Dictionary<string, object> 
                    { 
                        ["path"] = writeMatch.Groups[1].Value.Trim(),
                        ["content"] = writeMatch.Groups[2].Value.Trim('\r', '\n')
                    }
                };
            }

            // 2. Try Standard Tools [TOOL_NAME arg1=val1 arg2=val2]
            var genericMatch = Regex.Match(content, @"\[([A-Z_]+)\s+([^\]]*)\]");
            if (genericMatch.Success)
            {
                var toolName = genericMatch.Groups[1].Value;
                var argStr = genericMatch.Groups[2].Value;
                var args = new Dictionary<string, object>();

                // Match key=value pairs
                var argMatches = Regex.Matches(argStr, @"(\w+)=([^ ]+)");
                foreach (Match m in argMatches)
                {
                    args[m.Groups[1].Value] = m.Groups[2].Value;
                }

                // Fallback for simple single-arg tools [READ_FILE path/to/file]
                if (args.Count == 0 && !string.IsNullOrWhiteSpace(argStr))
                {
                    if (toolName.Contains("FILE") || toolName.Contains("DIR"))
                        args["path"] = argStr.Trim();
                    else
                        args["command"] = argStr.Trim();
                }

                return new ToolCall
                {
                    ToolName = toolName,
                    StartIndex = genericMatch.Index,
                    EndIndex = genericMatch.Index + genericMatch.Length,
                    Arguments = args
                };
            }

            return null;
        }
    }
}
