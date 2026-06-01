using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace LocalCursor.Services.Core
{
    public record WorkspaceState
    {
        public AgentState SessionState { get; init; } = AgentState.Idle;
        public string CorrelationId { get; init; } = "";
        public string WorkspacePath { get; init; } = "";
        
        public AgentRole? CurrentAgent { get; init; }
        public ModelMetadata? CurrentModel { get; init; }
        
        public ImmutableDictionary<AgentRole, string> ExecutionGraph { get; init; } = ImmutableDictionary<AgentRole, string>.Empty
            .Add(AgentRole.Planner, "Pending")
            .Add(AgentRole.PlanReviewer, "Pending")
            .Add(AgentRole.Executor, "Pending")
            .Add(AgentRole.Reviewer, "Pending")
            .Add(AgentRole.SecurityReviewer, "Pending");
        
        public ImmutableList<Observation> MemorySnapshot { get; init; } = ImmutableList<Observation>.Empty;
        public ImmutableList<string> FileDiffState { get; init; } = ImmutableList<string>.Empty;
        public string ToolState { get; init; } = "Ready";
        public string LastToolOutput { get; init; } = "";
        
        // Phase 2.2 Hardening: AI Execution Ledger properties
        public string ExecutionFingerprint { get; init; } = "";
        public ImmutableList<string> HotFiles { get; init; } = ImmutableList<string>.Empty;
        public string ActiveRules { get; init; } = "";
    }
}
