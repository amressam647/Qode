using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class GrokProvider : IAIProvider
    {
        public string Id => "Grok";
        public string Name => "Grok x.ai (Cloud)";

        public Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var models = new List<ModelMetadata>
            {
                new("grok-2-1212", "Grok 2", Id, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("grok-beta", "Grok Beta", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false)
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
