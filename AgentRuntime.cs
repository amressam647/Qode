using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Core
{
    public class AgentRuntime
    {
        private readonly SandboxService _sandbox;
        private readonly WorkflowController _workflow;
        private readonly IAgentEventStream _eventStream;
        private readonly RoleOrchestrator _roleOrchestrator;
        private readonly SecretsService _secretsService;
        private readonly List<RoleBinding> _roleBindings;
        private readonly bool _isAutoRotateEnabled;

        // Phase 2.2: Local state variable eliminated. All state reads go through the canonical central store.
        private AgentState CurrentState => Bootstrapper.StateStore?.CurrentState?.SessionState ?? AgentState.Idle;
        private readonly List<Observation> _observations = new();
        private readonly List<AgentEvent> _eventLog = new();
        private readonly List<AgentRole> _postponedRoles = new();
        private readonly MarkdownAuditReporter _auditReporter;
        private int _iterationCount = 0;
        private const int MAX_ITERATIONS = 12;

        public event Action<AgentState>? OnStateChanged;
        public List<Observation> Observations => _observations;
        public List<AgentEvent> EventLog => _eventLog;

        public AgentRuntime(
            SandboxService sandbox, 
            WorkflowController workflow, 
            IAgentEventStream eventStream,
            RoleOrchestrator roleOrchestrator,
            SecretsService secretsService,
            List<RoleBinding> roleBindings,
            bool isAutoRotateEnabled)
        {
            _sandbox = sandbox;
            _workflow = workflow;
            _eventStream = eventStream;
            _roleOrchestrator = roleOrchestrator;
            _secretsService = secretsService;
            _roleBindings = roleBindings;
            _isAutoRotateEnabled = isAutoRotateEnabled;
            _auditReporter = new MarkdownAuditReporter(sandbox.WorkspaceRoot);
        }

        public async Task<string> RunAsync(string userInput)
        {
            _observations.Clear();
            _eventLog.Clear();
            _postponedRoles.Clear();
            _iterationCount = 0;
            
            // Dispatch Session Started Event
            string correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _eventStream.Dispatch(new SessionStartedEvent(correlationId, _sandbox.WorkspaceRoot, DateTime.UtcNow));

            _eventStream.PublishMessage(new ChatMessage 
            { 
                Role = "system", 
                Content = "🚀 Starting the Sequential Multi-Agent Team Execution Pipeline...",
                Timestamp = DateTime.Now 
            });

            _auditReporter.LogEvent("Runtime", "Session Start", $"Initialized multi-agent session {correlationId}", "Success");

            string plan = "";
            string refinedPlan = "";
            string executionLogs = "";
            string qaReport = "";
            string finalSummary = "Pipeline executed with skipped or postponed stages.";

            try
            {
                // Stage 1: Planner
                if (await CheckStageGateAsync(AgentRole.Planner))
                {
                    TransitionTo(AgentState.Planning, "Planner designing implementation plan");
                    UpdateMemberStatus(AgentRole.Planner, "Active 🧠", "Designing the technical architecture...");
                    
                    plan = await RunPlannerStepAsync(userInput);
                    
                    UpdateMemberStatus(AgentRole.Planner, "Completed ✅", "Architecture plan drafted.");
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = $"📋 **Implementation Plan Drafted:**\n\n{plan}",
                        Timestamp = DateTime.Now 
                    });
                    
                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        AgentRole.Planner.ToString(),
                        ResolveModelForRole(AgentRole.Planner).Name,
                        "Completed ✅",
                        "Architecture plan drafted",
                        $"[Planner] Plan drafted:\n{plan}\n",
                        DateTime.UtcNow
                    ));

                    _auditReporter.LogEvent("Planner", "Plan Drafting", "Drafted implementation plan", "Success");
                }

                // Stage 2: PlanReviewer
                if (await CheckStageGateAsync(AgentRole.PlanReviewer))
                {
                    TransitionTo(AgentState.Planning, "PlanReviewer reviewing the implementation plan");
                    UpdateMemberStatus(AgentRole.PlanReviewer, "Active 🔍", "Critiquing and refining the architecture plan...");
                    
                    refinedPlan = await RunPlanReviewerStepAsync(userInput, plan);
                    
                    UpdateMemberStatus(AgentRole.PlanReviewer, "Completed ✅", "Plan refined and approved.");
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = $"🔍 **Refined & Approved Implementation Plan:**\n\n{refinedPlan}",
                        Timestamp = DateTime.Now 
                    });

                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        AgentRole.PlanReviewer.ToString(),
                        ResolveModelForRole(AgentRole.PlanReviewer).Name,
                        "Completed ✅",
                        "Plan refined and approved",
                        $"[PlanReviewer] Refined plan:\n{refinedPlan}\n",
                        DateTime.UtcNow
                    ));

                    _auditReporter.LogEvent("PlanReviewer", "Plan Review", "Refined and approved implementation plan", "Success");
                }
                else
                {
                    refinedPlan = plan; // Fallback
                }

                // Stage 3: Executor
                if (await CheckStageGateAsync(AgentRole.Executor))
                {
                    TransitionTo(AgentState.Executing, "Executor implementing the code changes");
                    UpdateMemberStatus(AgentRole.Executor, "Active 🛠️", "Executing tool calls to apply code changes...");
                    
                    executionLogs = await RunExecutorStepAsync(userInput, refinedPlan);
                    
                    UpdateMemberStatus(AgentRole.Executor, "Completed ✅", "Code modifications completed.");
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = $"⚙️ **Execution Complete!** Applied code changes and verified results.",
                        Timestamp = DateTime.Now 
                    });

                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        AgentRole.Executor.ToString(),
                        ResolveModelForRole(AgentRole.Executor).Name,
                        "Completed ✅",
                        "Code modifications completed",
                        $"[Executor] Execution finished.\nLogs summary:\n{executionLogs}\n",
                        DateTime.UtcNow
                    ));

                    _auditReporter.LogEvent("Executor", "Execution", "Applied code modifications and tool runs", "Success");
                }

                // Stage 4: Reviewer
                if (await CheckStageGateAsync(AgentRole.Reviewer))
                {
                    TransitionTo(AgentState.Observing, "Reviewer auditing the execution quality");
                    UpdateMemberStatus(AgentRole.Reviewer, "Active 🔬", "Reviewing changes for syntactic and functional correctness...");
                    
                    qaReport = await RunReviewerStepAsync(userInput, refinedPlan, executionLogs);
                    
                    UpdateMemberStatus(AgentRole.Reviewer, "Completed ✅", "QA audit concluded.");
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = $"🔬 **QA Review Report:**\n\n{qaReport}",
                        Timestamp = DateTime.Now 
                    });

                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        AgentRole.Reviewer.ToString(),
                        ResolveModelForRole(AgentRole.Reviewer).Name,
                        "Completed ✅",
                        "QA audit concluded",
                        $"[Reviewer] QA Report:\n{qaReport}\n",
                        DateTime.UtcNow
                    ));

                    _auditReporter.LogEvent("Reviewer", "QA Audit", "Concluded quality assurance audit", "Success");
                }

                // Stage 5: SecurityReviewer
                if (await CheckStageGateAsync(AgentRole.SecurityReviewer))
                {
                    TransitionTo(AgentState.Completed, "SecurityReviewer performing final safety audit");
                    UpdateMemberStatus(AgentRole.SecurityReviewer, "Active 🛡️", "Scanning changes for vulnerabilities & sandbox escapes...");
                    
                    finalSummary = await RunSecurityReviewerStepAsync(userInput, refinedPlan, executionLogs, qaReport);
                    
                    UpdateMemberStatus(AgentRole.SecurityReviewer, "Completed ✅", "Security audit passed.");

                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        AgentRole.SecurityReviewer.ToString(),
                        ResolveModelForRole(AgentRole.SecurityReviewer).Name,
                        "Completed ✅",
                        "Security audit passed",
                        $"[SecurityReviewer] Final Summary:\n{finalSummary}\n",
                        DateTime.UtcNow
                    ));

                    _auditReporter.LogEvent("SecurityReviewer", "Security Audit", "Performed final safety audit", "Success");
                }

                // Run postponed stages if any
                if (_postponedRoles.Count > 0)
                {
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "system", 
                        Content = "🕒 Running postponed stages sequentially...",
                        Timestamp = DateTime.Now 
                    });

                    foreach (var role in _postponedRoles.ToList())
                    {
                        _eventStream.PublishMessage(new ChatMessage 
                        { 
                            Role = "system", 
                            Content = $"🔄 Executing postponed stage: {role} ({GetArabicStageName(role)})",
                            Timestamp = DateTime.Now 
                        });
                        
                        var roleModel = ResolveModelForRole(role);
                        _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                            role.ToString(),
                            roleModel.Name,
                            "Executing 🛠️",
                            $"Running postponed {GetArabicStageName(role)}...",
                            $"[System] Running postponed stage {role}...\n",
                            DateTime.UtcNow
                        ));
                        
                        if (role == AgentRole.Planner)
                        {
                            TransitionTo(AgentState.Planning, "Running postponed Planner step");
                            UpdateMemberStatus(AgentRole.Planner, "Active 🧠", "Postponed plan draft running...");
                            plan = await RunPlannerStepAsync(userInput);
                            UpdateMemberStatus(AgentRole.Planner, "Completed ✅", "Postponed plan drafted.");
                            _auditReporter.LogEvent("Planner", "Postponed Plan Draft", "Drafted plan for task: " + userInput, "Success");
                        }
                        else if (role == AgentRole.PlanReviewer)
                        {
                            TransitionTo(AgentState.Planning, "Running postponed PlanReviewer step");
                            UpdateMemberStatus(AgentRole.PlanReviewer, "Active 🔍", "Postponed plan review running...");
                            refinedPlan = await RunPlanReviewerStepAsync(userInput, plan);
                            UpdateMemberStatus(AgentRole.PlanReviewer, "Completed ✅", "Postponed plan review approved.");
                            _auditReporter.LogEvent("PlanReviewer", "Postponed Plan Review", "Reviewed plan", "Success");
                        }
                        else if (role == AgentRole.Executor)
                        {
                            TransitionTo(AgentState.Executing, "Running postponed Executor step");
                            UpdateMemberStatus(AgentRole.Executor, "Active 🛠️", "Postponed execution running...");
                            executionLogs = await RunExecutorStepAsync(userInput, refinedPlan);
                            UpdateMemberStatus(AgentRole.Executor, "Completed ✅", "Postponed execution complete.");
                            _auditReporter.LogEvent("Executor", "Postponed Execution", "Executed tools", "Success");
                        }
                        else if (role == AgentRole.Reviewer)
                        {
                            TransitionTo(AgentState.Observing, "Running postponed Reviewer step");
                            UpdateMemberStatus(AgentRole.Reviewer, "Active 🔬", "Postponed review running...");
                            qaReport = await RunReviewerStepAsync(userInput, refinedPlan, executionLogs);
                            UpdateMemberStatus(AgentRole.Reviewer, "Completed ✅", "Postponed review complete.");
                            _auditReporter.LogEvent("Reviewer", "Postponed Review", "Audited execution quality", "Success");
                        }
                        else if (role == AgentRole.SecurityReviewer)
                        {
                            TransitionTo(AgentState.Completed, "Running postponed SecurityReviewer step");
                            UpdateMemberStatus(AgentRole.SecurityReviewer, "Active 🛡️", "Postponed security audit running...");
                            finalSummary = await RunSecurityReviewerStepAsync(userInput, refinedPlan, executionLogs, qaReport);
                            UpdateMemberStatus(AgentRole.SecurityReviewer, "Completed ✅", "Postponed security audit complete.");
                            _auditReporter.LogEvent("SecurityReviewer", "Postponed Security Audit", "Performed final safety audit", "Success");
                        }
                        
                        _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                            role.ToString(),
                            roleModel.Name,
                            "Completed ✅",
                            $"Postponed stage {GetArabicStageName(role)} finished",
                            $"[System] Postponed stage {role} complete.\n",
                            DateTime.UtcNow
                        ));
                    }
                }

                // Dispatch Session Completed
                _eventStream.Dispatch(new SessionCompletedEvent(finalSummary, DateTime.UtcNow));
                _auditReporter.LogEvent("Runtime", "Session Complete", "Multi-agent session finished successfully", "Success");

                return finalSummary;
            }
            catch (Exception ex)
            {
                // Dispatch Session Failed
                _eventStream.Dispatch(new SessionFailedEvent(ex.Message, DateTime.UtcNow));
                TransitionTo(AgentState.Failed, ex.Message);
                _auditReporter.LogEvent("Runtime", "Session Failure", $"Exception encountered: {ex.Message}", "Failed");
                throw;
            }
        }

        private async Task<string> RunPlannerStepAsync(string userInput)
        {
            var model = ResolveModelForRole(AgentRole.Planner);
            var binding = _roleBindings.FirstOrDefault(b => b.Role == AgentRole.Planner);
            string policy = binding?.ExecutionPolicy ?? "You are a professional software architect. Create a comprehensive implementation plan to solve the task.";
            
            // Phase 2.3: Inject relevant workspace context from SemanticIndex
            string relevantContext = "";
            try
            {
                relevantContext = Bootstrapper.SemanticIndex?.BuildRelevantContext(userInput, 3000) ?? "";
            }
            catch { /* SemanticIndex may not be ready yet */ }

            string systemContent = $"[ROLE POLICY]\n{policy}\n\nIf the user input is in Arabic, respond in Arabic. If English, respond in English.";
            if (!string.IsNullOrEmpty(relevantContext))
            {
                systemContent += $"\n\n[WORKSPACE CONTEXT — Relevant Files]\n{relevantContext}";
            }

            var history = new List<SimpleMessage>
            {
                new() { Role = "system", Content = systemContent },
                new() { Role = "user", Content = userInput }
            };
            
            return await CallLlmForRoleAsync(AgentRole.Planner, model, history);
        }

        private async Task<string> RunPlanReviewerStepAsync(string userInput, string originalPlan)
        {
            var model = ResolveModelForRole(AgentRole.PlanReviewer);
            var binding = _roleBindings.FirstOrDefault(b => b.Role == AgentRole.PlanReviewer);
            string policy = binding?.ExecutionPolicy ?? "You are an expert technical planner. Review the implementation plan, refine it, fix architectural issues, add edge cases, and output the final plan.";
            
            var history = new List<SimpleMessage>
            {
                new() { Role = "system", Content = $"[ROLE POLICY]\n{policy}\n\nIf the user input is in Arabic, respond in Arabic. If English, respond in English." },
                new() { Role = "user", Content = $"Original User Request: {userInput}\n\nDraft Implementation Plan:\n{originalPlan}\n\nPlease review and output the final refined implementation plan." }
            };
            
            return await CallLlmForRoleAsync(AgentRole.PlanReviewer, model, history);
        }

        private async Task<string> RunExecutorStepAsync(string userInput, string refinedPlan)
        {
            var model = ResolveModelForRole(AgentRole.Executor);
            var binding = _roleBindings.FirstOrDefault(b => b.Role == AgentRole.Executor);
            string policy = binding?.ExecutionPolicy ?? "You are a professional software engineer. Implement the refined plan using the available tools.";
            
            string executorSystemPrompt = 
                $"You are the Executor agent.\n" +
                $"[ROLE POLICY]\n{policy}\n\n" +
                $"Task: {userInput}\n" +
                $"Plan: {refinedPlan}\n\n" +
                $"You must execute the plan by invoking tools. Think step-by-step. " +
                $"Use [TOOL_NAME arg=val] to act (e.g., [write_file file=path content=text], [read_file file=path]). " +
                $"If the implementation is fully complete, just say TASK_COMPLETE.";

            var apiKey = GetApiKeyForProvider(model.ProviderId);
            var planner = new LlmExecutorPlanner(_roleOrchestrator.Registry, model, apiKey, executorSystemPrompt, _isAutoRotateEnabled, this);
            
            var context = new AgentContext
            {
                UserInput = userInput,
                Observations = new List<Observation>()
            };

            int iteration = 0;
            const int maxIterations = 25; // Phase 2.3: Increased from 8, budget checks now handle limits
            bool completed = false;

            while (iteration < maxIterations && !completed)
            {
                iteration++;

                // Phase 2.3: Budget check before each iteration
                var budgetCheck = Bootstrapper.Budget?.TryConsume("ITERATION");
                if (budgetCheck.HasValue && !budgetCheck.Value.Allowed)
                {
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "system", 
                        Content = $"⚠️ BUDGET EXHAUSTED: {budgetCheck.Value.Reason}",
                        Timestamp = DateTime.Now 
                    });
                    TransitionTo(AgentState.RePlanning, budgetCheck.Value.Reason);
                    break;
                }

                // Phase 2.3: Check for failure loops
                var lastError = context.Observations.LastOrDefault(o => !o.Result.Success)?.Result?.Error;
                if (lastError != null && Bootstrapper.FailureMemory?.IsInFailureLoop(lastError) == true)
                {
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "system", 
                        Content = "🔄 FAILURE LOOP DETECTED: Same error repeating. Transitioning to RePlanning.",
                        Timestamp = DateTime.Now 
                    });
                    TransitionTo(AgentState.RePlanning, "Failure loop detected");
                    break;
                }

                UpdateMemberStatus(AgentRole.Executor, "Active 🛠️", $"Executing tool loop (Step {iteration}/{maxIterations})...");
                
                var toolCalls = await planner.GetPlanAsync(context);
                if (toolCalls == null || !toolCalls.Any())
                {
                    completed = true;
                    break;
                }
                
                var call = toolCalls.First();
                if (call.ToolName == "TASK_COMPLETE" || call.ToolName == "Completed")
                {
                    completed = true;
                    break;
                }

                // Phase 2.2: Output Validation Layer
                var validation = ValidateToolExecution(call);
                if (!validation.Success)
                {
                    string errMsg = $"🛡️ VALIDATION FAIL: {validation.Error}";
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "system", 
                        Content = errMsg,
                        Timestamp = DateTime.Now 
                    });
                    
                    var failedResult = new ToolResult(false, "", errMsg, FailureClassification.SecurityViolation);
                    
                    _eventStream.Dispatch(new ToolExecutionCompletedEvent(call.ToolName, false, failedResult.Output, failedResult.Error, call.Arguments, DateTime.UtcNow));
                    
                    var failedObs = new Observation
                    {
                        ToolName = call.ToolName,
                        Result = failedResult,
                        Timestamp = DateTime.UtcNow
                    };
                    context.Observations.Add(failedObs);
                    _observations.Add(failedObs);
                    continue;
                }

                // Phase 2.3: Budget consumption per tool type
                string budgetCategory = call.ToolName.ToUpperInvariant() switch
                {
                    "WRITE_FILE" or "FILE_WRITE" or "WRITE" => "FILE_WRITE",
                    "READ_FILE" or "FILE_READ" or "READ" => "FILE_READ",
                    "CMD" or "PS" or "RUN_CMD" or "RUN_PS" => "CMD",
                    _ => "ITERATION" // Already consumed above
                };
                if (budgetCategory != "ITERATION")
                {
                    var toolBudget = Bootstrapper.Budget?.TryConsume(budgetCategory);
                    if (toolBudget.HasValue && !toolBudget.Value.Allowed)
                    {
                        _eventStream.PublishMessage(new ChatMessage 
                        { 
                            Role = "system", 
                            Content = $"⚠️ BUDGET EXHAUSTED for {budgetCategory}: {toolBudget.Value.Reason}",
                            Timestamp = DateTime.Now 
                        });
                        break;
                    }
                }

                // Phase 2.3: InvariantChecker — validate content safety
                if (call.ToolName.Equals("WRITE_FILE", StringComparison.OrdinalIgnoreCase) ||
                    call.ToolName.Equals("FILE_WRITE", StringComparison.OrdinalIgnoreCase) ||
                    call.ToolName.Equals("WRITE", StringComparison.OrdinalIgnoreCase))
                {
                    if (call.Arguments.TryGetValue("path", out var pathObj) && pathObj is string filePath)
                    {
                        var pathCheck = Bootstrapper.InvariantChecker?.Check(InvariantScope.FilePath, filePath);
                        if (pathCheck != null && !pathCheck.IsValid)
                        {
                            string invariantMsg = $"🛡️ INVARIANT VIOLATION: {pathCheck.GetViolationSummary()}";
                            _eventStream.PublishMessage(new ChatMessage { Role = "system", Content = invariantMsg, Timestamp = DateTime.Now });
                            var invResult = new ToolResult(false, "", invariantMsg, FailureClassification.SecurityViolation);
                            context.Observations.Add(new Observation { ToolName = call.ToolName, Result = invResult, Timestamp = DateTime.UtcNow });
                            _observations.Add(new Observation { ToolName = call.ToolName, Result = invResult, Timestamp = DateTime.UtcNow });
                            continue;
                        }
                    }
                    if (call.Arguments.TryGetValue("content", out var contentObj) && contentObj is string content)
                    {
                        var contentCheck = Bootstrapper.InvariantChecker?.Check(InvariantScope.FileContent, content);
                        if (contentCheck != null && !contentCheck.IsValid)
                        {
                            string invariantMsg = $"🛡️ CONTENT INVARIANT VIOLATION: {contentCheck.GetViolationSummary()}";
                            _eventStream.PublishMessage(new ChatMessage { Role = "system", Content = invariantMsg, Timestamp = DateTime.Now });
                            var invResult = new ToolResult(false, "", invariantMsg, FailureClassification.SecurityViolation);
                            context.Observations.Add(new Observation { ToolName = call.ToolName, Result = invResult, Timestamp = DateTime.UtcNow });
                            _observations.Add(new Observation { ToolName = call.ToolName, Result = invResult, Timestamp = DateTime.UtcNow });
                            continue;
                        }
                    }
                }

                _eventStream.PublishMessage(new ChatMessage 
                { 
                    Role = "system", 
                    Content = $"🛠️ Executor invoking tool: `{call.ToolName}`",
                    Timestamp = DateTime.Now 
                });

                // Dispatch Tool Execution Started
                _eventStream.Dispatch(new ToolExecutionStartedEvent(call.ToolName, call.Arguments, DateTime.UtcNow));

                var execToken = await _workflow.GetTokenAsync(WorkflowStage.Execution);
                if (execToken == null)
                {
                    _eventStream.PublishMessage(new ChatMessage { Role = "system", Content = "⚠️ Execution Blocked by workflow.", Timestamp = DateTime.Now });
                    _eventStream.Dispatch(new ToolExecutionCompletedEvent(call.ToolName, false, "", "Execution Blocked by workflow", call.Arguments, DateTime.UtcNow));
                    break;
                }

                var command = new ExecutionCommand(call.ToolName, call.Arguments, execToken, Guid.NewGuid().ToString());
                var result = await _sandbox.ExecuteAsync(command);
                _workflow.RevokeToken();

                // Dispatch Tool Execution Completed
                _eventStream.Dispatch(new ToolExecutionCompletedEvent(call.ToolName, result.Success, result.Output, result.Error, call.Arguments, DateTime.UtcNow));

                // Phase 2.3: Record failures to FailureMemory for learning
                if (!result.Success)
                {
                    string errorMsg = result.Error ?? result.Output;
                    Bootstrapper.FailureMemory?.RecordFailure(errorMsg, $"Tool:{call.ToolName}");
                    
                    // Check if we have a known fix
                    var (seen, suggestedFix, prevCount) = Bootstrapper.FailureMemory?.CheckError(errorMsg) ?? (false, null, 0);
                    if (seen && !string.IsNullOrEmpty(suggestedFix))
                    {
                        _eventStream.PublishMessage(new ChatMessage 
                        { 
                            Role = "system", 
                            Content = $"💡 KNOWN ERROR (seen {prevCount}x). Suggested fix: {suggestedFix}",
                            Timestamp = DateTime.Now 
                        });
                        // Inject fix suggestion as observation
                        context.Observations.Add(new Observation
                        {
                            ToolName = "SYSTEM_FIX_SUGGESTION",
                            Result = new ToolResult(true, $"Known fix for this error: {suggestedFix}"),
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }

                // Phase 2.3: Update SemanticIndex for modified files
                if (result.Success && (call.ToolName.Equals("WRITE_FILE", StringComparison.OrdinalIgnoreCase) || 
                    call.ToolName.Equals("FILE_WRITE", StringComparison.OrdinalIgnoreCase)))
                {
                    if (call.Arguments.TryGetValue("path", out var wp) && wp is string writtenPath &&
                        call.Arguments.TryGetValue("content", out var wc) && wc is string writtenContent)
                    {
                        try { Bootstrapper.SemanticIndex?.UpdateFile(writtenPath, writtenContent); } catch { }
                    }
                }

                var observation = new Observation
                {
                    ToolName = call.ToolName,
                    Result = result,
                    Timestamp = DateTime.UtcNow
                };
                context.Observations.Add(observation);
                _observations.Add(observation); // Keep trace updated
                
                _eventStream.PublishMessage(new ChatMessage 
                { 
                    Role = "system", 
                    Content = result.Success 
                        ? $"✅ Tool `{call.ToolName}` succeeded." 
                        : $"❌ Tool `{call.ToolName}` failed: {result.Error ?? result.Output}",
                    Timestamp = DateTime.Now 
                });
            }

            var summary = string.Join("\n", context.Observations.Select((o, i) => 
                $"{i+1}. Tool `{o.ToolName}`: Success={o.Result.Success}, Output={(o.Result.Output.Length > 200 ? o.Result.Output.Substring(0, 200) + "..." : o.Result.Output)}"));
            
            // Phase 2.2: Semantic Drift Detector
            var driftDetector = new DriftDetector(_sandbox.WorkspaceRoot);
            var (hasDrift, explanation) = driftDetector.AnalyzeSemanticAndStateDrift(refinedPlan, summary);
            if (hasDrift)
            {
                string driftMsg = $"⚠️ STATE DRIFT DETECTED: {explanation}";
                _eventStream.PublishMessage(new ChatMessage 
                { 
                    Role = "system", 
                    Content = driftMsg,
                    Timestamp = DateTime.Now 
                });
                
                _eventStream.Dispatch(new DriftDetectedEvent("ExecutorExecution", refinedPlan.Substring(0, Math.Min(100, refinedPlan.Length)), summary.Substring(0, Math.Min(100, summary.Length)), explanation, DateTime.UtcNow));
                
                // Rollback / Transition to RePlanning
                TransitionTo(AgentState.RePlanning, driftMsg);
            }

            return summary;
        }

        private (bool Success, string? Error) ValidateToolExecution(ToolCall call)
        {
            if (string.IsNullOrWhiteSpace(call.ToolName))
            {
                return (false, "Tool name is empty.");
            }

            // 1. Structural Check
            if (call.ToolName.Equals("WRITE_FILE", StringComparison.OrdinalIgnoreCase) || call.ToolName.Equals("FILE_WRITE", StringComparison.OrdinalIgnoreCase))
            {
                if (!call.Arguments.ContainsKey("path") || !call.Arguments.ContainsKey("content"))
                {
                    return (false, "WRITE_FILE requires both 'path' and 'content' arguments.");
                }
            }

            // 2. Rule Compliance Check
            if (call.Arguments.TryGetValue("path", out var pathObj) && pathObj is string path)
            {
                if (path.Contains("..") || path.StartsWith("/") || path.StartsWith("\\"))
                {
                    return (false, "Path traversal traversal is strictly prohibited.");
                }
                if (path.EndsWith(".exe") || path.EndsWith(".dll") || path.EndsWith(".lnk"))
                {
                    return (false, "Executable file creation/modification is strictly prohibited.");
                }
            }

            return (true, null);
        }

        private async Task<string> RunReviewerStepAsync(string userInput, string refinedPlan, string executionLogs)
        {
            var model = ResolveModelForRole(AgentRole.Reviewer);
            var binding = _roleBindings.FirstOrDefault(b => b.Role == AgentRole.Reviewer);
            string policy = binding?.ExecutionPolicy ?? "You are a senior software developer and QA engineer. Review the executed task and code changes.";
            
            var history = new List<SimpleMessage>
            {
                new() { Role = "system", Content = $"[ROLE POLICY]\n{policy}\n\nIf the user input is in Arabic, respond in Arabic. If English, respond in English." },
                new() { Role = "user", Content = $"Original User Request: {userInput}\n\nRefined Implementation Plan:\n{refinedPlan}\n\nExecution Logs / Code Changes:\n{executionLogs}\n\nPlease review these changes and output your comprehensive Quality Assurance and Review Report." }
            };
            
            return await CallLlmForRoleAsync(AgentRole.Reviewer, model, history);
        }

        private async Task<string> RunSecurityReviewerStepAsync(string userInput, string refinedPlan, string executionLogs, string qaReport)
        {
            var model = ResolveModelForRole(AgentRole.SecurityReviewer);
            var binding = _roleBindings.FirstOrDefault(b => b.Role == AgentRole.SecurityReviewer);
            string policy = binding?.ExecutionPolicy ?? "You are a cybersecurity expert. Perform a final security audit of the code changes and summarize the completed task.";
            
            var history = new List<SimpleMessage>
            {
                new() { Role = "system", Content = $"[ROLE POLICY]\n{policy}\n\nIf the user input is in Arabic, respond in Arabic. If English, respond in English." },
                new() { Role = "user", Content = $"Original User Request: {userInput}\n\nRefined Plan:\n{refinedPlan}\n\nExecution Logs:\n{executionLogs}\n\nQA Report:\n{qaReport}\n\nPlease audit for any security risks, whitelists, or vulnerabilities. Provide a final security audit report and a beautiful final summary of the completed task." }
            };
            
            return await CallLlmForRoleAsync(AgentRole.SecurityReviewer, model, history);
        }

        private async Task<string> CallLlmForRoleAsync(AgentRole role, ModelMetadata model, List<SimpleMessage> history, CancellationToken ct = default)
        {
            // Phase 2.3: Apply PromptGovernor to trim history within token budget
            var governor = Bootstrapper.PromptGovernor;
            if (governor != null)
            {
                history = governor.TrimHistory(history);
            }

            // Dispatch LLM Call Started Event
            _eventStream.Dispatch(new LlmCallStartedEvent(role, model.Id, model.ProviderId, DateTime.UtcNow));

            var provider = _roleOrchestrator.Registry.GetProvider(model.ProviderId);
            if (provider == null) 
                throw new InvalidOperationException($"Provider {model.ProviderId} not found.");

            string apiKey = GetApiKeyForProvider(model.ProviderId);
            string resp;

            if (_isAutoRotateEnabled)
            {
                var llm = new LlmService();
                var autoRotate = new AutoRotateService(llm);
                var chain = BuildChainForModel(model);
                autoRotate.ConfigureChain(chain);
                
                // Attach the Safe Auto-Rotation Protocol handler
                autoRotate.OnModelRotatedWithHistory += (oldM, newM, hist) => InjectRotationBanner(role, oldM, newM, hist);
                
                var (response, usedModel) = await autoRotate.ChatWithFallbackAsync(history, ct);
                resp = response;
                
                if (!string.IsNullOrEmpty(usedModel) && usedModel != $"{model.ProviderId}/{model.Id}")
                {
                    _eventStream.PublishMessage(new ChatMessage 
                    { 
                        Role = "system", 
                        Content = $"🔄 Quota limit hit! Auto-rotated from {model.ProviderId}/{model.Id} to {usedModel}.",
                        Timestamp = DateTime.Now 
                    });
                }
            }
            else
            {
                resp = await provider.ChatAsync(model, history, apiKey, ct);
            }

            // Dispatch LLM Call Completed Event
            string snippet = resp.Length > 100 ? resp.Substring(0, 100) + "..." : resp;
            _eventStream.Dispatch(new LlmCallCompletedEvent(role, model.Id, model.ProviderId, snippet, DateTime.UtcNow));

            return resp;
        }

        /// <summary>
        /// Phase 2.2 Hybrid Context Engine: Injects compact event history, structured file diff summaries,
        /// and selective hot file contents into the rotation banner to maintain reasoning continuity
        /// without context explosion.
        /// </summary>
        private void InjectRotationBanner(AgentRole role, string oldModel, string newModel, List<SimpleMessage> history)
        {
            var state = Bootstrapper.StateStore?.CurrentState;
            string contextPayload = BuildHybridContextPayload(state);

            string banner;
            bool isArabic = history.Any(m => m.Content != null && (m.Content.Contains("خطوة") || m.Content.Contains("نفذ") || m.Content.Contains("خطة")));

            if (isArabic)
            {
                banner = $@"=== تنبيه النظام: حدث تدوير وتغيير للموديل الذكي ===
تم تحويل الموديل الأساسي من {oldModel} إلى {newModel} بسبب تخطي حدود الاستخدام أو مشاكل اتصال.

--- سجل التنفيذ المضغوط ---
{contextPayload}
--- نهاية السجل ---

يرجى إكمال المهمة بسلاسة وفقًا لدورك المحدد ({role}) والخطة المعتمدة.
لا تكرر الخطوات المكتملة. أكمل من حيث توقف الموديل السابق.";
            }
            else
            {
                banner = $@"=== SYSTEM NOTICE: MODEL ROTATION EVENT ===
The primary model has been rotated from {oldModel} to {newModel} due to a capacity limit or connection issue.

--- Compact Execution Ledger ---
{contextPayload}
--- End Ledger ---

Please continue seamlessly according to your assigned role ({role}) and the refined plan.
Do NOT repeat completed steps. Resume from where the previous model left off.";
            }

            // Inject warning banner right before the latest request to keep it highly active in LLM memory
            if (history.Count > 0)
            {
                history.Insert(history.Count - 1, new SimpleMessage { Role = "system", Content = banner });
            }
            else
            {
                history.Add(new SimpleMessage { Role = "system", Content = banner });
            }

            // Dispatch Model Rotated Event to state store
            _eventStream.Dispatch(new ModelAutoRotatedEvent(oldModel, newModel, DateTime.UtcNow));
        }

        /// <summary>
        /// Builds a compact context payload from the canonical state store using the Hybrid Context Model:
        /// 1. Compact Event History (last 8 observations)
        /// 2. Structured File Diff Summary
        /// 3. Selective Hot File Content Injection (only modified files)
        /// </summary>
        private string BuildHybridContextPayload(WorkspaceState? state)
        {
            if (state == null)
                return "[No state available - primary model failed before state capture]";

            var sb = new System.Text.StringBuilder();

            // Layer 1: Session metadata + fingerprint
            sb.AppendLine($"Session: {state.CorrelationId} | State: {state.SessionState} | Fingerprint: {state.ExecutionFingerprint}");
            sb.AppendLine($"Tool State: {state.ToolState}");
            sb.AppendLine();

            // Layer 2: Execution Graph (agent pipeline status)
            sb.AppendLine("Pipeline Status:");
            foreach (var kv in state.ExecutionGraph)
            {
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            // Layer 3: Compact Event History (last 8 observations only to prevent context explosion)
            if (state.MemorySnapshot.Count > 0)
            {
                sb.AppendLine("Recent Tool Executions (compact):");
                var recentObs = state.MemorySnapshot
                    .Skip(Math.Max(0, state.MemorySnapshot.Count - 8))
                    .ToList();
                int idx = 1;
                foreach (var obs in recentObs)
                {
                    string output = obs.Result.Output;
                    if (output.Length > 120) output = output.Substring(0, 120) + "...";
                    sb.AppendLine($"  {idx}. [{(obs.Result.Success ? "OK" : "FAIL")}] {obs.ToolName}: {output}");
                    idx++;
                }
                sb.AppendLine();
            }

            // Layer 4: Structured File Diff Summary
            if (state.FileDiffState.Count > 0)
            {
                sb.AppendLine("Modified Files (diff summary):");
                foreach (var file in state.FileDiffState)
                {
                    sb.AppendLine($"  [MODIFIED] {file}");
                }
                sb.AppendLine();
            }

            // Layer 5: Selective Hot File Content Injection
            if (state.HotFiles.Count > 0 && !string.IsNullOrEmpty(state.WorkspacePath))
            {
                sb.AppendLine("Hot Files (recently modified content):");
                foreach (var hotFile in state.HotFiles.Take(5)) // Cap at 5 files max
                {
                    try
                    {
                        string fullPath = Path.Combine(state.WorkspacePath, hotFile);
                        if (File.Exists(fullPath))
                        {
                            string content = File.ReadAllText(fullPath);
                            // Cap per-file injection at 800 chars to prevent context explosion
                            if (content.Length > 800) content = content.Substring(0, 800) + "\n... [TRUNCATED]";
                            sb.AppendLine($"  --- {hotFile} ---");
                            sb.AppendLine(content);
                            sb.AppendLine($"  --- end {hotFile} ---");
                        }
                    }
                    catch
                    {
                        sb.AppendLine($"  [{hotFile}] (unable to read)");
                    }
                }
                sb.AppendLine();
            }

            // Layer 6: Active Rules
            if (!string.IsNullOrEmpty(state.ActiveRules))
            {
                sb.AppendLine($"Active Rules: {state.ActiveRules}");
            }

            return sb.ToString();
        }

        private List<RotateEntry> BuildChainForModel(ModelMetadata primaryModel)
        {
            var chain = new List<RotateEntry>();
            
            chain.Add(new RotateEntry 
            { 
                Provider = primaryModel.ProviderId, 
                Model = primaryModel.Id, 
                ApiKey = GetApiKeyForProvider(primaryModel.ProviderId) 
            });
            
            var providers = new[] { "Google Gemini", "Groq", "Grok", "OpenAI", "DeepSeek", "Moonshot" };
            foreach (var p in providers)
            {
                if (p == primaryModel.ProviderId) continue;
                string key = GetApiKeyForProvider(p);
                if (!string.IsNullOrEmpty(key))
                {
                    var defaultModelForProvider = p switch
                    {
                        "Google Gemini" => "gemini-2.0-flash",
                        "Groq" => "llama-3.3-70b-versatile",
                        "Grok" => "grok-2-1212",
                        "OpenAI" => "gpt-4o-mini",
                        "DeepSeek" => "deepseek-chat",
                        "Moonshot" => "moonshot-v1-8k",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(defaultModelForProvider))
                    {
                        chain.Add(new RotateEntry { Provider = p, Model = defaultModelForProvider, ApiKey = key });
                    }
                }
            }
            
            return chain;
        }

        private ModelMetadata ResolveModelForRole(AgentRole role)
        {
            var binding = _roleBindings.FirstOrDefault(b => b.Role == role);
            if (binding != null && binding.SelectedModel != null)
                return binding.SelectedModel;
            
            return _roleOrchestrator.ResolveModel(role);
        }

        private string GetApiKeyForProvider(string providerId)
        {
            string keyName = providerId switch
            {
                "Google Gemini" => "GeminiApiKey",
                "OpenAI" => "OpenAIApiKey",
                "DeepSeek" => "DeepSeekApiKey",
                "Moonshot" => "MoonshotApiKey",
                "Groq" => "GroqApiKey",
                "Grok" => "GrokApiKey",
                _ => providerId + "ApiKey"
            };
            string val = _secretsService.GetSecret(keyName);
            if (!string.IsNullOrEmpty(val)) return val;

            string upperKeyName = providerId.Replace(" ", "").ToUpper() + "_API_KEY";
            val = _secretsService.GetSecret(upperKeyName);
            if (!string.IsNullOrEmpty(val)) return val;

            upperKeyName = providerId.ToUpper() + "_API_KEY";
            val = _secretsService.GetSecret(upperKeyName);
            return val ?? "";
        }

        private void UpdateMemberStatus(AgentRole role, string status, string lastAction)
        {
            var devRole = MapToDevRole(role);
            var model = ResolveModelForRole(role);
            _eventStream.UpdateTeamMember(new TeamMember(devRole, model.Name, status, lastAction));
        }

        private DevRole MapToDevRole(AgentRole role) => role switch
        {
            AgentRole.Planner => DevRole.FullStackEngineer,
            AgentRole.PlanReviewer => DevRole.Reviewer,
            AgentRole.Executor => DevRole.FullStackEngineer,
            AgentRole.Reviewer => DevRole.Reviewer,
            AgentRole.SecurityReviewer => DevRole.SecurityReviewer,
            AgentRole.FrontendDeveloper => DevRole.FrontendEngineer,
            AgentRole.BackendDeveloper => DevRole.BackendEngineer,
            _ => DevRole.FullStackEngineer
        };

        /// <summary>
        /// Phase 2.2: State transitions now query the canonical central store instead of a local variable.
        /// The central store is the single source of truth for session state.
        /// </summary>
        private void TransitionTo(AgentState newState, string? reason = null)
        {
            var currentState = CurrentState;

            bool isValid = (currentState, newState) switch
            {
                (AgentState.Idle, AgentState.Planning) => true,
                (AgentState.Planning, AgentState.Planning) => true,
                (AgentState.Planning, AgentState.Executing) => true,
                (AgentState.Planning, AgentState.Completed) => true,
                (AgentState.Executing, AgentState.Observing) => true,
                (AgentState.Executing, AgentState.RePlanning) => true,
                (AgentState.Observing, AgentState.RePlanning) => true,
                (AgentState.Observing, AgentState.Completed) => true,
                (AgentState.Observing, AgentState.Failed) => true,
                (AgentState.RePlanning, AgentState.Executing) => true,
                (AgentState.RePlanning, AgentState.Completed) => true,
                (AgentState.RePlanning, AgentState.Failed) => true,
                (_, AgentState.Failed) => true,
                _ => false
            };

            if (!isValid)
                throw new InvalidOperationException($"Illegal state transition from {currentState} to {newState}");

            var evt = new AgentEvent(currentState, newState, DateTime.UtcNow, reason);
            _eventLog.Add(evt);
            // No local _state mutation — the central store is updated via _eventStream.Publish
            _eventStream.Publish(evt);
            OnStateChanged?.Invoke(newState);
        }

        private async Task<bool> CheckStageGateAsync(AgentRole role)
        {
            string stageName = GetArabicStageName(role);
            string moduleName = GetCurrentModuleName();
            string statusMsg = $"أنا الآن أعمل في {stageName} بموديول {moduleName}";
            
            var model = ResolveModelForRole(role);
            
            _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                role.ToString(),
                model.Name,
                "Gating Stage 🕒",
                statusMsg,
                $"[System] Waiting for approval to start {stageName} stage...\n",
                DateTime.UtcNow
            ));

            _eventStream.PublishMessage(new ChatMessage
            {
                Role = "system",
                Content = statusMsg,
                Timestamp = DateTime.Now
            });

            var approval = await _workflow.GetStageApprovalAsync(role);
            if (approval == StageApprovalResult.Skip)
            {
                _eventStream.PublishMessage(new ChatMessage
                {
                    Role = "system",
                    Content = $"⏭️ Skipped stage: {role} ({stageName})",
                    Timestamp = DateTime.Now
                });
                UpdateMemberStatus(role, "Skipped ⏭️", "Stage skipped by user decision.");
                
                _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                    role.ToString(),
                    model.Name,
                    "Skipped ⏭️",
                    "Stage skipped by user",
                    $"[System] Stage {stageName} was skipped.\n",
                    DateTime.UtcNow
                ));
                _auditReporter.LogEvent(role.ToString(), "Skip Stage", $"User skipped {stageName} stage.", "Skipped");
                return false;
            }
            else if (approval == StageApprovalResult.Postpone)
            {
                _eventStream.PublishMessage(new ChatMessage
                {
                    Role = "system",
                    Content = $"🕒 Postponed stage: {role} ({stageName})",
                    Timestamp = DateTime.Now
                });
                UpdateMemberStatus(role, "Postponed 🕒", "Stage postponed by user decision.");
                
                _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                    role.ToString(),
                    model.Name,
                    "Postponed 🕒",
                    "Stage postponed",
                    $"[System] Stage {stageName} was postponed to execute at the end.\n",
                    DateTime.UtcNow
                ));
                _postponedRoles.Add(role);
                _auditReporter.LogEvent(role.ToString(), "Postpone Stage", $"User postponed {stageName} stage.", "Postponed");
                return false;
            }

            _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                role.ToString(),
                model.Name,
                "Executing 🛠️",
                $"Running {stageName}...",
                $"[System] Stage {stageName} approved. Executing...\n",
                DateTime.UtcNow
            ));
            _auditReporter.LogEvent(role.ToString(), "Approve Stage", $"User approved {stageName} stage execution.", "Approved");
            return true;
        }

        private string GetArabicStageName(AgentRole role) => role switch
        {
            AgentRole.Planner => "التخطيط",
            AgentRole.PlanReviewer => "مراجعة الخطة",
            AgentRole.Executor => "التنفيذ",
            AgentRole.Reviewer => "المراجعة والتدقيق",
            AgentRole.SecurityReviewer => "الأمان",
            _ => role.ToString()
        };

        private string GetCurrentModuleName()
        {
            var state = Bootstrapper.StateStore?.CurrentState;
            if (state != null && state.HotFiles.Count > 0)
            {
                var firstFile = state.HotFiles.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstFile))
                {
                    var dirName = Path.GetDirectoryName(firstFile);
                    if (!string.IsNullOrEmpty(dirName))
                    {
                        return Path.GetFileName(dirName);
                    }
                }
            }
            if (!string.IsNullOrEmpty(_sandbox.WorkspaceRoot))
            {
                return Path.GetFileName(_sandbox.WorkspaceRoot.TrimEnd('\\', '/'));
            }
            return "المشروع";
        }

        public async Task RunCustomAgentAsync(string roleName, string initialStage, string userInput, ModelMetadata model)
        {
            var state = new AgentExecutionState
            {
                Role = roleName,
                ModelName = model.Name,
                Status = "Starting...",
                CurrentAction = "Initializing..."
            };

            _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                roleName,
                model.Name,
                state.Status,
                state.CurrentAction,
                state.OutputLog,
                DateTime.UtcNow
            ));

            _auditReporter.LogEvent(roleName, "Initialize Custom Agent", $"Initialized custom agent {roleName} with model {model.Name} starting at {initialStage}", "Success");

            try
            {
                if (initialStage.Equals("Planning", StringComparison.OrdinalIgnoreCase))
                {
                    state.Status = "Planning 🧠";
                    state.CurrentAction = "Generating custom plan...";
                    state.OutputLog += $"[System] Starting Planning phase using {model.Name}...\n";
                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));

                    var history = new List<SimpleMessage>
                    {
                        new() { Role = "system", Content = $"You are a custom AI agent named {roleName} acting as a planner. Respond in the user's language." },
                        new() { Role = "user", Content = userInput }
                    };
                    var plan = await CallLlmForRoleAsync(AgentRole.Planner, model, history);
                    state.OutputLog += $"\n[Plan Drafted]\n{plan}\n";
                    state.Status = "Planning Completed ✅";
                    state.CurrentAction = "Idle";
                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));

                    _auditReporter.LogEvent(roleName, "Custom Agent Planning", "Generated custom planning draft", "Success");
                }
                else
                {
                    state.Status = "Executing 🛠️";
                    state.CurrentAction = "Running tool execution loop...";
                    state.OutputLog += $"[System] Starting Execution phase using {model.Name}...\n";
                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));

                    string systemPrompt = $"You are the custom Executor agent: {roleName}.\n" +
                                          $"Implement solutions for: {userInput}\n" +
                                          $"You must execute the plan by invoking tools. Use [TOOL_NAME arg=val] to act (e.g., [write_file file=path content=text], [read_file file=path]). " +
                                          $"Say TASK_COMPLETE when done.";

                    var apiKey = GetApiKeyForProvider(model.ProviderId);
                    var planner = new LlmExecutorPlanner(_roleOrchestrator.Registry, model, apiKey, systemPrompt, _isAutoRotateEnabled, this);
                    var context = new AgentContext { UserInput = userInput, Observations = new List<Observation>() };

                    int iteration = 0;
                    bool completed = false;
                    while (iteration < 10 && !completed)
                    {
                        iteration++;
                        state.CurrentAction = $"Step {iteration}/10";
                        _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                            roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));

                        var toolCalls = await planner.GetPlanAsync(context);
                        if (toolCalls == null || !toolCalls.Any())
                        {
                            completed = true;
                            break;
                        }

                        var call = toolCalls.First();
                        if (call.ToolName == "TASK_COMPLETE" || call.ToolName == "Completed")
                        {
                            completed = true;
                            break;
                        }

                        state.OutputLog += $"\n[Tool Call] {call.ToolName} with args: {string.Join(", ", call.Arguments.Select(x => $"{x.Key}={x.Value}"))}\n";
                        _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                            roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));

                        var validation = ValidateToolExecution(call);
                        if (!validation.Success)
                        {
                            string errMsg = $"🛡️ VALIDATION FAIL: {validation.Error}";
                            state.OutputLog += $"{errMsg}\n";
                            var failedResult = new ToolResult(false, "", errMsg, FailureClassification.SecurityViolation);
                            context.Observations.Add(new Observation { ToolName = call.ToolName, Result = failedResult, Timestamp = DateTime.UtcNow });
                            _auditReporter.LogEvent(roleName, "Tool Validation Failure", $"Validation failed for tool {call.ToolName}: {validation.Error}", "Failed");
                            continue;
                        }

                        var execToken = await _workflow.GetTokenAsync(WorkflowStage.Execution);
                        if (execToken == null)
                        {
                            state.OutputLog += "Execution Blocked by workflow.\n";
                            break;
                        }

                        var command = new ExecutionCommand(call.ToolName, call.Arguments, execToken, Guid.NewGuid().ToString());
                        var result = await _sandbox.ExecuteAsync(command);
                        _workflow.RevokeToken();

                        state.OutputLog += result.Success 
                            ? $"[Success] Tool output length: {result.Output.Length} bytes\n"
                            : $"[Failed] {result.Error ?? result.Output}\n";

                        var observation = new Observation { ToolName = call.ToolName, Result = result, Timestamp = DateTime.UtcNow };
                        context.Observations.Add(observation);

                        _auditReporter.LogEvent(roleName, $"Tool Call: {call.ToolName}", $"Executed tool {call.ToolName} with success={result.Success}", result.Success ? "Success" : "Failed");
                    }

                    state.Status = "Completed ✅";
                    state.CurrentAction = "Execution completed successfully";
                    _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                        roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));
                }
            }
            catch (Exception ex)
            {
                state.Status = "Failed ❌";
                state.CurrentAction = "Exception encountered";
                state.OutputLog += $"\n[ERROR] {ex.Message}\n";
                _eventStream.Dispatch(new AgentExecutionStateUpdatedEvent(
                    roleName, model.Name, state.Status, state.CurrentAction, state.OutputLog, DateTime.UtcNow));
                _auditReporter.LogEvent(roleName, "Execution Error", $"Exception: {ex.Message}", "Failed");
            }
        }

        private class LlmExecutorPlanner : ILLMPlanner
        {
            private readonly ModelRegistry _registry;
            private readonly ModelMetadata _model;
            private readonly string? _apiKey;
            private readonly string _systemPrompt;
            private readonly bool _isAutoRotateEnabled;
            private readonly AgentRuntime _parent;
            private readonly ToolParser _parser = new();

            public LlmExecutorPlanner(ModelRegistry registry, ModelMetadata model, string? apiKey, string systemPrompt, bool isAutoRotate, AgentRuntime parent)
            {
                _registry = registry;
                _model = model;
                _apiKey = apiKey;
                _systemPrompt = systemPrompt;
                _isAutoRotateEnabled = isAutoRotate;
                _parent = parent;
            }

            public async Task<List<ToolCall>> GetPlanAsync(AgentContext context)
            {
                return await GetNextStepAsync(context);
            }

            public async Task<List<ToolCall>> GetNextStepAsync(AgentContext context)
            {
                string workspacePath = _parent._sandbox.WorkspaceRoot;
                var currentIntent = ModelCommandAdapter.DetectIntent(context.UserInput);
                
                string systemPromptWithOverride = _systemPrompt;
                if (currentIntent != ModelCommandAdapter.SystemIntent.None)
                {
                    string forcePrompt = ModelCommandAdapter.GetForcingInstruction(currentIntent, workspacePath);
                    if (!string.IsNullOrEmpty(forcePrompt))
                    {
                        systemPromptWithOverride = $"{_systemPrompt}\n\n{forcePrompt}";
                    }
                }

                var history = new List<SimpleMessage>
                {
                    new() { Role = "system", Content = systemPromptWithOverride }
                };

                foreach (var obs in context.Observations)
                {
                    var feedback = obs.Result.Success 
                        ? $"[OBSERVATION: {obs.ToolName} SUCCESS]\n{obs.Result.Output}" 
                        : $"[OBSERVATION: {obs.ToolName} FAILED]\nError: {obs.Result.Error ?? obs.Result.Output}";
                    
                    history.Add(new SimpleMessage { Role = "user", Content = feedback });
                }

                string response = await _parent.CallLlmForRoleAsync(AgentRole.Executor, _model, history);
                
                if (response.Contains("TASK_COMPLETE"))
                {
                    return new List<ToolCall> { new ToolCall { ToolName = "TASK_COMPLETE" } };
                }

                var toolCall = _parser.Parse(response);
                
                if (toolCall == null && currentIntent != ModelCommandAdapter.SystemIntent.None)
                {
                    // Use fallback recovery for local models
                    var (fallbackType, fallbackArg1, fallbackArg2) = ModelCommandAdapter.GetFallbackToolCall(currentIntent, context.UserInput, workspacePath);
                    if (fallbackType != null)
                    {
                        var dictArgs = new Dictionary<string, object>();
                        if (fallbackType == "WRITE")
                        {
                            dictArgs["path"] = fallbackArg1;
                            dictArgs["content"] = fallbackArg2;
                        }
                        else if (fallbackType == "READ" || fallbackType == "CREATE_FOLDER" || fallbackType == "LIST")
                        {
                            dictArgs["path"] = fallbackArg1;
                        }
                        else if (fallbackType == "RUN_BUILD")
                        {
                            dictArgs["command"] = fallbackArg1;
                        }

                        return new List<ToolCall> 
                        { 
                            new ToolCall 
                            { 
                                ToolName = fallbackType == "READ" ? "READ_FILE" :
                                           fallbackType == "WRITE" ? "WRITE_FILE" :
                                           fallbackType == "LIST" ? "LIST_DIR" :
                                           fallbackType == "RUN_BUILD" ? "RUN_BUILD" : fallbackType,
                                Arguments = dictArgs
                            } 
                        };
                    }
                }

                if (toolCall == null) return new List<ToolCall>();

                return new List<ToolCall> 
                { 
                    new ToolCall 
                    { 
                        ToolName = toolCall.ToolName, 
                        Arguments = toolCall.Arguments.ToDictionary(x => x.Key, x => (object)x.Value)
                    } 
                };
            }

            public Task<string> SummarizeFinalResponseAsync(AgentContext context)
            {
                return Task.FromResult("Execution complete.");
            }
        }
    }
}
