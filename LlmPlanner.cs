using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCursor.Services.Core
{
    public class LlmPlanner : ILLMPlanner
    {
        private readonly ModelRegistry _registry;
        private readonly ToolParser _parser;
        private readonly ModelMetadata _model;
        private readonly string? _apiKey;

        public LlmPlanner(ModelRegistry registry, ModelMetadata model, string? apiKey)
        {
            _registry = registry;
            _model = model;
            _apiKey = apiKey;
            _parser = new ToolParser();
        }

        public async Task<List<ToolCall>> GetPlanAsync(AgentContext context)
        {
            return await GetNextStepAsync(context);
        }

        public async Task<List<ToolCall>> GetNextStepAsync(AgentContext context)
        {
            var provider = _registry.GetProvider(_model.ProviderId);
            if (provider == null) return new List<ToolCall>();

            var history = BuildHistory(context);
            var response = await provider.ChatAsync(_model, history, _apiKey, CancellationToken.None);

            var toolCall = _parser.Parse(response);
            if (toolCall == null) return new List<ToolCall>();

            // Convert to new ToolCall format
            return new List<ToolCall> 
            { 
                new ToolCall 
                { 
                    ToolName = toolCall.ToolName, 
                    Arguments = toolCall.Arguments.ToDictionary(x => x.Key, x => (object)x.Value)
                } 
            };
        }

        public async Task<string> SummarizeFinalResponseAsync(AgentContext context)
        {
            var provider = _registry.GetProvider(_model.ProviderId);
            if (provider == null) return "Task Complete.";

            var history = BuildHistory(context);
            history.Add(new SimpleMessage { Role = "user", Content = "The task is done. Please provide a concise summary of what was achieved." });
            
            return await provider.ChatAsync(_model, history, _apiKey, CancellationToken.None);
        }

        private List<SimpleMessage> BuildHistory(AgentContext context)
        {
            var messages = new List<SimpleMessage>();
            
            // System Prompt with tool descriptions
            messages.Add(new SimpleMessage { Role = "system", Content = BuildSystemPrompt() });
            
            // Initial User Input
            messages.Add(new SimpleMessage { Role = "user", Content = context.UserInput });

            // Observations as history
            foreach (var obs in context.Observations)
            {
                var feedback = obs.Result.Success 
                    ? $"[OBSERVATION: {obs.ToolName} SUCCESS]\n{obs.Result.Output}" 
                    : $"[OBSERVATION: {obs.ToolName} FAILED]\nError: {obs.Result.Error}";
                
                messages.Add(new SimpleMessage { Role = "user", Content = feedback });
            }

            return messages;
        }

        private string BuildSystemPrompt()
        {
            return "You are Qode AI, an expert agent. Think step-by-step. Use [TOOL_NAME arg=val] to act. If finished, just say TASK_COMPLETE.";
        }
    }
}
