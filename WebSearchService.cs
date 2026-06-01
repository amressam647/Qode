using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    /// <summary>
    /// Searches the web via DuckDuckGo Instant Answer API (free, no API key).
    /// Use when the agent lacks information and needs to look it up.
    /// </summary>
    public class WebSearchService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>
        /// Search the web and return formatted results for the LLM.
        /// </summary>
        public async Task<string> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Error: Empty search query.";

            try
            {
                var url = "https://api.duckduckgo.com/?q=" + Uri.EscapeDataString(query.Trim()) + "&format=json&no_html=1";
                using var response = await _http.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var sb = new StringBuilder();
                sb.AppendLine($"[Web Search: {query}]");
                sb.AppendLine();

                // Abstract (main result)
                if (root.TryGetProperty("Abstract", out var abs) && !string.IsNullOrEmpty(abs.GetString()))
                {
                    sb.AppendLine("--- Summary ---");
                    sb.AppendLine(abs.GetString());
                    if (root.TryGetProperty("AbstractURL", out var absUrl))
                        sb.AppendLine($"Source: {absUrl.GetString()}");
                    sb.AppendLine();
                }

                // RelatedTopics
                if (root.TryGetProperty("RelatedTopics", out var related) && related.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("--- Related ---");
                    int count = 0;
                    foreach (var item in related.EnumerateArray())
                    {
                        if (count >= 5) break;
                        string text = null, urlStr = null;
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            if (item.TryGetProperty("Text", out var t)) text = t.GetString();
                            if (item.TryGetProperty("FirstURL", out var u)) urlStr = u.GetString();
                        }
                        else if (item.ValueKind == JsonValueKind.String)
                        {
                            text = item.GetString();
                        }
                        if (!string.IsNullOrEmpty(text))
                        {
                            sb.AppendLine($"• {text}");
                            if (!string.IsNullOrEmpty(urlStr))
                                sb.AppendLine($"  {urlStr}");
                            count++;
                        }
                    }
                    sb.AppendLine();
                }

                // Results (web links)
                if (root.TryGetProperty("Results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    sb.AppendLine("--- Web Results ---");
                    int count = 0;
                    foreach (var r in results.EnumerateArray())
                    {
                        if (count >= 5) break;
                        var text = r.TryGetProperty("Text", out var t) ? t.GetString() : null;
                        var firstUrl = r.TryGetProperty("FirstURL", out var u) ? u.GetString() : null;
                        var title = r.TryGetProperty("Title", out var ti) ? ti.GetString() : null;
                        if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(title))
                        {
                            sb.AppendLine($"• {title ?? text}");
                            if (!string.IsNullOrEmpty(text) && text != title) sb.AppendLine($"  {text}");
                            if (!string.IsNullOrEmpty(firstUrl)) sb.AppendLine($"  {firstUrl}");
                            count++;
                        }
                    }
                }

                if (sb.Length <= query.Length + 30)
                    return $"[Web Search: {query}]\n\nNo results found. Try a different search phrase or be more specific.";

                return sb.ToString().TrimEnd();
            }
            catch (HttpRequestException ex)
            {
                return $"Web search failed (network): {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                return "Web search timed out.";
            }
            catch (Exception ex)
            {
                return $"Web search error: {ex.Message}";
            }
        }
    }
}
