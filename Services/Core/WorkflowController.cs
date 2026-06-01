using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LocalCursor.Services.Core
{
    public class WorkflowController
    {
        private ExecutionMode _mode = ExecutionMode.Human;
        private ExecutionToken? _currentToken;
        private readonly SandboxService _sandbox;
        private readonly Dictionary<WorkflowStage, TaskCompletionSource<ExecutionToken?>> _approvalGates = new();
        private readonly Dictionary<AgentRole, TaskCompletionSource<StageApprovalResult>> _stageGates = new();

        public event Action<AgentRole>? OnStageGatePending;
        public event Action<AgentRole, StageApprovalResult>? OnStageGateResolved;

        public WorkflowController(SandboxService sandbox)
        {
            _sandbox = sandbox;
        }

        public ExecutionMode Mode => _mode;

        public void SetMode(ExecutionMode mode)
        {
            _mode = mode;
            if (mode == ExecutionMode.Auto)
            {
                // Auto mode: issue a generic token for any pending gates
                foreach (var stage in _approvalGates.Keys)
                {
                    _approvalGates[stage].TrySetResult(new ExecutionToken(stage, "AUTO_MODE_GENERIC"));
                }
                foreach (var role in _stageGates.Keys)
                {
                    _stageGates[role].TrySetResult(StageApprovalResult.Approved);
                }
            }
        }

        public async Task<ExecutionToken?> GetTokenAsync(WorkflowStage stage)
        {
            if (_mode == ExecutionMode.Auto) 
            {
                return new ExecutionToken(stage, _sandbox.GenerateSecureToken());
            }

            // Human Mode: Wait for UI to issue token via Approve()
            var tcs = new TaskCompletionSource<ExecutionToken?>();
            _approvalGates[stage] = tcs;
            
            Console.WriteLine($"[EIL] Blocking execution at {stage}. Waiting for Token issuance.");
            var token = await tcs.Task;
            _currentToken = token;
            return token;
        }

        public async Task<StageApprovalResult> GetStageApprovalAsync(AgentRole role)
        {
            if (_mode == ExecutionMode.Auto)
            {
                return StageApprovalResult.Approved;
            }

            var tcs = new TaskCompletionSource<StageApprovalResult>();
            _stageGates[role] = tcs;

            OnStageGatePending?.Invoke(role);

            Console.WriteLine($"[Workflow] Gating stage {role}. Waiting for user action (Approve/Skip/Postpone).");
            var result = await tcs.Task;
            _stageGates.Remove(role);

            OnStageGateResolved?.Invoke(role, result);
            return result;
        }

        public void ResolveStageGate(AgentRole role, StageApprovalResult result)
        {
            if (_stageGates.TryGetValue(role, out var tcs))
            {
                tcs.TrySetResult(result);
            }
        }

        public void Approve(WorkflowStage stage)
        {
            if (_approvalGates.TryGetValue(stage, out var tcs))
            {
                var token = new ExecutionToken(stage, _sandbox.GenerateSecureToken());
                tcs.TrySetResult(token);
                _approvalGates.Remove(stage);
                Console.WriteLine($"[EIL] Token ISSUED for stage {stage}.");
            }
        }

        public void Reject(WorkflowStage stage)
        {
            if (_approvalGates.TryGetValue(stage, out var tcs))
            {
                tcs.TrySetResult(null);
                _approvalGates.Remove(stage);
                _currentToken = null;
            }
        }

        public void RevokeToken() => _currentToken = null;
    }
}
