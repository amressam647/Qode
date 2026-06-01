using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    /// <summary>
    /// Self-Critique Loop - Agent reviews its own work before finalizing.
    /// Produces self_critique.md with quality assessment.
    /// </summary>
    public class SelfCritiqueService
    {
        private readonly LlmService _llmService;
        private readonly string _workspacePath;

        public SelfCritiqueService(LlmService llmService, string workspacePath)
        {
            _llmService = llmService;
            _workspacePath = workspacePath;
        }

        /// <summary>
        /// Runs self-critique after execution completes.
        /// </summary>
        public async Task<CritiqueResult> CritiqueAsync(
            string originalGoal,
            List<string> toolsUsed,
            List<string> filesModified,
            string executionLog)
        {
            var prompt = GetCritiquePrompt(originalGoal, toolsUsed, filesModified, executionLog);

            var history = new List<SimpleMessage>
            {
                new SimpleMessage { Role = "system", Content = GetCriticSystemPrompt() },
                new SimpleMessage { Role = "user", Content = prompt }
            };

            var response = await _llmService.ChatAsync(history);
            var result = ParseCritique(response);

            // Save critique to file
            SaveCritique(result, originalGoal);

            return result;
        }

        private string GetCriticSystemPrompt()
        {
            return @"
You are a CRITIC agent. Your job is to review completed work with a critical eye.
You are NOT the executor. You review what was done.

Evaluate:
1. MINIMAL CHANGE: Were the changes the smallest possible to achieve the goal?
2. TOOL EFFICIENCY: Could fewer tools have been used?
3. RISK ASSESSMENT: Were there any unmentioned risks?
4. QUALITY: Is the code clean, tested, documented?
5. COMPLETENESS: Did the execution fully satisfy the goal?

Be honest and constructive. Don't be harsh, but don't be lenient either.
";
        }

        private string GetCritiquePrompt(string goal, List<string> tools, List<string> files, string log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## ORIGINAL GOAL");
            sb.AppendLine(goal);
            sb.AppendLine();
            sb.AppendLine("## TOOLS USED");
            foreach (var tool in tools) sb.AppendLine($"- {tool}");
            sb.AppendLine();
            sb.AppendLine("## FILES MODIFIED");
            foreach (var file in files) sb.AppendLine($"- {file}");
            sb.AppendLine();
            sb.AppendLine("## EXECUTION LOG (last 2000 chars)");
            var logTrimmed = log.Length > 2000 ? log.Substring(log.Length - 2000) : log;
            sb.AppendLine(logTrimmed);
            sb.AppendLine();
            sb.AppendLine("Please provide your critique in the following format:");
            sb.AppendLine(@"
## SCORE: [1-10]

## MINIMAL CHANGE
[Analysis]

## TOOL EFFICIENCY
[Analysis]

## RISK ASSESSMENT
[Any unmentioned risks]

## QUALITY
[Code quality assessment]

## COMPLETENESS
[Was the goal fully achieved?]

## RECOMMENDATIONS
- [Recommendation 1]
- [Recommendation 2]
");
            return sb.ToString();
        }

        private CritiqueResult ParseCritique(string response)
        {
            var result = new CritiqueResult
            {
                RawCritique = response,
                GeneratedAt = DateTime.Now
            };

            // Parse score
            var scoreMatch = System.Text.RegularExpressions.Regex.Match(
                response, @"##\s*SCORE:\s*(\d+)");
            if (scoreMatch.Success)
                result.Score = int.Parse(scoreMatch.Groups[1].Value);

            // Check for concerns
            result.HasRiskConcerns = response.ToUpper().Contains("RISK") && 
                                      !response.ToUpper().Contains("NO RISK") &&
                                      !response.ToUpper().Contains("LOW RISK");

            result.HasEfficiencyConcerns = response.ToUpper().Contains("COULD HAVE") ||
                                           response.ToUpper().Contains("UNNECESSARY") ||
                                           response.ToUpper().Contains("TOO MANY");

            result.IsComplete = !response.ToUpper().Contains("INCOMPLETE") &&
                                !response.ToUpper().Contains("NOT FULLY") &&
                                !response.ToUpper().Contains("MISSING");

            return result;
        }

        private void SaveCritique(CritiqueResult result, string goal)
        {
            var critiquePath = Path.Combine(_workspacePath, "self_critique.md");

            var content = new StringBuilder();
            content.AppendLine($"# Self-Critique Report");
            content.AppendLine($"Generated: {result.GeneratedAt:O}");
            content.AppendLine($"Goal: {goal?.Substring(0, Math.Min(100, goal?.Length ?? 0))}...");
            content.AppendLine();
            content.AppendLine($"## Overall Score: {result.Score}/10");
            content.AppendLine();
            content.AppendLine($"| Concern | Status |");
            content.AppendLine($"|---------|--------|");
            content.AppendLine($"| Risk | {(result.HasRiskConcerns ? "⚠️ Concerns" : "✅ OK")} |");
            content.AppendLine($"| Efficiency | {(result.HasEfficiencyConcerns ? "⚠️ Concerns" : "✅ OK")} |");
            content.AppendLine($"| Completeness | {(result.IsComplete ? "✅ Complete" : "❌ Incomplete")} |");
            content.AppendLine();
            content.AppendLine("## Detailed Critique");
            content.AppendLine();
            content.AppendLine(result.RawCritique);

            File.WriteAllText(critiquePath, content.ToString());
        }
    }

    public class CritiqueResult
    {
        public string RawCritique { get; set; }
        public int Score { get; set; }
        public bool HasRiskConcerns { get; set; }
        public bool HasEfficiencyConcerns { get; set; }
        public bool IsComplete { get; set; }
        public DateTime GeneratedAt { get; set; }
    }
}
