using System;
using System.Collections.Generic;
using System.IO;
using LocalCursor.Services;
using LocalCursor.Services.Core;
using LocalCursor.Services.Tools;
using LocalCursor.Services.Providers;

namespace LocalCursor
{
    public static class Bootstrapper
    {
        public static AgentOrchestratorService Orchestrator { get; private set; }
        public static RoleOrchestrator RoleOrchestrator { get; private set; }
        public static FileService FileService { get; private set; }
        public static SecretsService SecretsService { get; private set; }
        public static WorkspaceContextService WorkspaceContext { get; private set; }
        public static ModelRegistry Registry { get; private set; }
        public static SandboxService Sandbox { get; private set; }
        public static TerminalService TerminalService { get; private set; }
        public static WorkspaceStateStore StateStore { get; private set; }

        // Phase 2.3: Safety services wired into pipeline
        public static ExecutionBudget Budget { get; private set; }
        public static FailureMemory FailureMemory { get; private set; }
        public static PromptGovernor PromptGovernor { get; private set; }
        public static InvariantChecker InvariantChecker { get; private set; }
        public static ExecutionPolicy ExecutionPolicy { get; private set; }
        public static SemanticIndex SemanticIndex { get; private set; }

        public static void Init()
        {
            string workspacePath = Directory.GetCurrentDirectory();
            
            // 1. Core Services
            SecretsService = new SecretsService("");
            WorkspaceContext = new WorkspaceContextService(workspacePath, SecretsService);
            StateStore = new WorkspaceStateStore();
            Registry = new ModelRegistry();
            Registry.RegisterProvider(new GeminiProvider());
            Registry.RegisterProvider(new OllamaProvider());
            Registry.RegisterProvider(new OpenAIProvider());
            Registry.RegisterProvider(new DeepSeekProvider());
            Registry.RegisterProvider(new GroqProvider());
            Registry.RegisterProvider(new MoonshotProvider());
            Registry.RegisterProvider(new GrokProvider());
            Registry.RegisterProvider(new LMStudioProvider());

            RoleOrchestrator = new RoleOrchestrator(Registry);
            FileService = new FileService(workspacePath);

            // 2. Phase 2.3: Safety & Intelligence Services
            Budget = new ExecutionBudget(workspacePath);
            FailureMemory = new FailureMemory(workspacePath);
            PromptGovernor = new PromptGovernor(maxContextTokens: 8000, reservedForOutput: 2000);
            InvariantChecker = new InvariantChecker();
            ExecutionPolicy = new ExecutionPolicy();
            SemanticIndex = new SemanticIndex(workspacePath);

            // Wire budget events to state store
            Budget.OnBudgetWarning += (resource, current, max) =>
            {
                StateStore?.Dispatch(new DriftDetectedEvent(
                    "BudgetWarning", $"{resource} limit: {max}", $"{resource} usage: {current}",
                    $"Budget at {(double)current/max*100:F0}% for {resource}", DateTime.UtcNow));
            };

            // 3. Tools & Workflow
            TerminalService = new TerminalService(workspacePath);
            var tools = new List<ITool>
            {
                new FileReadTool(FileService),
                new FileWriteTool(FileService),
                new ListDirTool(FileService),
                new TerminalTool(TerminalService)
            };

            var executor = new ToolExecutor(tools);
            Sandbox = new SandboxService(workspacePath, executor);

            var workflow = new WorkflowController(Sandbox);
            var eventStream = new AgentEventStream();
            
            // 4. Orchestrator (The Director)
            var core = new OrchestratorCore(RoleOrchestrator, Sandbox, workflow, eventStream, SecretsService, Registry);
            Orchestrator = new AgentOrchestratorService(core);

            // 5. Background index workspace (non-blocking)
            Task.Run(() => { try { SemanticIndex.IndexWorkspace(workspacePath); } catch { } });
        }
    }
}
