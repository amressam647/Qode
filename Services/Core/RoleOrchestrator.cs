using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalCursor.Services.Core
{
    public class RoleOrchestrator
    {
        private readonly ModelRegistry _registry;
        private readonly SmartModelRouter _router;
        private readonly List<RoleModelBinding> _bindings = new();

        public ModelRegistry Registry => _registry;

        public RoleOrchestrator(ModelRegistry registry)
        {
            _registry = registry;
            _router = new SmartModelRouter(registry);
        }

        public void BindRole(AgentRole role, string providerId, string modelId)
        {
            var existing = _bindings.FirstOrDefault(b => b.Role == role);
            if (existing != null) _bindings.Remove(existing);
            _bindings.Add(new RoleModelBinding { Role = role, ProviderId = providerId, ModelId = modelId });
        }

        public ModelMetadata ResolveModel(AgentRole role)
        {
            // Rule 1: Explicit User Configuration
            var binding = _bindings.FirstOrDefault(b => b.Role == role);
            if (binding != null)
            {
                var model = _registry.GetAllModels().FirstOrDefault(m => m.ProviderId == binding.ProviderId && m.Id == binding.ModelId);
                if (model != null) return model;
            }

            // Rule 2: Smart Router
            try
            {
                var context = new RoutingContext(role);
                return _router.Route(context);
            }
            catch
            {
                // Rule 3: Absolute Fallback (First Available)
                var fallback = _registry.GetAllModels().FirstOrDefault();
                if (fallback == null) throw new InvalidOperationException("CRITICAL: No models available in the system.");
                return fallback;
            }
        }

        public List<RoleModelBinding> GetCurrentBindings() => _bindings.ToList();
    }
}
