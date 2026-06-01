using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class OllamaProvider : IAIProvider
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        public string Id => "Ollama";
        public string Name => "Ollama (Local)";

        public async Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.TrimEnd('/');
            var models = new List<ModelMetadata>();

            try
            {
                var response = await _http.GetAsync($"{baseUrl}/api/tags");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var modelList))
                    {
                        foreach (var m in modelList.EnumerateArray())
                        {
                            var name = m.GetProperty("name").GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                // Smart Capability Mapping based on name
                                var caps = ModelCapabilities.Text | ModelCapabilities.Streaming;
                                if (name.Contains("vision") || name.Contains("llava")) caps |= ModelCapabilities.Vision;
                                if (name.Contains("command") || name.Contains("code") || name.Contains("agent")) caps |= ModelCapabilities.ToolCalling;

                                models.Add(new ModelMetadata(
                                    Id: name,
                                    Name: name.Contains(":") ? name.Split(':')[0] : name, // Display Name
                                    ProviderId: Id,
                                    Capabilities: caps,
                                    IsLocal: true
                                ));
                            }
                        }
                    }
                }
            }
            catch { /* Ollama might not be running, return empty list instead of crashing */ }

            return models;
        }

        public async Task<string> ChatAsync(ModelMetadata model, List<SimpleMessage> history, string? apiKey, CancellationToken ct)
        {
            var llm = new LlmService();
            llm.Configure(Id, "", "", model.Id);
            return await llm.ChatAsync(history, ct);
        }
    }
}
