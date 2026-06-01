using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalCursor.ViewModels
{
    public partial class EditorTab : ObservableObject
    {
        [ObservableProperty]
        private string _filePath = "";

        [ObservableProperty]
        private string _fileName = "Untitled";

        [ObservableProperty]
        private string _content = "";

        [ObservableProperty]
        private bool _isModified = false;

        [ObservableProperty]
        private bool _isActive = false;

        public string DisplayName => IsModified ? $"{FileName} •" : FileName;

        partial void OnFileNameChanged(string value)
        {
            OnPropertyChanged(nameof(DisplayName));
        }

        partial void OnIsModifiedChanged(bool value)
        {
            OnPropertyChanged(nameof(DisplayName));
        }
        public EditorTab() { }

        public EditorTab(string fileName, string filePath, string content)
        {
            FileName = fileName;
            FilePath = filePath;
            Content = content;
        }
    }
}
