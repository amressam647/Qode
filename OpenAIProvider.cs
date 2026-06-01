using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class OpenAIProvider : IAIProvider
    {
        public string Id => "OpenAI";
        public string Name => "OpenAI (Cloud)";

        public Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var models = new List<ModelMetadata>
            {
                new("gpt-4o", "GPT-4o (Omni)", Id, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("gpt-4o-mini", "GPT-4o Mini", Id, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("o1-mini", "o1 Mini", Id, ModelCapabilities.Text | ModelCapabilities.Streaming, false),
                new("o3-mini", "o3 Mini", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false)
            };
            return Task.FromResult(models);
        }

        public async Task<string> ChatAsync(ModelMetadata model, List<SimpleMessage> history, string? apiKey, CancellationToken ct)
        {
            var llm = new LlmService();
            llm.Configure(Id, apiKey ?? "", "", model.Id);
            return await llm.ChatAsync(history, ct);
        }
    }
}
