using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LocalCursor.Services
{
    /// <summary>
    /// Separates planning from execution for better control and reduced hallucinations.
    /// </summary>
    public class PlannerService
    {
        private readonly LlmService _llmService;

        public PlannerService(LlmService llmService)
        {
            _llmService = llmService;
        }

        /// <summary>
        /// Generates a structured plan from a user request.
        /// </summary>
        public async Task<ExecutionPlan> CreatePlanAsync(string userRequest, string projectContext)
        {
            var prompt = GetPlannerPrompt(userRequest, projectContext);
            
            var history = new List<SimpleMessage>
            {
                new SimpleMessage { Role = "system", Content = prompt },
                new SimpleMessage { Role = "user", Content = userRequest }
            };

            var response = await _llmService.ChatAsync(history);
            return ParsePlan(response);
        }

        /// <summary>
        /// Reviews a plan execution result.
        /// </summary>
        public async Task<ReviewResult> ReviewExecutionAsync(ExecutionPlan plan, string executionLog)
        {
            var prompt = GetReviewerPrompt();
            
            var history = new List<SimpleMessage>
            {
                new SimpleMessage { Role = "system", Content = prompt },
                new SimpleMessage { Role = "user", Content = $"Plan:\n{plan}\n\nExecution Log:\n{executionLog}" }
            };

            var response = await _llmService.ChatAsync(history);
            return ParseReview(response);
        }

        private string GetPlannerPrompt(string request, string context)
        {
            return $@"
You are a PLANNING agent. Your job is to create a structured execution plan.
DO NOT execute anything. Just plan.

=== PROJECT CONTEXT ===
{context}

=== OUTPUT FORMAT ===
Create a plan in this exact format:

## GOAL
[One sentence describing the goal]

## PRE-CHECKS
- [ ] Check 1
- [ ] Check 2

## STEPS
1. [ACTION] [Description] [RISK: Safe/Medium/Dangerous]
2. [ACTION] [Description] [RISK: Safe/Medium/Dangerous]
...

## VERIFICATION
- [ ] Verify step 1
- [ ] Verify step 2

## ROLLBACK (if something fails)
- Rollback step 1
- Rollback step 2

=== RULES ===
1. Break complex tasks into atomic steps
2. Each step should be one tool call
3. Mark risk level for each step
4. Include verification steps
5. Always plan a rollback strategy
";
        }

        private string GetReviewerPrompt()
        {
            return @"
You are a REVIEW agent. Your job is to validate execution results.

Analyze the execution log and provide:

## STATUS
[SUCCESS/PARTIAL/FAILED]

## ISSUES
- Issue 1
- Issue 2

## RECOMMENDATIONS
- Recommendation 1
- Recommendation 2

## NEXT STEPS
- [ ] Step 1
- [ ] Step 2

Be critical but constructive.
";
        }

        private ExecutionPlan ParsePlan(string response)
        {
            var plan = new ExecutionPlan
            {
                RawPlan = response,
                Steps = new List<PlanStep>()
            };

            // Parse GOAL
            var goalMatch = System.Text.RegularExpressions.Regex.Match(
                response, @"## GOAL\s*\n(.+?)(?=\n##|\n\n|$)", 
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (goalMatch.Success)
                plan.Goal = goalMatch.Groups[1].Value.Trim();

            // Parse STEPS
            var stepsMatch = System.Text.RegularExpressions.Regex.Match(
                response, @"## STEPS\s*\n([\s\S]+?)(?=\n##|$)");
            if (stepsMatch.Success)
            {
                var lines = stepsMatch.Groups[1].Value.Split('\n');
                int stepNum = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || !char.IsDigit(trimmed[0])) continue;

                    var step = new PlanStep
                    {
                        Order = ++stepNum,
                        Description = trimmed,
                        Risk = trimmed.ToUpper().Contains("DANGEROUS") ? ToolRiskLevel.Dangerous
                             : trimmed.ToUpper().Contains("MEDIUM") ? ToolRiskLevel.Medium
                             : ToolRiskLevel.Safe
                    };
                    plan.Steps.Add(step);
                }
            }

            return plan;
        }

        private ReviewResult ParseReview(string response)
        {
            var result = new ReviewResult { RawReview = response };

            // Parse STATUS
            if (response.ToUpper().Contains("SUCCESS"))
                result.Status = ReviewStatus.Success;
            else if (response.ToUpper().Contains("FAILED"))
                result.Status = ReviewStatus.Failed;
            else
                result.Status = ReviewStatus.Partial;

            // Count issues
            result.IssueCount = System.Text.RegularExpressions.Regex.Matches(
                response, @"^\s*-\s", System.Text.RegularExpressions.RegexOptions.Multiline).Count;

            return result;
        }
    }

    public class ExecutionPlan
    {
        public string Goal { get; set; } = "";
        public string RawPlan { get; set; } = "";
        public List<PlanStep> Steps { get; set; } = new();
        public bool IsApproved { get; set; }

        public override string ToString()
        {
            return $"Goal: {Goal}\nSteps: {Steps.Count}\n---\n{RawPlan}";
        }
    }

    public class PlanStep
    {
        public int Order { get; set; }
        public string Description { get; set; } = "";
        public ToolRiskLevel Risk { get; set; }
        public bool IsCompleted { get; set; }
        public string Result { get; set; } = "";
    }

    public class ReviewResult
    {
        public ReviewStatus Status { get; set; }
        public string RawReview { get; set; } = "";
        public int IssueCount { get; set; }
    }

    public enum ReviewStatus
    {
        Success,
        Partial,
        Failed
    }
}
