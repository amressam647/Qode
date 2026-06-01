
using System;
using System.IO;
using System.Linq;

namespace LocalCursor.Services
{
    public class FileService
    {
        private string _workspacePath;
        internal static bool KernelLock { get; set; } = false;

        private void EnforceKernelLock()
        {
            if (!KernelLock) throw new InvalidOperationException("🛡️ KERNEL SECURITY VIOLATION: Direct file access outside Sandbox is forbidden.");
        }

        public FileService(string workspacePath)
        {
            _workspacePath = workspacePath;
            if (!Directory.Exists(_workspacePath))
            {
                Directory.CreateDirectory(_workspacePath);
            }
        }

        public string GetWorkspacePath() => _workspacePath;

        public void SetWorkspacePath(string path)
        {
            if (Directory.Exists(path))
            {
                _workspacePath = path;
            }
        }

        public string ReadFile(string path)
        {
            EnforceKernelLock();
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, path));
                if (File.Exists(fullPath))
                    return File.ReadAllText(fullPath);
                return $"Error: File not found: {path}";
            }
            catch (Exception ex)
            {
                return $"Error reading file: {ex.Message}";
            }
        }

        public string WriteFile(string path, string content)
        {
            EnforceKernelLock();
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, path));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, content);
                return $"Successfully wrote {path}";
            }
            catch (Exception ex)
            {
                return $"Error writing file: {ex.Message}";
            }
        }

        public string ListDirectory(string relPath = "")
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, relPath ?? ""));
                if (!Directory.Exists(fullPath)) return "Error: Directory not found.";

                var files = Directory.GetFiles(fullPath).Select(Path.GetFileName);
                var dirs = Directory.GetDirectories(fullPath).Select(Path.GetFileName);

                return string.Join("\n", dirs.Select(d => $"[DIR] {d}").Concat(files));
            }
            catch (Exception ex)
            {
                return $"Error listing directory: {ex.Message}";
            }
        }

        public string CreateFolder(string path)
        {
            EnforceKernelLock();
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, path));
                Directory.CreateDirectory(fullPath);
                return $"Successfully created directory {path}";
            }
            catch (Exception ex)
            {
                return $"Error creating directory: {ex.Message}";
            }
        }

        public string DeleteFile(string path)
        {
            EnforceKernelLock();
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, path));
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return $"Successfully deleted file {path}";
                }
                return $"Error: File not found: {path}";
            }
            catch (Exception ex)
            {
                return $"Error deleting file: {ex.Message}";
            }
        }

        public string DeleteFolder(string path)
        {
            EnforceKernelLock();
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, path));
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    return $"Successfully deleted directory {path}";
                }
                return $"Error: Directory not found: {path}";
            }
            catch (Exception ex)
            {
                return $"Error deleting directory: {ex.Message}";
            }
        }
    }
}
