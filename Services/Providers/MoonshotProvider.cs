using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class MoonshotProvider : IAIProvider
    {
        public string Id => "Moonshot";
        public string Name => "Moonshot Kimi (Cloud)";

        public Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var models = new List<ModelMetadata>
            {
                new("moonshot-v1-8k", "Moonshot V1 8K", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("moonshot-v1-32k", "Moonshot V1 32K", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false)
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
