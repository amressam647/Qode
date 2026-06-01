using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class GroqProvider : IAIProvider
    {
        public string Id => "Groq";
        public string Name => "Groq (Cloud)";

        public Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var models = new List<ModelMetadata>
            {
                new("llama-3.3-70b-versatile", "Llama 3.3 70B Versatile", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("deepseek-r1-distill-llama-70b", "DeepSeek R1 (Llama 70B Distilled)", Id, ModelCapabilities.Text | ModelCapabilities.Streaming, false),
                new("mixtral-8x7b-32768", "Mixtral 8x7B", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false),
                new("gemma2-9b-it", "Gemma 2 9B IT", Id, ModelCapabilities.Text | ModelCapabilities.Streaming | ModelCapabilities.ToolCalling, false)
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
