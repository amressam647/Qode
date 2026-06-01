
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    public class ModelDiscoveryService
    {
        private readonly HttpClient _http;
        public string? LastError { get; private set; }

        public ModelDiscoveryService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        private static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "http://localhost:11434";
            
            url = url.Trim().TrimEnd('/');
            
            // Ensure protocol is present
            if (!url.Contains("://"))
            {
                url = "http://" + url;
            }
            
            return url;
        }

        /// <summary>
        /// Discovers available models. Local models are prioritized first.
        /// </summary>
        public async Task<List<ModelInfo>> DiscoverModelsAsync()
        {
            var allModels = new List<ModelInfo>();
            allModels.AddRange(await DiscoverModelsForProviderAsync("Ollama"));
            allModels.AddRange(await DiscoverModelsForProviderAsync("LM Studio"));
            allModels.AddRange(await DiscoverModelsForProviderAsync("OpenAI"));
            allModels.AddRange(await DiscoverModelsForProviderAsync("Google Gemini"));
            allModels.AddRange(await DiscoverModelsForProviderAsync("Grok"));
            return allModels;
        }

        /// <summary>
        /// Discovers models for a specific provider only.
        /// </summary>
        /// <param name="provider">Provider name (Ollama, LM Studio, etc.)</param>
        /// <param name="customEndpoint">Optional custom base URL for local providers (e.g. http://localhost:11434)</param>
        public async Task<List<ModelInfo>> DiscoverModelsForProviderAsync(string provider, string? customEndpoint = null)
        {
            LastError = null;
            var models = new List<ModelInfo>();

            switch (provider)
            {
                case "Ollama":
                    var ollamaBase = NormalizeBaseUrl(customEndpoint ?? "http://localhost:11434");
                    bool success = false;
                    
                    try
                    {
                        // 1. Try primary endpoint
                        var response = await _http.GetAsync($"{ollamaBase}/api/tags");
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("models", out var modelList))
                            {
                                foreach (var m in modelList.EnumerateArray())
                                {
                                    var nameEl = m.GetProperty("name");
                                    var name = nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() : nameEl.ToString();
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        models.Add(new ModelInfo { Name = name, Provider = "Ollama", BaseUrl = ollamaBase, IsLocal = true });
                                    }
                                }
                            }
                            success = true;
                        }
                        else
                        {
                            LastError = $"Ollama error: {response.StatusCode}";
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = $"Connection failed: {ex.Message}";
                    }

                    // 2. Fallback to OpenAI-compatible endpoint if first failed
                    if (!success)
                    {
                        try
                        {
                            var response = await _http.GetAsync($"{ollamaBase}/v1/models");
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
                                            models.Add(new ModelInfo { Name = id, Provider = "Ollama", BaseUrl = ollamaBase, IsLocal = true });
                                    }
                                }
                                success = true;
                                LastError = null;
                            }
                        }
                        catch { /* Ignore fallback errors */ }
                    }

                    // 3. Last resort: Try 127.0.0.1 if localhost failed
                    if (!success && ollamaBase.Contains("localhost"))
                    {
                        try
                        {
                            var altBase = ollamaBase.Replace("localhost", "127.0.0.1");
                            var response = await _http.GetAsync($"{altBase}/api/tags");
                            if (response.IsSuccessStatusCode)
                            {
                                var json = await response.Content.ReadAsStringAsync();
                                using var doc = JsonDocument.Parse(json);
                                if (doc.RootElement.TryGetProperty("models", out var modelList))
                                {
                                    foreach (var m in modelList.EnumerateArray())
                                    {
                                        var nameEl = m.GetProperty("name");
                                        var name = nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() : nameEl.ToString();
                                        if (!string.IsNullOrEmpty(name))
                                            models.Add(new ModelInfo { Name = name, Provider = "Ollama", BaseUrl = altBase, IsLocal = true });
                                    }
                                }
                                success = true;
                                LastError = null;
                            }
                        }
                        catch { /* Ignore fallback errors */ }
                    }

                    if (!success && string.IsNullOrEmpty(LastError))
                    {
                        LastError = "Could not connect to Ollama. Make sure it is running (ollama serve).";
                    }
                    else if (!success)
                    {
                        // Clean up technical messages for the user
                        if (LastError.Contains("No connection could be made"))
                            LastError = "Connection failed: Ollama server not found. Is it running?";
                    }
                    break;

                case "LM Studio":
                    var lmBase = NormalizeBaseUrl(customEndpoint ?? "http://localhost:1234");
                    try
                    {
                        var response = await _http.GetAsync($"{lmBase}/v1/models");
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("data", out var dataList))
                            {
                                foreach (var m in dataList.EnumerateArray())
                                {
                                    var id = m.GetProperty("id").GetString();
                                    models.Add(new ModelInfo 
                                    { 
                                        Name = id, 
                                        Provider = "LM Studio", 
                                        BaseUrl = lmBase.TrimEnd('/') + "/v1",
                                        IsLocal = true
                                    });
                                }
                            }
                        }
                        else
                        {
                            LastError = $"LM Studio returned error: {response.StatusCode}";
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = $"LM Studio connection failed: {ex.Message}";
                    }
                    break;

                case "OpenAI":
                    models.Add(new ModelInfo { Name = "gpt-4-turbo", Provider = "OpenAI", BaseUrl = "https://api.openai.com/v1", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gpt-3.5-turbo", Provider = "OpenAI", BaseUrl = "https://api.openai.com/v1", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gpt-4o", Provider = "OpenAI", BaseUrl = "https://api.openai.com/v1", IsLocal = false });
                    break;

                case "Google Gemini":
                    models.Add(new ModelInfo { Name = "gemini-2.5-flash", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gemini-2.5-pro", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gemini-2.0-flash", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gemini-1.5-pro", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gemini-1.5-flash", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gemini-1.5-flash-8b", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    models.Add(new ModelInfo { Name = "gemini-pro", Provider = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com", IsLocal = false });
                    break;

                    break;

                case "Grok":
                    models.Add(new ModelInfo { Name = "grok-3-mini", Provider = "Grok", BaseUrl = "https://api.x.ai/v1", IsLocal = false });
                    models.Add(new ModelInfo { Name = "grok-3", Provider = "Grok", BaseUrl = "https://api.x.ai/v1", IsLocal = false });
                    models.Add(new ModelInfo { Name = "grok-4-1-fast-reasoning", Provider = "Grok", BaseUrl = "https://api.x.ai/v1", IsLocal = false });
                    break;
            }

            return models;
        }
    }
}
