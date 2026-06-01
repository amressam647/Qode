using System.Collections.Generic;
using System.Linq;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    /// <summary>
    /// Prompt size governor - manages token budget and trims context intelligently.
    /// </summary>
    public class PromptGovernor
    {
        private readonly int _maxTokens;
        private readonly int _reservedForOutput;
        
        // Priority levels for content trimming
        private const int PRIORITY_CRITICAL = 100;   // System prompt, safety rules
        private const int PRIORITY_HIGH = 75;        // Current task, recent errors
        private const int PRIORITY_MEDIUM = 50;      // Project context, memory
        private const int PRIORITY_LOW = 25;         // Old chat, verbose output

        public PromptGovernor(int maxContextTokens = 8000, int reservedForOutput = 2000)
        {
            _maxTokens = maxContextTokens;
            _reservedForOutput = reservedForOutput;
        }

        /// <summary>
        /// Trims chat history to fit within token budget.
        /// </summary>
        public List<SimpleMessage> TrimHistory(List<SimpleMessage> history, string? projectContext = null)
        {
            var result = new List<SimpleMessage>();
            int currentTokens = 0;
            int availableTokens = _maxTokens - _reservedForOutput;

            // Always keep system prompt (first message)
            if (history.Count > 0 && history[0].Role == "system")
            {
                var systemTokens = EstimateTokens(history[0].Content ?? "");
                result.Add(history[0]);
                currentTokens += systemTokens;
            }

            // Categorize messages by priority
            var prioritizedMessages = new List<(SimpleMessage Msg, int Priority, int Tokens)>();

            for (int i = 1; i < history.Count; i++)
            {
                var msg = history[i];
                var tokens = EstimateTokens(msg.Content ?? "");
                var priority = GetMessagePriority(msg, i, history.Count);
                prioritizedMessages.Add((msg, priority, tokens));
            }

            // Sort by priority (highest first) and recency (newest first for same priority)
            var ordered = prioritizedMessages
                .Select((m, idx) => (m.Msg, m.Priority, m.Tokens, Index: idx))
                .OrderByDescending(m => m.Priority)
                .ThenByDescending(m => m.Index)
                .ToList();

            // Add messages until budget exhausted
            var messagesToAdd = new List<(SimpleMessage Msg, int Index)>();

            foreach (var (msg, priority, tokens, index) in ordered)
            {
                if (currentTokens + tokens <= availableTokens)
                {
                    messagesToAdd.Add((msg, index));
                    currentTokens += tokens;
                }
                else if (priority >= PRIORITY_HIGH)
                {
                    // For high priority, try to fit a trimmed version
                    var trimmedContent = TrimContent(msg.Content, (availableTokens - currentTokens) * 3); // ~3 chars per token
                    if (!string.IsNullOrEmpty(trimmedContent))
                    {
                        messagesToAdd.Add((new SimpleMessage { Role = msg.Role, Content = trimmedContent }, index));
                        currentTokens += EstimateTokens(trimmedContent);
                    }
                }
            }

            // Sort back into original order
            var orderedToAdd = messagesToAdd.OrderBy(m => m.Index).Select(m => m.Msg);
            result.AddRange(orderedToAdd);

            return result;
        }

        /// <summary>
        /// Trims tool output to reasonable size.
        /// </summary>
        public string TrimToolOutput(string output, int maxChars = 2000)
        {
            if (string.IsNullOrEmpty(output) || output.Length <= maxChars)
                return output;

            // Keep first and last parts
            var firstPart = output.Substring(0, maxChars / 2);
            var lastPart = output.Substring(output.Length - maxChars / 2);

            return $"{firstPart}\n\n... [TRUNCATED {output.Length - maxChars} chars] ...\n\n{lastPart}";
        }

        /// <summary>
        /// Gets current token estimate.
        /// </summary>
        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Rough estimate: ~4 chars per token for English
            return text.Length / 4;
        }

        /// <summary>
        /// Gets budget status.
        /// </summary>
        public (int Used, int Available, double PercentageUsed) GetBudgetStatus(List<SimpleMessage> history)
        {
            int used = history.Sum(m => EstimateTokens(m.Content));
            int available = _maxTokens - _reservedForOutput;
            double percentage = (double)used / available * 100;

            return (used, available, percentage);
        }

        private int GetMessagePriority(SimpleMessage msg, int index, int totalCount)
        {
            var content = (msg.Content ?? "").ToUpper();

            // Critical content
            if (msg.Role == "system") return PRIORITY_CRITICAL;
            if (content.Contains("[SECURITY")) return PRIORITY_CRITICAL;
            if (content.Contains("ERROR") || content.Contains("FAIL")) return PRIORITY_HIGH;

            // High priority - recent messages
            double recencyRatio = (double)index / totalCount;
            if (recencyRatio > 0.8) return PRIORITY_HIGH; // Last 20%

            // Medium priority - task-related
            if (content.Contains("TASK") || content.Contains("PLAN")) return PRIORITY_MEDIUM;
            if (content.Contains("[TOOL_OUTPUT]")) return PRIORITY_MEDIUM;

            // Low priority - old chat, verbose output
            if (recencyRatio < 0.3) return PRIORITY_LOW; // First 30%
            if ((msg.Content?.Length ?? 0) > 1000) return PRIORITY_LOW; // Very long messages

            return PRIORITY_MEDIUM;
        }

        private string? TrimContent(string content, int maxChars)
        {
            if (content.Length <= maxChars) return content;
            if (maxChars < 100) return null; // Too small to be useful

            // Try to trim at a sentence boundary
            var trimmed = content.Substring(0, maxChars);
            var lastPeriod = trimmed.LastIndexOf('.');
            if (lastPeriod > maxChars / 2)
            {
                return trimmed.Substring(0, lastPeriod + 1) + " [TRIMMED]";
            }

            return trimmed + "... [TRIMMED]";
        }
    }
}
