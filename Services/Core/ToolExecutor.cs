using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LocalCursor.Services.Core
{
    public class ToolExecutor
    {
        private readonly Dictionary<string, ITool> _tools;
        private const int DEFAULT_TIMEOUT_MS = 30000;

        public ToolExecutor(IEnumerable<ITool> tools)
        {
            _tools = tools.ToDictionary(t => t.Name.ToUpper(), t => t);
        }

        public async Task<ToolResult> ExecuteAsync(ExecutionToken token, ToolCall call)
        {
            // HARD ENFORCEMENT: Only valid tokens from Sandbox allowed
            if (token == null || !token.IsValid(WorkflowStage.Execution))
                throw new InvalidOperationException("🛡️ KERNEL SECURITY VIOLATION: Direct execution of ToolExecutor is forbidden. Use SandboxService.");

            string toolName = call.ToolName.ToUpper();
            if (!_tools.ContainsKey(toolName))
            {
                return new ToolResult(false, "", $"Tool not found: {call.ToolName}", FailureClassification.ToolNotFound);
            }

            int retryCount = 0;
            const int MAX_RETRIES = 2;

            while (true)
            {
                try
                {
                    var tool = _tools[toolName];
                    using var cts = new CancellationTokenSource(DEFAULT_TIMEOUT_MS);
                    var task = tool.ExecuteAsync(call.Arguments);

                    if (await Task.WhenAny(task, Task.Delay(DEFAULT_TIMEOUT_MS, cts.Token)) == task)
                    {
                        var result = await task;
                        if (result.Success || retryCount >= MAX_RETRIES) return result;
                    }
                    else
                    {
                        if (retryCount >= MAX_RETRIES)
                            return new ToolResult(false, "", $"Execution timed out after {DEFAULT_TIMEOUT_MS}ms", FailureClassification.ExecutionTimeout);
                    }
                }
                catch (Exception ex) when (retryCount < MAX_RETRIES)
                {
                    // Log transient failure and retry
                }
                catch (Exception ex)
                {
                    return new ToolResult(false, "", $"Fatal Execution Error ({call.ToolName}): {ex.Message}", FailureClassification.ResourceError);
                }

                retryCount++;
                await Task.Delay(100 * retryCount); // Exponential backoff light
            }
        }

        public IEnumerable<ITool> GetRegisteredTools() => _tools.Values;
    }
}
