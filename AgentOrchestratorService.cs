using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LocalCursor.Services.Core;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    /// <summary>
    /// SERVICE BRIDGE (Phase 4).
    /// Provides a clean API for the UI. Delegates all logic to OrchestratorCore.
    /// </summary>
    public class AgentOrchestratorService
    {
        private readonly OrchestratorCore _core;
        public OrchestratorCore Core => _core;

        public event Action<string> OnStatusChanged;
        public event Action<string> OnResponseReceived;
        public event Action<string> OnTraceUpdated;

        public AgentOrchestratorService(OrchestratorCore core)
        {
            _core = core;
            
            // Bridge events from Core to Service
            _core.OnStatusChanged += (s) => OnStatusChanged?.Invoke(s);
            _core.OnResponseReceived += (r) => OnResponseReceived?.Invoke(r);
            _core.OnTraceUpdated += (t) => OnTraceUpdated?.Invoke(t);
        }

        public async Task ProcessRequestAsync(string userMessage, List<RoleBinding> roleBindings, byte[] imageData = null, string imageMimeType = null)
        {
            await _core.ProcessRequestAsync(userMessage, roleBindings, imageData, imageMimeType);
        }

        public void Stop() => _core.Stop();
        public void TriggerKillSwitch() => _core.TriggerKillSwitch();
    }
}
