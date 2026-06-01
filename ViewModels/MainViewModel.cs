using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LocalCursor.Services;
using LocalCursor.Services.Core;

namespace LocalCursor.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly AgentOrchestratorService _orchestrator;
        private readonly FileService _fileService;
        private readonly SecretsService _secretsService;
        private readonly ModelRegistry _registry;
        private readonly SpeechService _speechService = new();
        private DispatcherTimer _cursorUpdateTimer;

        public MainViewModel(
            AgentOrchestratorService orchestrator,
            FileService fileService,
            SecretsService secretsService,
            ModelRegistry registry)
        {
            _orchestrator = orchestrator;
            _fileService = fileService;
            _secretsService = secretsService;
            _registry = registry;
            
            _cursorUpdateTimer = new DispatcherTimer();
            _cursorUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            _cursorUpdateTimer.Tick += CursorUpdateTimer_Tick;

            InitializeRoles();
            LoadSettings();

            // Wire up central State Store events for thread-safe deterministic projection (Phase 2 Hardening)
            Bootstrapper.StateStore.OnStateUpdated += (evt, state) => {
                Application.Current.Dispatcher.BeginInvoke(() => {
                    // Update overall status
                    AgentStatus = $"State: {state.SessionState} | Tool: {state.ToolState}";
                    
                    // Synchronize active team member statuses deterministically from canonical state store
                    foreach (var role in state.ExecutionGraph.Keys)
                    {
                        var devRole = MapToDevRole(role);
                        var binding = RoleBindings.FirstOrDefault(b => b.Role == role);
                        string modelName = binding?.SelectedModel?.Name ?? "Unassigned";
                        
                        string status = state.ExecutionGraph[role];
                        string lastAction = state.CurrentAgent == role ? "Active" : "Awaiting";
                        
                        var updatedMember = new TeamMember(devRole, modelName, status, lastAction);
                        
                        var existing = Team.FirstOrDefault(t => t.Role == devRole);
                        if (existing != null)
                        {
                            int idx = Team.IndexOf(existing);
                            Team[idx] = updatedMember;
                        }
                        else
                        {
                            Team.Add(updatedMember);
                        }
                    }
                });
            };

            _orchestrator.OnResponseReceived += (resp) => {
                Application.Current.Dispatcher.BeginInvoke(() => {
                    if (!ChatHistory.Any(c => c.Content == resp && c.Role == "assistant"))
                    {
                        ChatHistory.Add(new ChatMessage { Content = resp, Role = "assistant", Timestamp = DateTime.Now });
                    }
                });
            };

            // Wire up multi-agent dynamic updates for Chat
            _orchestrator.Core.EventStream.OnAnyEvent += (evt) => {
                if (evt is ChatMessage msg)
                {
                    Application.Current.Dispatcher.BeginInvoke(() => {
                        if (!ChatHistory.Any(c => c.Content == msg.Content && c.Role == msg.Role))
                        {
                            ChatHistory.Add(msg);
                        }
                    });
                }
                else if (evt is AgentExecutionStateUpdatedEvent stateEvt)
                {
                    Application.Current.Dispatcher.BeginInvoke(() => {
                        var existing = ActiveAgents.FirstOrDefault(a => a.Role == stateEvt.Role);
                        if (existing != null)
                        {
                            existing.ModelName = stateEvt.ModelName;
                            existing.Status = stateEvt.Status;
                            existing.CurrentAction = stateEvt.CurrentAction;
                            existing.OutputLog = stateEvt.OutputLog;
                        }
                        else
                        {
                            ActiveAgents.Add(new AgentExecutionState
                            {
                                Role = stateEvt.Role,
                                ModelName = stateEvt.ModelName,
                                Status = stateEvt.Status,
                                CurrentAction = stateEvt.CurrentAction,
                                OutputLog = stateEvt.OutputLog
                            });
                        }
                    });
                }
            };

            // Wire up stage gating callbacks
            _orchestrator.Core.Workflow.OnStageGatePending += (role) => {
                Application.Current.Dispatcher.BeginInvoke(() => {
                    PendingStageRole = role;
                    PendingStageName = MapRoleToStageName(role);
                    PendingStageModuleName = GetModuleNameForPending();
                    PendingStageAnnouncement = $"أنا الآن أعمل في {PendingStageName} بموديول {PendingStageModuleName}";
                    IsStageGatePending = true;
                });
            };

            _orchestrator.Core.Workflow.OnStageGateResolved += (role, result) => {
                Application.Current.Dispatcher.BeginInvoke(() => {
                    IsStageGatePending = false;
                });
            };
        }

        [ObservableProperty] private string _windowTitle = "Qode (LocalCursor) - Modern C# AI IDE";
        [ObservableProperty] private string? _userMessage;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string? _agentStatus;
        [ObservableProperty] private ObservableCollection<ChatMessage> _chatHistory = new();
        [ObservableProperty] private ObservableCollection<TeamMember> _team = new();
        [ObservableProperty] private ObservableCollection<RoleBinding> _roleBindings = new();
        [ObservableProperty] private ObservableCollection<ActiveModelInfo> _activeModels = new();
        [ObservableProperty] private string _terminalOutput = "";
        [ObservableProperty] private string _terminalInput = "";
        [ObservableProperty] private bool _isTerminalVisible;
        [ObservableProperty] private string? _workspacePath;
        [ObservableProperty] private bool _isSettingsVisible;
        [ObservableProperty] private bool _isTeamVisible;
        [ObservableProperty] private string? _fileContent;
        [ObservableProperty] private string? _currentFileName;
        [ObservableProperty] private string? _voiceAssistantStatus;
        [ObservableProperty] private EditorTab? _activeTab;
        [ObservableProperty] private FileItem? _selectedFile;

        [ObservableProperty] private ObservableCollection<AgentExecutionState> _activeAgents = new();
        [ObservableProperty] private ObservableCollection<QueuedModule> _moduleQueue = new();

        // Gating stage properties
        [ObservableProperty] private bool _isStageGatePending;
        [ObservableProperty] private string _pendingStageName = "";
        [ObservableProperty] private string _pendingStageModuleName = "";
        [ObservableProperty] private string _pendingStageAnnouncement = "";
        [ObservableProperty] private AgentRole _pendingStageRole;

        // Custom dynamic agent properties
        [ObservableProperty] private string _customAgentName = "CustomAgent";
        [ObservableProperty] private string _customAgentInitialStage = "Planning"; // "Planning" or "Execution"
        [ObservableProperty] private ModelMetadata? _customAgentSelectedModel;

        public ObservableCollection<string> AllAvailableModelsFormatted { get; } = new();
        public ObservableCollection<EditorTab> OpenTabs { get; } = new();
        public ObservableCollection<ModelMetadata> FilteredModels { get; } = new();
        public ObservableCollection<FileItem> Files { get; } = new();

        public ObservableCollection<string> AvailableProviders { get; } = new()
        {
            "Google Gemini", "OpenAI", "Ollama", "LM Studio", "DeepSeek", "Groq", "Moonshot", "Grok"
        };
        [ObservableProperty] private string? _selectedProvider = "Google Gemini";
        [ObservableProperty] private string? _apiEndpoint;
        [ObservableProperty] private ModelMetadata? _selectedModelInfo;

        public ObservableCollection<string> RoleOptions { get; } = new()
        {
            "None", "Planner", "PlanReviewer", "Executor", "Reviewer", "SecurityReviewer", "FrontendDeveloper", "BackendDeveloper"
        };

        public ObservableCollection<string> ExecutionModes { get; } = new() { "Human", "Auto" };
        [ObservableProperty] private string? _selectedExecutionMode = "Human";
        [ObservableProperty] private bool _isAutoRotateEnabled = true;
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _connectionStatus = "Ready";

        public bool HasOpenTabs => OpenTabs.Count > 0;

        [ObservableProperty] private byte[]? _attachedImageData;
        [ObservableProperty] private string? _attachedImageMimeType;
        [ObservableProperty] private string? _attachedImageFileName;

        // DPAPI Enveloped API Key Properties
        public string GeminiApiKey
        {
            get => _secretsService.GetSecret("GeminiApiKey") ?? "";
            set { _secretsService.SetSecret("GeminiApiKey", value); OnPropertyChanged(); }
        }
        public string OpenAIApiKey
        {
            get => _secretsService.GetSecret("OpenAIApiKey") ?? "";
            set { _secretsService.SetSecret("OpenAIApiKey", value); OnPropertyChanged(); }
        }
        public string DeepSeekApiKey
        {
            get => _secretsService.GetSecret("DeepSeekApiKey") ?? "";
            set { _secretsService.SetSecret("DeepSeekApiKey", value); OnPropertyChanged(); }
        }
        public string MoonshotApiKey
        {
            get => _secretsService.GetSecret("MoonshotApiKey") ?? "";
            set { _secretsService.SetSecret("MoonshotApiKey", value); OnPropertyChanged(); }
        }
        public string GroqApiKey
        {
            get => _secretsService.GetSecret("GroqApiKey") ?? "";
            set { _secretsService.SetSecret("GroqApiKey", value); OnPropertyChanged(); }
        }
        public string GrokApiKey
        {
            get => _secretsService.GetSecret("GrokApiKey") ?? "";
            set { _secretsService.SetSecret("GrokApiKey", value); OnPropertyChanged(); }
        }

        partial void OnSelectedProviderChanged(string? value)
        {
            if (value == "Ollama") ApiEndpoint = "http://localhost:11434";
            else if (value == "LM Studio") ApiEndpoint = "http://localhost:1234";
            else ApiEndpoint = "";

            RefreshFilteredModels();
        }

        private void RefreshFilteredModels()
        {
            FilteredModels.Clear();
            if (string.IsNullOrEmpty(SelectedProvider)) return;

            var allModels = _registry.GetAllModels();
            foreach (var m in allModels)
            {
                if (m.ProviderId == SelectedProvider)
                {
                    FilteredModels.Add(m);
                }
            }
            if (FilteredModels.Count > 0)
            {
                SelectedModelInfo = FilteredModels[0];
            }
        }

        [RelayCommand]
        private void AddActiveModel()
        {
            if (SelectedModelInfo == null) return;
            if (ActiveModels.Any(am => am.Model.Id == SelectedModelInfo.Id)) return;

            var newActive = new ActiveModelInfo
            {
                Model = SelectedModelInfo,
                AssignedRole = "None"
            };
            newActive.OnChanged = () => SyncActiveModelRoleChange(newActive);
            ActiveModels.Add(newActive);
        }

        [RelayCommand]
        private void RemoveActiveModel(ActiveModelInfo model)
        {
            if (model == null) return;
            ActiveModels.Remove(model);
            
            var binding = RoleBindings.FirstOrDefault(b => b.SelectedModel?.Id == model.Model.Id);
            if (binding != null)
            {
                binding.SelectedModel = null;
                binding.SelectedModelName = null;
            }
            SyncTeamWithRoles();
            SaveRoleBindingsToContext();
        }

        private void SyncActiveModelRoleChange(ActiveModelInfo modelInfo)
        {
            if (modelInfo.AssignedRole == "None")
            {
                foreach (var b in RoleBindings)
                {
                    if (b.SelectedModel?.Id == modelInfo.Model.Id)
                    {
                        b.SelectedModel = null;
                        b.SelectedModelName = null;
                    }
                }
            }
            else if (Enum.TryParse<AgentRole>(modelInfo.AssignedRole, out var role))
            {
                var binding = RoleBindings.FirstOrDefault(b => b.Role == role);
                if (binding != null)
                {
                    binding.SelectedModel = modelInfo.Model;
                    binding.SelectedModelName = $"{modelInfo.Model.ProviderId} - {modelInfo.Model.Name}";
                }
            }
            SyncTeamWithRoles();
            SaveRoleBindingsToContext();
        }

        private async Task AutoDiscoverLocalServices()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            bool ollamaRunning = false;
            try
            {
                var resp = await client.GetAsync("http://localhost:11434");
                ollamaRunning = true;
            }
            catch
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var ollamaPath = Path.Combine(localAppData, @"Programs\Ollama\ollama.exe");
                if (File.Exists(ollamaPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = ollamaPath,
                            Arguments = "serve",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        await Task.Delay(3000);
                        ollamaRunning = true;
                    }
                    catch { }
                }
            }
        }

        [RelayCommand]
        private async Task RefreshModels()
        {
            IsBusy = true;
            
            await AutoDiscoverLocalServices();

            var keys = new Dictionary<string, string>();
            var endpoints = new Dictionary<string, string>();

            foreach (var p in AvailableProviders)
            {
                var secretKey = GetApiKeyNameForProvider(p);
                if (!string.IsNullOrEmpty(secretKey))
                {
                    keys[p] = _secretsService.GetSecret(secretKey) ?? "";
                }
                if (p == SelectedProvider && !string.IsNullOrEmpty(ApiEndpoint))
                {
                    endpoints[p] = ApiEndpoint;
                }
                else if (p == "Ollama")
                {
                    endpoints[p] = "http://localhost:11434";
                }
                else if (p == "LM Studio")
                {
                    endpoints[p] = "http://localhost:1234";
                }
            }

            await _registry.RefreshAllAsync(keys, endpoints);

            AllAvailableModelsFormatted.Clear();
            foreach (var model in _registry.GetAllModels())
            {
                AllAvailableModelsFormatted.Add($"{model.ProviderId} - {model.Name}");
            }

            RefreshFilteredModels();
            IsBusy = false;
        }

        private string GetApiKeyNameForProvider(string provider) => provider switch
        {
            "Google Gemini" => "GeminiApiKey",
            "OpenAI" => "OpenAIApiKey",
            "DeepSeek" => "DeepSeekApiKey",
            "Moonshot" => "MoonshotApiKey",
            "Groq" => "GroqApiKey",
            "Grok" => "GrokApiKey",
            _ => ""
        };

        [RelayCommand]
        private async Task TestConnection()
        {
            IsBusy = true;
            ConnectionStatus = "Testing connections...";
            try
            {
                var keys = new Dictionary<string, string>();
                var endpoints = new Dictionary<string, string>();
                foreach (var p in AvailableProviders)
                {
                    var secretKey = GetApiKeyNameForProvider(p);
                    if (!string.IsNullOrEmpty(secretKey))
                    {
                        keys[p] = _secretsService.GetSecret(secretKey) ?? "";
                    }
                }
                if (!string.IsNullOrEmpty(ApiEndpoint) && SelectedProvider != null)
                {
                    endpoints[SelectedProvider] = ApiEndpoint;
                }

                await _registry.RefreshAllAsync(keys, endpoints);
                var allModels = _registry.GetAllModels();

                if (allModels.Count > 0)
                {
                    IsConnected = true;
                    ConnectionStatus = $"Successfully discovered {allModels.Count} models across providers.";
                }
                else
                {
                    IsConnected = false;
                    ConnectionStatus = "No models discovered. Please check your API keys and endpoints.";
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionStatus = $"Connection test failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void SaveSettings()
        {
            var ctx = Bootstrapper.WorkspaceContext;
            ctx.WorkspacePath = WorkspacePath ?? "";
            ctx.SelectedProvider = SelectedProvider ?? "Google Gemini";
            ctx.ApiEndpoint = ApiEndpoint ?? "";
            ctx.SelectedExecutionMode = SelectedExecutionMode ?? "Human";
            ctx.IsAutoRotateEnabled = IsAutoRotateEnabled;

            ctx.ActiveModelPool.Clear();
            foreach (var am in ActiveModels)
            {
                ctx.ActiveModelPool.Add(am.Model.Id);
            }

            SaveRoleBindingsToContext();
            ctx.Save();

            IsConnected = true;
            ConnectionStatus = "Successfully connected and settings persisted.";
            IsSettingsVisible = false;
        }

        private void SaveRoleBindingsToContext()
        {
            var ctx = Bootstrapper.WorkspaceContext;
            ctx.RoleBindings.Clear();
            ctx.ExecutionPolicies.Clear();

            foreach (var binding in RoleBindings)
            {
                if (binding.SelectedModel != null)
                {
                    ctx.RoleBindings[binding.Role] = binding.SelectedModel.Id;
                }
                if (!string.IsNullOrEmpty(binding.ExecutionPolicy))
                {
                    ctx.ExecutionPolicies[binding.Role] = binding.ExecutionPolicy;
                }
            }
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                WorkspacePath = dialog.FolderName;
                _fileService.SetWorkspacePath(WorkspacePath);
                Bootstrapper.TerminalService.SetWorkingDirectory(WorkspacePath);
                LoadWorkspace(WorkspacePath);
            }
        }

        [RelayCommand]
        private void OpenFolder() => BrowseFolder();

        private void LoadWorkspace(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            Files.Clear();
            var rootDir = new FileItem(path, true);
            rootDir.Name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(rootDir.Name)) rootDir.Name = path;

            LoadDirectoryRecursive(path, rootDir.Children);
            Files.Add(rootDir);
        }

        private void LoadDirectoryRecursive(string path, ObservableCollection<FileItem> collection)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(".") || dirName == "bin" || dirName == "obj" || dirName == "node_modules") continue;

                    var dirItem = new FileItem(dir, true);
                    LoadDirectoryRecursive(dir, dirItem.Children);
                    collection.Add(dirItem);
                }

                foreach (var file in Directory.GetFiles(path))
                {
                    var fileItem = new FileItem(file, false);
                    collection.Add(fileItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load dir {path}: {ex.Message}");
            }
        }

        partial void OnSelectedFileChanged(FileItem? value)
        {
            if (value != null && !value.IsDirectory)
            {
                OpenFileInTab(value.FullPath);
            }
        }

        private void OpenFileInTab(string filePath)
        {
            try
            {
                FileService.KernelLock = true;
                string content = "";
                var relPath = Path.GetRelativePath(_fileService.GetWorkspacePath(), filePath);
                content = _fileService.ReadFile(relPath);

                if (content.StartsWith("Error reading file:"))
                {
                    content = File.ReadAllText(filePath);
                }

                var tab = new EditorTab(Path.GetFileName(filePath), filePath, content);
                OpenTabs.Add(tab);
                ActiveTab = tab;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                FileService.KernelLock = false;
                OnPropertyChanged(nameof(HasOpenTabs));
            }
        }

        partial void OnActiveTabChanged(EditorTab? value)
        {
            if (value != null)
            {
                foreach (var t in OpenTabs) t.IsActive = (t == value);
                FileContent = value.Content;
                CurrentFileName = value.FileName;
            }
            else
            {
                FileContent = "";
                CurrentFileName = "";
            }
        }

        partial void OnFileContentChanged(string? value)
        {
            if (ActiveTab != null && ActiveTab.Content != value)
            {
                ActiveTab.Content = value ?? "";
                ActiveTab.IsModified = true;
            }
        }

        [RelayCommand]
        private void CloseTab(EditorTab tab)
        {
            if (tab == null) return;
            if (tab.IsModified)
            {
                var result = MessageBox.Show($"Save changes to {tab.FileName}?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    SaveDocument(tab);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            OpenTabs.Remove(tab);
            if (ActiveTab == tab)
            {
                ActiveTab = OpenTabs.LastOrDefault();
            }
            OnPropertyChanged(nameof(HasOpenTabs));
        }

        [RelayCommand]
        private void SaveDocument(object? parameter)
        {
            var tab = parameter as EditorTab ?? ActiveTab;
            if (tab == null) return;

            try
            {
                FileService.KernelLock = true;
                var relPath = Path.GetRelativePath(_fileService.GetWorkspacePath(), tab.FilePath);
                var result = _fileService.WriteFile(relPath, tab.Content);

                if (result.StartsWith("Error writing file:"))
                {
                    File.WriteAllText(tab.FilePath, tab.Content);
                }

                tab.IsModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                FileService.KernelLock = false;
            }
        }

        [RelayCommand]
        private async Task ExecuteTerminal()
        {
            if (string.IsNullOrWhiteSpace(TerminalInput)) return;

            var cmd = TerminalInput;
            TerminalInput = "";
            TerminalOutput += $"\n> {cmd}\n";

            try
            {
                TerminalService.KernelLock = true;
                var res = await Bootstrapper.TerminalService.ExecuteCmdAsync(cmd);
                TerminalOutput += res + "\n";
            }
            catch (Exception ex)
            {
                TerminalOutput += $"Error executing command: {ex.Message}\n";
            }
            finally
            {
                TerminalService.KernelLock = false;
            }
        }

        [RelayCommand] private void ClearTerminal() => TerminalOutput = "";
        [RelayCommand] private void ToggleTerminal() => IsTerminalVisible = !IsTerminalVisible;

        [RelayCommand]
        private async Task ToggleRecording()
        {
            if (!_speechService.IsRecording)
            {
                try
                {
                    _speechService.StartRecording();
                    VoiceAssistantStatus = "Recording voice... Click Stop when finished.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Microphone Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    VoiceAssistantStatus = null;
                }
            }
            else
            {
                VoiceAssistantStatus = "Processing transcription...";
                try
                {
                    var audioPath = _speechService.StopRecording();
                    if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
                    {
                        var key = OpenAIApiKey;
                        string text;
                        if (!string.IsNullOrEmpty(key))
                        {
                            text = await _speechService.TranscribeWithWhisperAsync(audioPath, key);
                        }
                        else
                        {
                            text = "[Please set OpenAI API Key in Settings to enable Whisper voice transcription]";
                        }

                        if (!string.IsNullOrEmpty(text) && !text.StartsWith("Transcription error:") && !text.StartsWith("["))
                        {
                            UserMessage = (UserMessage + " " + text).Trim();
                        }
                        else
                        {
                            MessageBox.Show(text, "Transcription Status", MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        try { File.Delete(audioPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to transcribe audio: {ex.Message}", "Transcription Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    VoiceAssistantStatus = null;
                }
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrEmpty(UserMessage) && AttachedImageData == null) return;
            
            var msg = new ChatMessage { Content = UserMessage ?? "", Role = "user", ImageData = AttachedImageData, Timestamp = DateTime.Now };
            ChatHistory.Add(msg);
            
            string content = UserMessage ?? "";
            byte[]? data = AttachedImageData;
            
            UserMessage = "";
            AttachedImageData = null;
            AttachedImageFileName = null;
            IsBusy = true;

            await _orchestrator.ProcessRequestAsync(content, RoleBindings.ToList(), data, AttachedImageMimeType);
            IsBusy = false;
        }

        [RelayCommand]
        private async Task RunAgent()
        {
            if (IsBusy) return;

            var lastUserMsg = ChatHistory.LastOrDefault(m => m.Role == "user")?.Content;
            if (string.IsNullOrEmpty(lastUserMsg))
            {
                lastUserMsg = "Start implementing the planned workspace tasks.";
            }

            IsBusy = true;
            await _orchestrator.ProcessRequestAsync(lastUserMsg, RoleBindings.ToList(), AttachedImageData, AttachedImageMimeType);
            IsBusy = false;
        }

        [RelayCommand] private void ToggleSettings() => IsSettingsVisible = !IsSettingsVisible;
        [RelayCommand] private void ToggleTeam() => IsTeamVisible = !IsTeamVisible;
        
        public void TrySetAttachedImage(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    AttachedImageData = File.ReadAllBytes(path);
                    AttachedImageFileName = Path.GetFileName(path);
                    AttachedImageMimeType = path.EndsWith(".png") ? "image/png" : "image/jpeg";
                }
            }
            catch { }
        }

        private void InitializeRoles()
        {
            RoleBindings.Clear();
            RoleBindings.Add(new RoleBinding { Role = AgentRole.Planner, Description = "Architect & Task Planner" });
            RoleBindings.Add(new RoleBinding { Role = AgentRole.PlanReviewer, Description = "Review Planner plans" });
            RoleBindings.Add(new RoleBinding { Role = AgentRole.Executor, Description = "Code Implementation" });
            RoleBindings.Add(new RoleBinding { Role = AgentRole.Reviewer, Description = "Code Review & Quality" });
            RoleBindings.Add(new RoleBinding { Role = AgentRole.SecurityReviewer, Description = "Vulnerabilities & Security Audit" });

            foreach (var rb in RoleBindings)
                rb.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(RoleBinding.SelectedModel)) SyncTeamWithRoles(); };
        }

        private void SyncTeamWithRoles()
        {
            Team.Clear();
            foreach (var rb in RoleBindings)
            {
                if (rb.SelectedModel == null) continue;
                Team.Add(new TeamMember(MapToDevRole(rb.Role), rb.SelectedModel.Name, "Ready", "Awaiting Assignment"));
            }
        }

        private DevRole MapToDevRole(AgentRole role) => role switch
        {
            AgentRole.Planner => DevRole.FullStackEngineer,
            AgentRole.PlanReviewer => DevRole.Reviewer,
            AgentRole.Executor => DevRole.FullStackEngineer,
            AgentRole.Reviewer => DevRole.Reviewer,
            AgentRole.SecurityReviewer => DevRole.SecurityReviewer,
            AgentRole.FrontendDeveloper => DevRole.FrontendEngineer,
            AgentRole.BackendDeveloper => DevRole.BackendEngineer,
            _ => DevRole.FullStackEngineer
        };

        private void LoadSettings()
        {
            var ctx = Bootstrapper.WorkspaceContext;
            WorkspacePath = ctx.WorkspacePath;
            SelectedProvider = ctx.SelectedProvider;
            ApiEndpoint = ctx.ApiEndpoint;
            SelectedExecutionMode = ctx.SelectedExecutionMode;
            IsAutoRotateEnabled = ctx.IsAutoRotateEnabled;

            ActiveModels.Clear();
            foreach (var modelId in ctx.ActiveModelPool)
            {
                var metadata = _registry.GetAllModels().FirstOrDefault(m => m.Id == modelId);
                if (metadata != null)
                {
                    var assignedRole = ctx.RoleBindings.FirstOrDefault(kv => kv.Value == modelId).Key;
                    var assignedRoleStr = assignedRole == default ? "None" : assignedRole.ToString();
                    
                    var modelInfo = new ActiveModelInfo
                    {
                        Model = metadata,
                        AssignedRole = assignedRoleStr
                    };
                    modelInfo.OnChanged = () => SyncActiveModelRoleChange(modelInfo);
                    ActiveModels.Add(modelInfo);
                }
            }

            foreach (var binding in RoleBindings)
            {
                if (ctx.RoleBindings.TryGetValue(binding.Role, out var modelId))
                {
                    var metadata = _registry.GetAllModels().FirstOrDefault(m => m.Id == modelId);
                    if (metadata != null)
                    {
                        binding.SelectedModel = metadata;
                        binding.SelectedModelName = $"{metadata.ProviderId} - {metadata.Name}";
                    }
                }
                if (ctx.ExecutionPolicies.TryGetValue(binding.Role, out var policy))
                {
                    binding.ExecutionPolicy = policy;
                }
            }

            SyncTeamWithRoles();
            if (!string.IsNullOrEmpty(WorkspacePath))
            {
                LoadWorkspace(WorkspacePath);
            }
            Task.Run(RefreshModels);
        }

        private void CursorUpdateTimer_Tick(object? sender, EventArgs e) { }

        // Gating stage commands
        [RelayCommand]
        private void ApproveStage()
        {
            _orchestrator.Core.Workflow.ResolveStageGate(PendingStageRole, StageApprovalResult.Approved);
            IsStageGatePending = false;
        }

        [RelayCommand]
        private void SkipStage()
        {
            _orchestrator.Core.Workflow.ResolveStageGate(PendingStageRole, StageApprovalResult.Skip);
            IsStageGatePending = false;
        }

        [RelayCommand]
        private void PostponeStage()
        {
            _orchestrator.Core.Workflow.ResolveStageGate(PendingStageRole, StageApprovalResult.Postpone);
            IsStageGatePending = false;
        }

        // Custom Dynamic Agent Command
        [RelayCommand]
        private async Task AddCustomAgent()
        {
            if (string.IsNullOrWhiteSpace(CustomAgentName))
            {
                MessageBox.Show("Please enter a valid agent name.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var model = CustomAgentSelectedModel ?? FilteredModels.FirstOrDefault() ?? _registry.GetAllModels().FirstOrDefault();
            if (model == null)
            {
                MessageBox.Show("No model selected or available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string inputMsg = ChatHistory.LastOrDefault(c => c.Role == "user")?.Content ?? "Analyze the workspace.";
            
            // Add a log state entry so it appears on the dashboard immediately
            if (!ActiveAgents.Any(a => a.Role == CustomAgentName))
            {
                ActiveAgents.Add(new AgentExecutionState
                {
                    Role = CustomAgentName,
                    ModelName = model.Name,
                    Status = "Starting...",
                    CurrentAction = "Initializing Custom Thread..."
                });
            }

            await _orchestrator.Core.RunCustomAgentAsync(CustomAgentName, CustomAgentInitialStage, inputMsg, model);
        }

        // Priority queue commands
        [RelayCommand]
        private void AddFolderToQueue()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                var folderPath = dialog.FolderName;
                var folderName = Path.GetFileName(folderPath);
                if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                if (ModuleQueue.Any(q => q.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    return; // Avoid duplicates
                }

                int nextPriority = ModuleQueue.Count > 0 ? ModuleQueue.Max(q => q.Priority) + 1 : 1;
                ModuleQueue.Add(new QueuedModule
                {
                    Path = folderPath,
                    Name = folderName,
                    Priority = nextPriority,
                    Status = "Queued"
                });
            }
        }

        [RelayCommand]
        private void MoveQueueUp(QueuedModule module)
        {
            if (module == null) return;
            var index = ModuleQueue.IndexOf(module);
            if (index <= 0) return;

            // Swap priorities
            var tempPriority = module.Priority;
            module.Priority = ModuleQueue[index - 1].Priority;
            ModuleQueue[index - 1].Priority = tempPriority;

            // Re-order the collection
            var sorted = ModuleQueue.OrderBy(q => q.Priority).ToList();
            ModuleQueue.Clear();
            foreach (var item in sorted)
            {
                ModuleQueue.Add(item);
            }
        }

        [RelayCommand]
        private void MoveQueueDown(QueuedModule module)
        {
            if (module == null) return;
            var index = ModuleQueue.IndexOf(module);
            if (index < 0 || index >= ModuleQueue.Count - 1) return;

            // Swap priorities
            var tempPriority = module.Priority;
            module.Priority = ModuleQueue[index + 1].Priority;
            ModuleQueue[index + 1].Priority = tempPriority;

            // Re-order the collection
            var sorted = ModuleQueue.OrderBy(q => q.Priority).ToList();
            ModuleQueue.Clear();
            foreach (var item in sorted)
            {
                ModuleQueue.Add(item);
            }
        }

        [RelayCommand]
        private async Task RunQueue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var queuedItems = ModuleQueue.Where(q => q.Status == "Queued" || q.Status == "Failed").OrderBy(q => q.Priority).ToList();
                foreach (var item in queuedItems)
                {
                    item.Status = "Running";
                    
                    // Set current workspace path
                    WorkspacePath = item.Path;
                    _fileService.SetWorkspacePath(WorkspacePath);
                    Bootstrapper.TerminalService.SetWorkingDirectory(WorkspacePath);
                    LoadWorkspace(WorkspacePath);

                    // Trigger the run
                    string runMsg = "Start implementing the planned workspace tasks for module " + item.Name;
                    
                    // Add chat message
                    ChatHistory.Add(new ChatMessage { Content = $"🚀 [Queue Orchestrator] Starting autonomous run for module: {item.Name}", Role = "system" });

                    try
                    {
                        await _orchestrator.ProcessRequestAsync(runMsg, RoleBindings.ToList());
                        item.Status = "Completed";
                    }
                    catch (Exception ex)
                    {
                        item.Status = "Failed";
                        ChatHistory.Add(new ChatMessage { Content = $"❌ [Queue Orchestrator] Module {item.Name} failed: {ex.Message}", Role = "system" });
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Helper methods
        private string MapRoleToStageName(AgentRole role) => role switch
        {
            AgentRole.Planner => "التخطيط",
            AgentRole.PlanReviewer => "مراجعة الخطة",
            AgentRole.Executor => "التنفيذ",
            AgentRole.Reviewer => "المراجعة والتدقيق",
            AgentRole.SecurityReviewer => "الأمان",
            _ => role.ToString()
        };

        private string GetModuleNameForPending()
        {
            var activeInQueue = ModuleQueue.FirstOrDefault(q => q.Status == "Running");
            if (activeInQueue != null)
            {
                return activeInQueue.Name;
            }
            if (!string.IsNullOrEmpty(WorkspacePath))
            {
                return Path.GetFileName(WorkspacePath.TrimEnd('\\', '/'));
            }
            return "المشروع";
        }
    }

    public class FileItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string Icon => IsDirectory ? "\uE8B7" : "\uE7C3";
        public ObservableCollection<FileItem> Children { get; } = new();
        public FileItem(string path, bool isDir)
        {
            FullPath = path;
            IsDirectory = isDir;
            Name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(Name)) Name = path;
        }
    }
}
