using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace LocalCursor.Services.Core
{
    public class AgentEventStream : IAgentEventStream
    {
        public event Action<AgentEvent>? OnEventPublished;
        public event Action<object>? OnAnyEvent;

        public void Publish(AgentEvent e)
        {
            OnEventPublished?.Invoke(e);
            OnAnyEvent?.Invoke(e);
            
            // Dispatch to central state store
            Bootstrapper.StateStore?.Dispatch(new AgentStateChangedEvent(
                e.FromState,
                e.ToState,
                e.Reason,
                e.Timestamp
            ));
        }

        public void PublishMessage(ChatMessage message)
        {
            OnAnyEvent?.Invoke(message);
        }

        public void UpdateTeamMember(TeamMember member)
        {
            OnAnyEvent?.Invoke(member);
            
            // Dispatch state store status
            var role = MapDevRoleToAgentRole(member.Role);
            Bootstrapper.StateStore?.Dispatch(new TeamMemberStatusUpdatedEvent(
                role,
                member.Status,
                member.LastAction,
                DateTime.UtcNow
            ));
        }

        public void Dispatch(IWorkspaceEvent @event)
        {
            // Direct dispatch to central store
            Bootstrapper.StateStore?.Dispatch(@event);
            
            // Also notify any general subscribers of the event for backwards compatibility
            OnAnyEvent?.Invoke(@event);
        }

        private AgentRole MapDevRoleToAgentRole(DevRole devRole) => devRole switch
        {
            DevRole.Reviewer => AgentRole.Reviewer,
            DevRole.SecurityReviewer => AgentRole.SecurityReviewer,
            DevRole.FrontendEngineer => AgentRole.FrontendDeveloper,
            DevRole.BackendEngineer => AgentRole.BackendDeveloper,
            _ => AgentRole.Executor
        };
    }


    public class UIStateMapper
    {
        private readonly UIState _uiState;

        public UIStateMapper(UIState uiState)
        {
            _uiState = uiState;
        }

        public void Map(AgentEvent e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                _uiState.State = e.ToState;
                
                var entry = new ExecutionTraceEntry(
                    _uiState.Trace.Count + 1,
                    e.ToState,
                    e.Reason ?? "Processing...",
                    null,
                    null,
                    e.Timestamp
                );
                
                _uiState.Trace.Add(entry);
                if (_uiState.Trace.Count > 100) _uiState.Trace.RemoveAt(0);
            }));
        }
    }
}
