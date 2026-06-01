using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    public class StreamingLlmService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl = "http://localhost:11434/api/chat";
        private string _apiKey = "";
        private string _model = "llama3";
        private bool _isOpenAiStyle = false;

        public StreamingLlmService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public void Configure(string provider, string key, string url, string model)
        {
            _apiKey = key;
            _model = model;

            if (provider.ToLower() == "openai")
            {
                _baseUrl = "https://api.openai.com/v1/chat/completions";
                _isOpenAiStyle = true;
            }
            else if (provider.ToLower() == "ollama")
            {
                _baseUrl = string.IsNullOrEmpty(url) ? "http://localhost:11434/api/chat" : url;
                _isOpenAiStyle = false;
            }
            else if (provider.ToLower() == "custom")
            {
                _baseUrl = url;
                _isOpenAiStyle = true;
            }
        }

        /// <summary>
        /// Streams chat response, calling onToken for each token received.
        /// </summary>
        public async Task<string> ChatStreamingAsync(
            List<SimpleMessage> history,
            Action<string> onToken,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                model = _model,
                messages = history,
                stream = true
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
            request.Content = content;

            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            }

            try
            {
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return $"Error: {response.StatusCode} - {errorBody}";
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var fullResponse = new StringBuilder();

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;

                    var token = ParseStreamLine(line);
                    if (!string.IsNullOrEmpty(token))
                    {
                        fullResponse.Append(token);
                        onToken?.Invoke(token);
                    }
                }

                return fullResponse.ToString();
            }
            catch (OperationCanceledException)
            {
                return "[Cancelled]";
            }
            catch (Exception ex)
            {
                return $"Stream Error: {ex.Message}";
            }
        }

        private string ParseStreamLine(string line)
        {
            try
            {
                if (_isOpenAiStyle)
                {
                    // OpenAI format: data: {"choices":[{"delta":{"content":"..."}}]}
                    if (!line.StartsWith("data: ")) return null;
                    var jsonPart = line.Substring(6);
                    if (jsonPart == "[DONE]") return null;

                    using var doc = JsonDocument.Parse(jsonPart);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var contentProp))
                        {
                            return contentProp.GetString();
                        }
                    }
                }
                else
                {
                    // Ollama format: {"message":{"content":"..."}}
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("message", out var msg))
                    {
                        if (msg.TryGetProperty("content", out var contentProp))
                        {
                            return contentProp.GetString();
                        }
                    }
                }
            }
            catch
            {
                // Ignore parse errors for SSE
            }
            return null;
        }
    }
}
