using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalCursor.ViewModels
{
    /// <summary>
    /// A command/message queued for the agent to process.
    /// </summary>
    public partial class QueuedCommand : ObservableObject
    {
        [ObservableProperty]
        private string _content = "";

        [ObservableProperty]
        private byte[]? _imageData;

        [ObservableProperty]
        private string? _imageMimeType;

        [ObservableProperty]
        private string? _imageFileName;

        /// <summary>Preview for display (truncated text).</summary>
        public string Preview => string.IsNullOrEmpty(Content) 
            ? "(فارغ)" 
            : (Content.Length > 60 ? Content.Substring(0, 57) + "..." : Content);

        partial void OnContentChanged(string value) => OnPropertyChanged(nameof(Preview));
    }
}
