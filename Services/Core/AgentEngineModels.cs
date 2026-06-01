using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalCursor.Services.Core
{
    [Flags]
    public enum ModelCapabilities { None = 0, Text = 1, Vision = 2, ToolCalling = 4, Streaming = 8 }
    
    public enum AgentState { Idle, Planning, Executing, Observing, RePlanning, Completed, Failed }
    
    public enum FailureClassification { None, SecurityViolation, ToolNotFound, ExecutionTimeout, ResourceError, LogicError }
    
    public enum AgentRole { Planner, PlanReviewer, Executor, Reviewer, SecurityReviewer, FrontendDeveloper, BackendDeveloper }
    
    public enum ExecutionMode { Human, Auto }
    
    public enum WorkflowStage { Planning, Execution, Review, Finalization }
    
    public enum DevRole { BackendEngineer, FrontendEngineer, FullStackEngineer, Reviewer, SecurityReviewer, Tester }

    public enum StageApprovalResult { Approved, Skip, Postpone }

    public class QueuedModule : ObservableObject
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        private int _priority;
        public int Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }
        private string _status = "Queued";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }
    }

    public class AgentExecutionState : ObservableObject
    {
        public string Role { get; set; } = "";
        public string ModelName { get; set; } = "";
        private string _status = "Awaiting Assignment";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }
        private string _currentAction = "Idle";
        public string CurrentAction
        {
            get => _currentAction;
            set => SetProperty(ref _currentAction, value);
        }
        private string _outputLog = "";
        public string OutputLog
        {
            get => _outputLog;
            set => SetProperty(ref _outputLog, value);
        }
    }

    public record ModelMetadata(string Id, string Name, string ProviderId, ModelCapabilities Capabilities, bool IsLocal);

    public class SimpleMessage 
    { 
        public string Role { get; set; } = ""; 
        public string Content { get; set; } = ""; 
        public string? AttachedImageBase64 { get; set; } 
        public string? AttachedImageMimeType { get; set; } 
    }

    public interface IAIProvider
    {
        string Id { get; }
        string Name { get; }
        Task<List<ModelMetadata>> DiscoverModelsAsync(string? apiKey = null, string? endpoint = null);
        Task<string> ChatAsync(ModelMetadata model, List<SimpleMessage> history, string? apiKey, CancellationToken ct);
    }

    public class ToolCall
    {
        public string ToolName { get; set; } = "";
        public Dictionary<string, object> Arguments { get; set; } = new();
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
    }

    public record ToolResult(
        bool Success, 
        string Output, 
        string? Error = null, 
        FailureClassification Classification = FailureClassification.None, 
        Dictionary<string, string>? Metadata = null
    );

    public record AgentEvent(AgentState FromState, AgentState ToState, DateTime Timestamp, string? Reason = null);

    public class Observation
    {
        public string ToolName { get; set; } = "";
        public ToolResult Result { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    public record ToolObservation(string ToolName, string Input, string Output, bool IsError);

    public class AgentContext
    {
        public string UserInput { get; set; } = "";
        public List<Observation> Observations { get; set; } = new();
        public List<ToolCall>? PendingToolCalls { get; set; }
        public ToolResult? LastResult { get; set; }
        public string? CurrentTool { get; set; }
        public string? FinalResponse { get; set; }
    }

    public interface ITool
    {
        string Name { get; }
        Task<ToolResult> ExecuteAsync(Dictionary<string, object> args);
    }

    public interface ILLMPlanner
    {
        Task<List<ToolCall>> GetPlanAsync(AgentContext context);
        Task<List<ToolCall>> GetNextStepAsync(AgentContext context);
        Task<string> SummarizeFinalResponseAsync(AgentContext context);
    }

    public record RoutingContext(
        AgentRole Role,
        bool RequiresSpeed = false,
        bool RequiresAccuracy = true,
        bool IsSecurityCritical = false,
        int EstimatedPromptLength = 0
    );

    public record RoleModelBinding
    {
        public AgentRole Role { get; init; }
        public string? ProviderId { get; set; }
        public string? ModelId { get; set; }
        public string? SelectedModelName { get; set; }
    }

    public record TeamMember(DevRole Role, string AssignedModel, string Status, string LastAction);

    public class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public byte[]? ImageData { get; set; }
        public bool HasImage => ImageData != null;
    }

    public partial class RoleBinding : ObservableObject
    {
        public AgentRole Role { get; set; }
        public string RoleName => Role.ToString();
        public string Description { get; set; } = "";
        
        [ObservableProperty] private ModelMetadata? _selectedModel;
        public ObservableCollection<ModelMetadata> AvailableModels { get; } = new();

        [ObservableProperty] private string? _selectedModelName;
        [ObservableProperty] private bool _isAuto = true;
        [ObservableProperty] private string _executionPolicy = "";
    }

    public partial class ActiveModelInfo : ObservableObject
    {
        public ModelMetadata Model { get; set; } = null!;
        public string Provider => Model.ProviderId;
        public string ModelName => Model.Name;
        
        [ObservableProperty] private string _assignedRole = "None";
        public Action? OnChanged { get; set; }
        partial void OnAssignedRoleChanged(string value) => OnChanged?.Invoke();
    }

    public interface IAgentEventStream
    {
        void Publish(AgentEvent e);
        void PublishMessage(ChatMessage message);
        void UpdateTeamMember(TeamMember member);
        event Action<object> OnAnyEvent;
        void Dispatch(IWorkspaceEvent @event);
    }

    public sealed class ExecutionToken
    {
        public string Secret { get; }
        public WorkflowStage Stage { get; }
        public DateTime IssuedAt { get; } = DateTime.UtcNow;
        public TimeSpan Expiry { get; } = TimeSpan.FromMinutes(10);

        public ExecutionToken(WorkflowStage stage, string secret)
        {
            Stage = stage;
            Secret = secret;
        }

        public bool IsValid(WorkflowStage requiredStage) 
            => Stage == requiredStage && (DateTime.UtcNow - IssuedAt) < Expiry;
    }

    public record ExecutionCommand(
        string ToolName,
        Dictionary<string, object> Arguments,
        ExecutionToken Token,
        string RequestId = ""
    );

    public partial class UIState : ObservableObject
    {
        public ObservableCollection<ExecutionTraceEntry> Trace { get; } = new();
        public ObservableCollection<ChatMessage> Chat { get; } = new();
        public ObservableCollection<TeamMember> Team { get; } = new();

        [ObservableProperty] private AgentState _state;
        [ObservableProperty] private WorkflowStage _currentStage;
        [ObservableProperty] private ExecutionMode _mode;

        public bool IsAgentBusy => State != AgentState.Idle && State != AgentState.Completed && State != AgentState.Failed;
    }

    public record ExecutionTraceEntry(
        int StepNumber,
        AgentState State,
        string Thought,
        string? Action,
        ToolObservation? Observation,
        DateTime Timestamp
    );
}
