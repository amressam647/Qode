using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    public class WorkspaceContextService
    {
        private readonly string _settingsPath;
        private readonly SecretsService _secretsService;

        public string WorkspacePath { get; set; } = "";
        public string SelectedProvider { get; set; } = "Google Gemini";
        public string ApiEndpoint { get; set; } = "";
        public string SelectedExecutionMode { get; set; } = "Human";
        public bool IsAutoRotateEnabled { get; set; } = true;

        public List<string> ActiveModelPool { get; } = new();
        public Dictionary<AgentRole, string> RoleBindings { get; } = new();
        public Dictionary<AgentRole, string> ExecutionPolicies { get; } = new();

        public WorkspaceContextService(string workspacePath, SecretsService secretsService)
        {
            WorkspacePath = workspacePath;
            _secretsService = secretsService;

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appDataPath, "LocalCursor");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "settings.json");

            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("WorkspacePath", out var wp)) WorkspacePath = wp.GetString() ?? WorkspacePath;
                    if (root.TryGetProperty("SelectedProvider", out var sp)) SelectedProvider = sp.GetString() ?? SelectedProvider;
                    if (root.TryGetProperty("ApiEndpoint", out var ae)) ApiEndpoint = ae.GetString() ?? ApiEndpoint;
                    if (root.TryGetProperty("SelectedExecutionMode", out var em)) SelectedExecutionMode = em.GetString() ?? SelectedExecutionMode;
                    if (root.TryGetProperty("IsAutoRotateEnabled", out var ar)) IsAutoRotateEnabled = ar.GetBoolean();

                    ActiveModelPool.Clear();
                    if (root.TryGetProperty("ActiveModelPool", out var amp))
                    {
                        foreach (var item in amp.EnumerateArray())
                        {
                            var modelId = item.GetString();
                            if (!string.IsNullOrEmpty(modelId)) ActiveModelPool.Add(modelId);
                        }
                    }

                    RoleBindings.Clear();
                    if (root.TryGetProperty("RoleBindings", out var rb))
                    {
                        foreach (var prop in rb.EnumerateObject())
                        {
                            if (Enum.TryParse<AgentRole>(prop.Name, out var role))
                            {
                                RoleBindings[role] = prop.Value.GetString() ?? "";
                            }
                        }
                    }

                    ExecutionPolicies.Clear();
                    if (root.TryGetProperty("ExecutionPolicies", out var ep))
                    {
                        foreach (var prop in ep.EnumerateObject())
                        {
                            if (Enum.TryParse<AgentRole>(prop.Name, out var role))
                            {
                                ExecutionPolicies[role] = prop.Value.GetString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load workspace settings: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                var rbDict = new Dictionary<string, string>();
                foreach (var kv in RoleBindings) rbDict[kv.Key.ToString()] = kv.Value;

                var epDict = new Dictionary<string, string>();
                foreach (var kv in ExecutionPolicies) epDict[kv.Key.ToString()] = kv.Value;

                var settings = new
                {
                    WorkspacePath,
                    SelectedProvider,
                    ApiEndpoint,
                    SelectedExecutionMode,
                    IsAutoRotateEnabled,
                    ActiveModelPool,
                    RoleBindings = rbDict,
                    ExecutionPolicies = epDict
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save workspace settings: {ex.Message}");
            }
        }
    }
}
