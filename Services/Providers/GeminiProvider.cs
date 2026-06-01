using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class GeminiProvider : IAIProvider
    {
        public string Id => "Google Gemini";
        public string Name => "Google Gemini (Cloud)";

        public Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            // Static list for now as Gemini API doesn't always expose all models via discovery easily without specific keys
            var models = new List<ModelMetadata>
            {
                new("gemini-2.0-flash", "Gemini 2.0 Flash", Id, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Streaming, false),
                new("gemini-1.5-pro", "Gemini 1.5 Pro", Id, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Streaming, false),
                new("gemini-1.5-flash", "Gemini 1.5 Flash", Id, ModelCapabilities.Text | ModelCapabilities.Vision | ModelCapabilities.Streaming, false)
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
