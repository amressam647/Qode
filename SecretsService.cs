using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalCursor.Services
{
    /// <summary>
    /// Secure secrets management using Windows DPAPI.
    /// Never stores secrets in plain text.
    /// </summary>
    public class SecretsService
    {
        private readonly string _secretsPath;
        private Dictionary<string, string> _cache = new();

        public SecretsService(string workspacePath)
        {
            // Store secrets in user's AppData, not in workspace
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var secretsDir = Path.Combine(appDataPath, "LocalCursor", "secrets");
            
            if (!Directory.Exists(secretsDir))
                Directory.CreateDirectory(secretsDir);

            _secretsPath = Path.Combine(secretsDir, "vault.dat");
            LoadSecrets();
        }

        /// <summary>
        /// Stores a secret securely using DPAPI.
        /// </summary>
        public void SetSecret(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be empty");

            _cache[key] = value;
            SaveSecrets();
        }

        /// <summary>
        /// Retrieves a secret.
        /// </summary>
        public string GetSecret(string key)
        {
            return _cache.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Checks if a secret exists.
        /// </summary>
        public bool HasSecret(string key)
        {
            return _cache.ContainsKey(key);
        }

        /// <summary>
        /// Removes a secret.
        /// </summary>
        public void RemoveSecret(string key)
        {
            if (_cache.Remove(key))
            {
                SaveSecrets();
            }
        }

        /// <summary>
        /// Lists all secret keys (not values).
        /// </summary>
        public IEnumerable<string> ListKeys()
        {
            return _cache.Keys;
        }

        /// <summary>
        /// Gets a connection string with secrets replaced.
        /// Format: ${SECRET_NAME}
        /// </summary>
        public string ResolveSecrets(string template)
        {
            if (string.IsNullOrEmpty(template)) return template;

            var result = template;
            foreach (var key in _cache.Keys)
            {
                result = result.Replace($"${{{key}}}", _cache[key]);
            }
            return result;
        }

        private void SaveSecrets()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache);
                var plainBytes = Encoding.UTF8.GetBytes(json);

                // Encrypt using DPAPI (Windows only)
                var encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null, // Optional entropy
                    DataProtectionScope.CurrentUser
                );

                File.WriteAllBytes(_secretsPath, encryptedBytes);
            }
            catch (Exception ex)
            {
                // Log but don't throw - secrets might be in memory only
                Console.WriteLine($"Warning: Could not save secrets: {ex.Message}");
            }
        }

        private void LoadSecrets()
        {
            try
            {
                if (!File.Exists(_secretsPath))
                {
                    _cache = new Dictionary<string, string>();
                    return;
                }

                var encryptedBytes = File.ReadAllBytes(_secretsPath);

                // Decrypt using DPAPI
                var plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser
                );

                var json = Encoding.UTF8.GetString(plainBytes);
                _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                         ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                // If decryption fails, start fresh
                Console.WriteLine($"Warning: Could not load secrets: {ex.Message}");
                _cache = new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Common secret keys.
        /// </summary>
        public static class Keys
        {
            public const string OpenAiApiKey = "OPENAI_API_KEY";
            public const string DbPassword = "DB_PASSWORD";
            public const string GitHubToken = "GITHUB_TOKEN";
            public const string AzureKey = "AZURE_KEY";
        }
    }
}
