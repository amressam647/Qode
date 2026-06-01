using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace LocalCursor.Services.Core
{
    public class WorkspaceStateStore
    {
        private readonly object _lock = new();
        private readonly List<IWorkspaceEvent> _events = new();
        private readonly List<WorkspaceState> _checkpoints = new();
        
        public WorkspaceState CurrentState { get; private set; } = new();
        public IReadOnlyList<WorkspaceState> Checkpoints
        {
            get
            {
                lock (_lock)
                {
                    return _checkpoints.AsReadOnly();
                }
            }
        }
        
        public event Action<IWorkspaceEvent, WorkspaceState>? OnStateUpdated;
        
        public void Dispatch(IWorkspaceEvent @event)
        {
            lock (_lock)
            {
                _events.Add(@event);
                var nextState = ApplyEvent(CurrentState, @event);
                CurrentState = nextState;
                _checkpoints.Add(nextState);
                OnStateUpdated?.Invoke(@event, CurrentState);
            }
        }
        
        public WorkspaceState RebuildStateFromEvents(IEnumerable<IWorkspaceEvent> events)
        {
            var state = new WorkspaceState();
            foreach (var @event in events)
            {
                state = ApplyEvent(state, @event);
            }
            return state;
        }

        public WorkspaceState Replay(IEnumerable<IWorkspaceEvent> events)
        {
            return RebuildStateFromEvents(events);
        }
        
        private WorkspaceState ApplyEvent(WorkspaceState state, IWorkspaceEvent @event)
        {
            WorkspaceState nextState = @event switch
            {
                SessionStartedEvent e => state with
                {
                    CorrelationId = e.CorrelationId,
                    WorkspacePath = e.WorkspacePath,
                    SessionState = AgentState.Idle,
                    CurrentAgent = null,
                    CurrentModel = null,
                    ToolState = "Ready",
                    LastToolOutput = "",
                    MemorySnapshot = ImmutableList<Observation>.Empty,
                    FileDiffState = ImmutableList<string>.Empty,
                    HotFiles = ImmutableList<string>.Empty,
                    ActiveRules = "",
                    ExecutionGraph = state.ExecutionGraph
                        .SetItem(AgentRole.Planner, "Pending")
                        .SetItem(AgentRole.PlanReviewer, "Pending")
                        .SetItem(AgentRole.Executor, "Pending")
                        .SetItem(AgentRole.Reviewer, "Pending")
                        .SetItem(AgentRole.SecurityReviewer, "Pending")
                },
                
                AgentStateChangedEvent e => state with
                {
                    SessionState = e.ToState,
                    ActiveRules = e.Reason ?? state.ActiveRules
                },
                
                TeamMemberStatusUpdatedEvent e => state with
                {
                    CurrentAgent = e.Role,
                    ExecutionGraph = state.ExecutionGraph.SetItem(e.Role, e.Status)
                },
                
                ToolExecutionStartedEvent e => state with
                {
                    ToolState = $"Invoking {e.ToolName}..."
                },
                
                ToolExecutionCompletedEvent e => ProcessToolExecutionCompletion(state, e),
                
                LlmCallStartedEvent e => state with
                {
                    CurrentAgent = e.Role
                },
                
                LlmCallCompletedEvent e => state,
                
                ModelAutoRotatedEvent e => state with
                {
                    ToolState = $"Rotated: {e.OldModel} -> {e.NewModel}"
                },
                
                SessionCompletedEvent e => state with
                {
                    SessionState = AgentState.Completed,
                    ToolState = "Session Completed Successfully"
                },
                
                SessionFailedEvent e => state with
                {
                    SessionState = AgentState.Failed,
                    ToolState = $"Session Failed: {e.ErrorMessage}"
                },

                DriftDetectedEvent e => state with
                {
                    ToolState = $"Drift Detected: {e.Reason}"
                },
                
                _ => state
            };

            // Seal with cryptographic execution fingerprint
            string fingerprint = ComputeFingerprint(nextState);
            return nextState with { ExecutionFingerprint = fingerprint };
        }

        private WorkspaceState ProcessToolExecutionCompletion(WorkspaceState state, ToolExecutionCompletedEvent e)
        {
            var obs = new Observation
            {
                ToolName = e.ToolName,
                Result = new ToolResult(e.Success, e.Output, e.Error),
                Timestamp = e.Timestamp
            };

            string newToolState = e.Success ? $"Success: {e.ToolName}" : $"Failed: {e.ToolName}";
            string output = e.Output;
            if (!string.IsNullOrEmpty(e.Error))
            {
                output += $"\nError: {e.Error}";
            }

            var hotFiles = state.HotFiles;
            var fileDiffState = state.FileDiffState;

            if (e.Success && (e.ToolName.Equals("WRITE_FILE", StringComparison.OrdinalIgnoreCase) || e.ToolName.Equals("FILE_WRITE", StringComparison.OrdinalIgnoreCase)))
            {
                if (e.Arguments.TryGetValue("path", out var pathObj) && pathObj is string path)
                {
                    if (!hotFiles.Contains(path))
                    {
                        hotFiles = hotFiles.Add(path);
                    }
                    if (!fileDiffState.Contains(path))
                    {
                        fileDiffState = fileDiffState.Add(path);
                    }
                }
            }

            return state with
            {
                ToolState = newToolState,
                LastToolOutput = output,
                MemorySnapshot = state.MemorySnapshot.Add(obs),
                HotFiles = hotFiles,
                FileDiffState = fileDiffState
            };
        }

        private string ComputeFingerprint(WorkspaceState state)
        {
            string raw = $"{state.CorrelationId}|{state.SessionState}|{state.ToolState}|{state.CurrentModel?.Id ?? "None"}|{state.ActiveRules}|{state.LastToolOutput}";
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash).Substring(0, 16);
        }
        
        public IEnumerable<IWorkspaceEvent> GetEvents()
        {
            lock (_lock)
            {
                return new List<IWorkspaceEvent>(_events);
            }
        }
    }
}
