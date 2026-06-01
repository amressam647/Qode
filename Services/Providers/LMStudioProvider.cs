using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Providers
{
    public class LMStudioProvider : IAIProvider
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        public string Id => "LM Studio";
        public string Name => "LM Studio (Local)";

        public async Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null)
        {
            var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:1234" : endpoint.TrimEnd('/');
            var models = new List<ModelMetadata>();

            try
            {
                var response = await _http.GetAsync($"{baseUrl}/v1/models");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var dataList))
                    {
                        foreach (var m in dataList.EnumerateArray())
                        {
                            var id = m.GetProperty("id").GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                var caps = ModelCapabilities.Text | ModelCapabilities.Streaming;
                                if (id.Contains("vision")) caps |= ModelCapabilities.Vision;
                                if (id.Contains("instruct") || id.Contains("agent") || id.Contains("tool")) caps |= ModelCapabilities.ToolCalling;

                                models.Add(new ModelMetadata(
                                    Id: id,
                                    Name: id.Contains("/") ? System.IO.Path.GetFileName(id) : id,
                                    ProviderId: Id,
                                    Capabilities: caps,
                                    IsLocal: true
                                ));
                            }
                        }
                    }
                }
            }
            catch { /* LM Studio might not be running, return empty list */ }

            return models;
        }

        public async Task<string> ChatAsync(ModelMetadata model, List<SimpleMessage> history, string? apiKey, CancellationToken ct)
        {
            var llm = new LlmService();
            llm.Configure(Id, apiKey ?? "", "", model.Id);
            return await llm.ChatAsync(history, ct);
        }
    }
}
