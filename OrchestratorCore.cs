using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalCursor.Services.Core;

namespace LocalCursor.Services.Core
{
    /// <summary>
    /// THE CANONICAL ORCHESTRATOR.
    /// Manages the full agentic lifecycle, model resolution, and session security.
    /// </summary>
    public class OrchestratorCore
    {
        private readonly RoleOrchestrator _roleOrchestrator;
        public RoleOrchestrator RoleOrchestrator => _roleOrchestrator;
        private readonly SandboxService _sandbox;
        private readonly WorkflowController _workflow;
        public WorkflowController Workflow => _workflow;
        private readonly IAgentEventStream _eventStream;
        public IAgentEventStream EventStream => _eventStream;
        private readonly SecretsService _secretsService;
        private readonly ModelRegistry _registry;
        
        private AgentRuntime _runtime;
        public AgentRuntime? Runtime => _runtime;
        private CancellationTokenSource _cts;
        private bool _isKillSwitchTriggered = false;

        public bool IsSandboxMode { get; set; } = true; // Safety First

        public event Action<string> OnStatusChanged;
        public event Action<string, string> OnToolExecuted;
        public event Action<string> OnResponseReceived;
        public event Action<string> OnTraceUpdated;

        public OrchestratorCore(
            RoleOrchestrator roleOrchestrator,
            SandboxService sandbox,
            WorkflowController workflow,
            IAgentEventStream eventStream,
            SecretsService secretsService,
            ModelRegistry registry)
        {
            _roleOrchestrator = roleOrchestrator;
            _sandbox = sandbox;
            _workflow = workflow;
            _eventStream = eventStream;
            _secretsService = secretsService;
            _registry = registry;
        }

        public async Task ProcessRequestAsync(string userMessage, List<RoleBinding> roleBindings, byte[] imageData = null, string imageMimeType = null)
        {
            _cts = new CancellationTokenSource();
            
            // Generate Correlation ID for tracing
            string correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            LogStructured("SESSION_START", correlationId, new { Message = userMessage });

            try
            {
                if (_isKillSwitchTriggered)
                {
                    OnStatusChanged?.Invoke("🚨 SYSTEM LOCKED: Kill switch active.");
                    return;
                }

                // Phase 2.3: Reset execution budget for new session
                Bootstrapper.Budget?.Reset();

                // 1. Resolve Workspace AutoRotate status from central context
                bool isAutoRotate = Bootstrapper.WorkspaceContext?.IsAutoRotateEnabled ?? true;

                // 2. Initialize Runtime with context mapping
                _runtime = new AgentRuntime(
                    _sandbox, 
                    _workflow, 
                    _eventStream, 
                    _roleOrchestrator, 
                    _secretsService, 
                    roleBindings, 
                    isAutoRotate
                );
                
                _runtime.OnStateChanged += (state) => OnStatusChanged?.Invoke($"AI Team State: {state} [{correlationId}]");

                // 3. Run Execution Loop
                OnStatusChanged?.Invoke("Thinking...");
                string finalResponse = await _runtime.RunAsync(userMessage);

                // 4. Finalize Trace & Results
                UpdateTrace(correlationId);
                OnResponseReceived?.Invoke(finalResponse);
                
                LogStructured("SESSION_COMPLETE", correlationId, new { Success = true });
            }
            catch (Exception ex)
            {
                // Phase 2.3: Record failure to FailureMemory for learning
                Bootstrapper.FailureMemory?.RecordFailure(ex.Message, $"Session {correlationId}");
                LogStructured("SESSION_ERROR", correlationId, new { Error = ex.Message });
                throw;
            }
        }

        private ModelMetadata ResolveModelForRole(AgentRole role, List<RoleBinding> roleBindings)
        {
            var binding = roleBindings.FirstOrDefault(b => b.Role == role);
            if (binding != null && binding.SelectedModel != null)
                return binding.SelectedModel;
            
            return _roleOrchestrator.ResolveModel(role);
        }

        private void UpdateTrace(string correlationId)
        {
            string trace = $"\n--- AGENT SESSION [{correlationId}] COMPLETE ---\n";
            foreach (var obs in _runtime.Observations)
            {
                trace += $"[OBSERVATION: {obs.ToolName} | CID: {correlationId}]\n";
                trace += $"SUCCESS: {obs.Result.Success}\n";
                trace += $"OUTPUT: {(obs.Result.Output.Length > 200 ? obs.Result.Output.Substring(0, 200) + "..." : obs.Result.Output)}\n";
                if (!string.IsNullOrEmpty(obs.Result.Error)) trace += $"ERROR: {obs.Result.Error}\n";
                trace += "----------------------------\n";
            }
            OnTraceUpdated?.Invoke(trace);
        }

        private void LogStructured(string eventType, string correlationId, object data)
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                Event = eventType,
                CID = correlationId,
                Data = data
            };
            
            // Persistence logic here (e.g. JSON file)
            string logDir = Path.Combine(_sandbox.WorkspaceRoot, ".qode_logs");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            
            File.AppendAllText(
                Path.Combine(logDir, "agent_structured.log"), 
                System.Text.Json.JsonSerializer.Serialize(logEntry) + Environment.NewLine
            );
        }

        public void Stop()
        {
            _cts?.Cancel();
            OnStatusChanged?.Invoke("Stopped.");
        }

        public async Task RunCustomAgentAsync(string name, string stage, string input, ModelMetadata model)
        {
            if (_runtime == null)
            {
                bool isAutoRotate = Bootstrapper.WorkspaceContext?.IsAutoRotateEnabled ?? true;
                _runtime = new AgentRuntime(_sandbox, _workflow, _eventStream, _roleOrchestrator, _secretsService, new List<RoleBinding>(), isAutoRotate);
            }
            _ = Task.Run(async () =>
            {
                await _runtime.RunCustomAgentAsync(name, stage, input, model);
            });
        }

        public void TriggerKillSwitch()
        {
            _isKillSwitchTriggered = true;
            _cts?.Cancel();
            OnStatusChanged?.Invoke("🚨 EMERGENCY STOP: Kill switch triggered.");
            LogStructured("KILL_SWITCH_TRIGGERED", "EMERGENCY", new { Action = "Kernel Halt" });
        }
    }
}
