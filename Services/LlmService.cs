using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    public class LlmService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl = "http://localhost:11434/api/chat";
        private string _apiKey = "";
        private string _model = "llama3";
        private bool _isOpenAiStyle = false;
        private bool _isGemini = false;
        private bool _isOllama = false;
        private bool _isGrok = false;
        private bool _isAnthropic = false;

        public LlmService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public void Configure(string provider, string key, string url, string model)
        {
            _apiKey = key;
            _model = model;

            var providerLower = provider.ToLower();

            _isGemini = false;
            _isOllama = false;
            _isGrok = false;
            _isAnthropic = false;
            _isOpenAiStyle = false;

            if (providerLower == "openai")
            {
                _baseUrl = "https://api.openai.com/v1/chat/completions";
                _isOpenAiStyle = true;
            }
            else if (providerLower == "ollama")
            {
                var baseUrl = string.IsNullOrEmpty(url) ? "http://localhost:11434" : url.Trim().TrimEnd('/');
                if (!baseUrl.Contains("://")) baseUrl = "http://" + baseUrl;
                _baseUrl = baseUrl.EndsWith("/api/chat") ? baseUrl : baseUrl + "/api/chat";
                _isOpenAiStyle = false;
                _isOllama = true;
            }
            else if (providerLower == "lm studio" || providerLower == "custom")
            {
                var baseUrl = string.IsNullOrEmpty(url) ? "http://localhost:1234/v1" : url.TrimEnd('/');
                _baseUrl = baseUrl.EndsWith("/chat/completions") ? baseUrl : baseUrl + "/chat/completions";
                _isOpenAiStyle = true;
            }
            else if (providerLower.Contains("google") || providerLower.Contains("gemini"))
            {
                _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent";
                _isGemini = true;
            }
            else if (providerLower == "deepseek")
            {
                _baseUrl = "https://api.deepseek.com/v1/chat/completions";
                _isOpenAiStyle = true;
            }
            else if (providerLower == "groq")
            {
                _baseUrl = "https://api.groq.com/openai/v1/chat/completions";
                _isOpenAiStyle = true;
            }
            else if (providerLower.Contains("moonshot") || providerLower.Contains("kimi"))
            {
                _baseUrl = "https://api.moonshot.cn/v1/chat/completions";
                _isOpenAiStyle = true;
            }
            else if (providerLower.Contains("grok"))
            {
                _baseUrl = "https://api.x.ai/v1/chat/completions";
                _isOpenAiStyle = true;
                _isGrok = true;
            }
            else
            {
                _baseUrl = string.IsNullOrEmpty(url) ? _baseUrl : url;
                _isOpenAiStyle = true;
            }
        }

        public async Task<string> ChatAsync(List<SimpleMessage> history, CancellationToken cancellationToken = default)
        {
            if (_isGemini && string.IsNullOrWhiteSpace(_apiKey))
                return "⚠️ Gemini يحتاج API Key. افتح الإعدادات ⚙️ واحفظ المفتاح أولاً.";

            string json;
            if (_isGemini)
                json = BuildGeminiRequestBody(history);
            else if (_isOllama)
                json = BuildOllamaRequestBody(history);
            else if (_isGrok)
                json = BuildGrokRequestBody(history);
            else
                json = BuildOpenAiRequestBody(history);

            try
            {
                const int maxRetries = 2;
                string responseString = "";

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    var url = _baseUrl;
                    if (_isGemini && !string.IsNullOrEmpty(_apiKey))
                        url = _baseUrl + (_baseUrl.Contains("?") ? "&" : "?") + "key=" + Uri.EscapeDataString(_apiKey.Trim());
                    var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(_apiKey))
                    {
                        if (_isGemini)
                            req.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey.Trim());
                    }

                    using var response = await _httpClient.SendAsync(req, cancellationToken);
                    responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode) break;

                    // Retry on 503 (Service Unavailable) - often transient with Ollama
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < maxRetries)
                    {
                        await Task.Delay(2000, cancellationToken); // Wait 2s before retry
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        return "⚠️ Service unavailable (503) after 3 attempts.\n\nTry:\n• Wait a minute (Ollama may be loading the model)\n• Restart Ollama: ollama serve\n• Choose a smaller model if memory is full";
                    }
                    return $"Error: {response.StatusCode} - {responseString}";
                }

                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;

                // Parse based on style
                if (_isOpenAiStyle)
                {
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                    // Error from API
                    if (root.TryGetProperty("error", out var errObj))
                        return $"API Error: {errObj.GetProperty("message").GetString()}";
                }
                else if (_isGemini)
                {
                    if (root.TryGetProperty("candidates", out var cand) && cand.GetArrayLength() > 0)
                    {
                        var c = cand[0];
                        if (c.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            return parts[0].GetProperty("text").GetString() ?? "";
                    }
                    if (root.TryGetProperty("error", out var gemErr))
                        return $"Gemini Error: {gemErr.GetProperty("message").GetString()}";
                }
                else
                {
                    // Ollama: message.content
                    if (root.TryGetProperty("message", out var msg))
                        return msg.GetProperty("content").GetString() ?? "";
                }

                return responseString; // Fallback
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

        private string BuildGeminiRequestBody(List<SimpleMessage> history)
        {
            var systemInstruction = "";
            var contents = new List<object>();
            foreach (var m in history)
            {
                if (m.Role == "system")
                {
                    systemInstruction = (systemInstruction + "\n" + m.Content).Trim();
                    continue;
                }
                var role = m.Role == "user" ? "user" : "model";
                var parts = new List<object>();
                if (!string.IsNullOrEmpty(m.AttachedImageBase64) && !string.IsNullOrEmpty(m.AttachedImageMimeType))
                {
                    parts.Add(new { inlineData = new { mimeType = m.AttachedImageMimeType, data = m.AttachedImageBase64 } });
                    parts.Add(new { text = m.Content ?? "" });
                }
                else
                {
                    parts.Add(new { text = m.Content ?? "" });
                }
                contents.Add(new { role, parts });
            }
            var body = new
            {
                systemInstruction = string.IsNullOrEmpty(systemInstruction) ? (object)null : new { parts = new[] { new { text = systemInstruction } } },
                contents,
                generationConfig = new { thinkingConfig = new { thinkingBudget = 0 } }
            };
            return JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        }

        private string BuildOllamaRequestBody(List<SimpleMessage> history)
        {
            var messages = new List<object>();
            foreach (var m in history)
            {
                var msg = new Dictionary<string, object> { ["role"] = m.Role, ["content"] = m.Content ?? "" };
                if (!string.IsNullOrEmpty(m.AttachedImageBase64))
                    msg["images"] = new[] { m.AttachedImageBase64 };
                messages.Add(msg);
            }
            var body = new 
            { 
                model = _model, 
                messages, 
                stream = false,
                options = new { num_ctx = 8192, temperature = 0.2 }
            };
            return JsonSerializer.Serialize(body);
        }

        private string BuildOpenAiRequestBody(List<SimpleMessage> history)
        {
            var messages = new List<object>();
            foreach (var m in history)
            {
                if (m.Role == "system")
                {
                    messages.Add(new { role = m.Role, content = m.Content ?? "" });
                    continue;
                }
                if (!string.IsNullOrEmpty(m.AttachedImageBase64) && !string.IsNullOrEmpty(m.AttachedImageMimeType))
                {
                    var dataUrl = $"data:{m.AttachedImageMimeType};base64,{m.AttachedImageBase64}";
                    var content = new object[]
                    {
                        new { type = "text", text = m.Content ?? "" },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    };
                    messages.Add(new { role = m.Role, content });
                }
                else
                {
                    messages.Add(new { role = m.Role, content = m.Content ?? "" });
                }
            }
            var body = new { model = _model, messages, stream = false };
            return JsonSerializer.Serialize(body);
        }

        /// <summary>x.ai Grok expects content as array [{"type":"text","text":"..."}] - plain string causes BadRequest.</summary>
        private string BuildGrokRequestBody(List<SimpleMessage> history)
        {
            var messages = new List<object>();
            foreach (var m in history)
            {
                if (m.Role == "system")
                {
                    messages.Add(new { role = m.Role, content = new[] { new { type = "text", text = m.Content ?? "" } } });
                    continue;
                }
                if (!string.IsNullOrEmpty(m.AttachedImageBase64) && !string.IsNullOrEmpty(m.AttachedImageMimeType))
                {
                    var dataUrl = $"data:{m.AttachedImageMimeType};base64,{m.AttachedImageBase64}";
                    var content = new object[]
                    {
                        new { type = "text", text = m.Content ?? "" },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    };
                    messages.Add(new { role = m.Role, content });
                }
                else
                {
                    messages.Add(new { role = m.Role, content = new[] { new { type = "text", text = m.Content ?? "" } } });
                }
            }
            var body = new { model = _model, messages, stream = false };
            return JsonSerializer.Serialize(body);
        }

    }
}
