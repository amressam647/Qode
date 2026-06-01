using System;
using System.Collections.Generic;

namespace LocalCursor.Services.Core
{
    public interface IWorkspaceEvent
    {
        DateTime Timestamp { get; }
    }

    public record SessionStartedEvent(string CorrelationId, string WorkspacePath, DateTime Timestamp) : IWorkspaceEvent;
    
    public record AgentStateChangedEvent(AgentState FromState, AgentState ToState, string? Reason, DateTime Timestamp) : IWorkspaceEvent;
    
    public record TeamMemberStatusUpdatedEvent(AgentRole Role, string Status, string LastAction, DateTime Timestamp) : IWorkspaceEvent;
    
    public record ToolExecutionStartedEvent(string ToolName, Dictionary<string, object> Arguments, DateTime Timestamp) : IWorkspaceEvent;
    
    public record ToolExecutionCompletedEvent(string ToolName, bool Success, string Output, string? Error, Dictionary<string, object> Arguments, DateTime Timestamp) : IWorkspaceEvent;
    
    public record LlmCallStartedEvent(AgentRole Role, string ModelId, string ProviderId, DateTime Timestamp) : IWorkspaceEvent;
    
    public record LlmCallCompletedEvent(AgentRole Role, string ModelId, string ProviderId, string OutputSnippet, DateTime Timestamp) : IWorkspaceEvent;
    
    public record ModelAutoRotatedEvent(string OldModel, string NewModel, DateTime Timestamp) : IWorkspaceEvent;
    
    public record SessionCompletedEvent(string FinalResponse, DateTime Timestamp) : IWorkspaceEvent;
    
    public record SessionFailedEvent(string ErrorMessage, DateTime Timestamp) : IWorkspaceEvent;
    
    // Phase 2.2 Hardening: Drift Detection Event
    public record DriftDetectedEvent(string Metric, string Expected, string Actual, string Reason, DateTime Timestamp) : IWorkspaceEvent;

    public record AgentExecutionStateUpdatedEvent(string Role, string ModelName, string Status, string CurrentAction, string OutputLog, DateTime Timestamp) : IWorkspaceEvent;
}

