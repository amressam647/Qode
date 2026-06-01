using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LocalCursor.Services.Core
{
    public class ModelRegistry
    {
        private readonly Dictionary<string, IAIProvider> _providers = new();
        private readonly List<ModelMetadata> _cachedModels = new();

        public ModelRegistry() { }

        public void RegisterProvider(IAIProvider provider)
        {
            _providers[provider.Id] = provider;
        }

        public async Task RefreshAllAsync(Dictionary<string, string> providerKeys, Dictionary<string, string> providerEndpoints)
        {
            _cachedModels.Clear();
            foreach (var provider in _providers.Values)
            {
                try
                {
                    providerKeys.TryGetValue(provider.Id, out var key);
                    providerEndpoints.TryGetValue(provider.Id, out var endpoint);
                    var models = await provider.DiscoverModelsAsync(key, endpoint);
                    _cachedModels.AddRange(models);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to refresh models for {provider.Name}: {ex.Message}");
                }
            }
        }

        public List<ModelMetadata> GetAllModels() => _cachedModels.ToList();
        
        public List<ModelMetadata> GetModelsByProvider(string providerId) => 
            _cachedModels.Where(m => m.ProviderId == providerId).ToList();

        public IAIProvider? GetProvider(string providerId) => 
            _providers.TryGetValue(providerId, out var provider) ? provider : null;

        public ModelMetadata GetDefaultModel() => _cachedModels.FirstOrDefault() ?? new ModelMetadata("default", "Default Model", "local", ModelCapabilities.Text, true);
    }
}
