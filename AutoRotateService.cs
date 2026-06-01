using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    /// <summary>
    /// Automatically rotates between AI models when quota is exhausted or errors occur.
    /// Similar to Cursor's "auto" model feature.
    /// </summary>
    public class AutoRotateService
    {
        private readonly LlmService _llmService;
        private List<RotateEntry> _chain = new();
        private int _currentIndex = 0;

        /// <summary>Fired when the active model changes due to rotation.</summary>
        public event Action<string, string>? OnModelRotated; // (oldModel, newModel)
        
        /// <summary>Fired when the active model changes, passing the history to allow modifying it in-place.</summary>
        public event Action<string, string, List<SimpleMessage>>? OnModelRotatedWithHistory;

        public string CurrentModel => _currentIndex < _chain.Count ? _chain[_currentIndex].Model : "";
        public string CurrentProvider => _currentIndex < _chain.Count ? _chain[_currentIndex].Provider : "";

        public AutoRotateService(LlmService llmService)
        {
            _llmService = llmService;
        }

        /// <summary>
        /// Configure the fallback chain. Primary model is first.
        /// </summary>
        public void ConfigureChain(List<RotateEntry> chain)
        {
            _chain = chain ?? new List<RotateEntry>();
            _currentIndex = 0;
            ApplyCurrent();
        }

        /// <summary>
        /// Sets a simple default chain: Gemini Flash → Grok → OpenAI GPT-4o-mini → DeepSeek
        /// </summary>
        public void SetDefaultChain(
            string geminiKey = "", string grokKey = "",
            string openAiKey = "", string deepSeekKey = "",
            string groqKey = "")
        {
            var chain = new List<RotateEntry>();

            if (!string.IsNullOrEmpty(geminiKey))
                chain.Add(new RotateEntry { Provider = "Google Gemini", Model = "gemini-2.5-flash-preview-04-17", ApiKey = geminiKey });
            if (!string.IsNullOrEmpty(groqKey))
                chain.Add(new RotateEntry { Provider = "Groq", Model = "llama-3.3-70b-versatile", ApiKey = groqKey });
            if (!string.IsNullOrEmpty(grokKey))
                chain.Add(new RotateEntry { Provider = "Grok", Model = "grok-3-mini", ApiKey = grokKey });
            if (!string.IsNullOrEmpty(openAiKey))
                chain.Add(new RotateEntry { Provider = "OpenAI", Model = "gpt-4o-mini", ApiKey = openAiKey });
            if (!string.IsNullOrEmpty(deepSeekKey))
                chain.Add(new RotateEntry { Provider = "DeepSeek", Model = "deepseek-chat", ApiKey = deepSeekKey });

            _chain = chain;
            _currentIndex = 0;
            ApplyCurrent();
        }

        /// <summary>
        /// Try to get a chat response. If quota/rate-limit error detected, rotate to next model.
        /// Returns (response, usedModel).
        /// </summary>
        public async Task<(string Response, string UsedModel)> ChatWithFallbackAsync(
            List<SimpleMessage> history,
            System.Threading.CancellationToken ct = default)
        {
            if (_chain.Count == 0)
            {
                // No chain configured, just use LLM as-is
                var resp = await _llmService.ChatAsync(history, ct);
                return (resp, "");
            }

            int startIndex = _currentIndex;
            int attempts = 0;

            while (attempts < _chain.Count)
            {
                ApplyCurrent();
                var entry = _chain[_currentIndex];

                try
                {
                    var response = await _llmService.ChatAsync(history, ct);

                    // Detect quota/rate-limit errors in the response text
                    if (IsQuotaError(response))
                    {
                        RotateNext(entry.Model, history);
                        attempts++;
                        continue;
                    }

                    return (response, $"{entry.Provider}/{entry.Model}");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    if (IsQuotaException(ex.Message))
                    {
                        RotateNext(entry.Model, history);
                        attempts++;
                        continue;
                    }
                    throw;
                }
            }

            // All models failed — return error
            return ($"⚠️ All models in the rotation chain failed. Please check your API keys.", "");
        }

        private bool IsQuotaError(string response)
        {
            if (string.IsNullOrEmpty(response)) return false;
            var lower = response.ToLowerInvariant();
            return lower.Contains("rate limit") || lower.Contains("quota") ||
                   lower.Contains("429") || lower.Contains("too many requests") ||
                   lower.Contains("resource_exhausted") || lower.Contains("billing") ||
                   lower.Contains("insufficient_quota") || lower.Contains("overloaded");
        }

        private bool IsQuotaException(string message)
        {
            var lower = message.ToLowerInvariant();
            return lower.Contains("429") || lower.Contains("rate limit") ||
                   lower.Contains("quota") || lower.Contains("too many");
        }

        private void RotateNext(string oldModel, List<SimpleMessage> history)
        {
            _currentIndex = (_currentIndex + 1) % _chain.Count;
            var entry = _chain[_currentIndex];
            var newModel = entry.Model;
            OnModelRotated?.Invoke(oldModel, newModel);
            OnModelRotatedWithHistory?.Invoke(oldModel, $"{entry.Provider}/{newModel}", history);
            ApplyCurrent();
        }

        private void ApplyCurrent()
        {
            if (_currentIndex < _chain.Count)
            {
                var e = _chain[_currentIndex];
                _llmService.Configure(e.Provider, e.ApiKey, e.BaseUrl ?? "", e.Model);
            }
        }

        public void ResetToFirst()
        {
            _currentIndex = 0;
            ApplyCurrent();
        }
    }

    public class RotateEntry
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string? BaseUrl { get; set; }
    }
}
