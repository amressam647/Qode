using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class DeepSeekProvider : IAIProvider
    {
        public string Id => "DeepSeek";
        public string Name => "DeepSeek (Cloud)";

        public Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var models = new List<ModelMetadata>
            {
                new("deepseek-chat", "DeepSeek V3", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("deepseek-reasoner", "DeepSeek R1 (Reasoning)", Id, ModelCapabilities.Text | ModelCapabilities.Streaming, false)
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
