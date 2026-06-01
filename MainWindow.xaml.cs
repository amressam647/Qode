using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalCursor.ViewModels;

namespace LocalCursor
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            
            // 1. Initialize Application Kernel
            Bootstrapper.Init();

            // 2. Inject Services into ViewModel (UI Bridge)
            _viewModel = new MainViewModel(
                Bootstrapper.Orchestrator,
                Bootstrapper.FileService,
                Bootstrapper.SecretsService,
                Bootstrapper.Registry
            );
            
            DataContext = _viewModel;

            // Wire up property changed for AvalonEdit sync
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Scroll chat to bottom when messages change (load or new message)
            _viewModel.ChatHistory.CollectionChanged += (s, e) => ScrollChatToEnd();

            // Also scroll when chat area is loaded (handles initial load timing)
            ChatScrollViewer.Loaded += (s, e) => ScrollChatToEnd();
        }

        private void ScrollChatToEnd()
        {
            // Defer until after layout updates (items measured/arranged)
            Dispatcher.BeginInvoke(() =>
            {
                ChatScrollViewer.UpdateLayout();
                ChatScrollViewer.ScrollToEnd();
            }, DispatcherPriority.ApplicationIdle);
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy) || e.PropertyName == nameof(MainViewModel.AgentStatus))
            {
                ScrollChatToEnd(); // Show thinking indicator at bottom
                return;
            }
            if (e.PropertyName == nameof(MainViewModel.FileContent))
            {
                if (CodeEditor.Text != _viewModel.FileContent)
                {
                    CodeEditor.Text = _viewModel.FileContent;
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentFileName))
            {
                UpdateSyntaxHighlighting(_viewModel.CurrentFileName);
            }
        }

        private void UpdateSyntaxHighlighting(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;

            var ext = System.IO.Path.GetExtension(fileName).ToLower();
            var highlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(ext);
            
            if (highlighting != null)
            {
                CodeEditor.SyntaxHighlighting = highlighting;
            }
            else
            {
                // Default fallback based on common types if not found by extension directly
                switch (ext)
                {
                    case ".cs":
                    case ".xaml":
                    case ".xml":
                    case ".html":
                    case ".js":
                    case ".json":
                    case ".css":
                    case ".py":
                    case ".sql":
                    case ".php":
                    case ".java":
                    case ".cpp":
                    case ".c":
                    case ".h":
                        CodeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(ext);
                        break;
                    default:
                        CodeEditor.SyntaxHighlighting = null;
                        break;
                }
            }
        }

        private void CodeEditor_TextChanged(object sender, System.EventArgs e)
        {
            if (_viewModel != null && _viewModel.FileContent != CodeEditor.Text)
            {
                _viewModel.FileContent = CodeEditor.Text;
            }
        }



        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tagContent)
            {
                try
                {
                    System.Windows.Clipboard.SetText(tagContent);
                    var successBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SuccessColor"];
                    var secondaryBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondary"];
                    btn.Content = CreateIconTextBlock("\uE73E", 14, successBrush); // Checkmark
                    
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = System.TimeSpan.FromSeconds(1);
                    timer.Tick += (s, args) =>
                    {
                        btn.Content = CreateIconTextBlock("\uE8C8", 14, secondaryBrush); // Copy
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch { }
            }
        }

        private static TextBlock CreateIconTextBlock(string glyph, double fontSize, System.Windows.Media.Brush foreground)
        {
            var font = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["IconFont"];
            return new TextBlock { Text = glyph, FontFamily = font, FontSize = fontSize, Foreground = foreground };
        }



        private void TerminalOutput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _viewModel.SelectedFile = e.NewValue as FileItem;
        }

        private void Tab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag != null)
            {
                _viewModel.ActiveTab = element.Tag as EditorTab;
            }
        }

        private void ChatInput_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var ext = Path.GetExtension(files[0]).ToLower();
                    if (new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" }.Contains(ext))
                    {
                        e.Effects = System.Windows.DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
            }
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void ChatInput_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                return;
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;
            var path = files[0];
            if (File.Exists(path))
            {
                _viewModel.TrySetAttachedImage(path);
                e.Handled = true;
            }
        }
    }
}