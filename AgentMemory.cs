using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalCursor.Services
{
    /// <summary>
    /// Hybrid memory system:
    /// - Short-term: Recent chat context
    /// - Long-term: SQLite database with searchable history
    /// </summary>
    public class AgentMemory
    {
        private readonly string _dbPath;
        private readonly int _maxShortTermSize;
        private List<MemoryEntry> _shortTermMemory = new();

        public AgentMemory(string workspacePath, int maxShortTermSize = 20)
        {
            var memoryDir = Path.Combine(workspacePath, ".agent_memory");
            if (!Directory.Exists(memoryDir))
                Directory.CreateDirectory(memoryDir);

            _dbPath = Path.Combine(memoryDir, "memory.db");
            _maxShortTermSize = maxShortTermSize;

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS memories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    type TEXT NOT NULL,
                    content TEXT NOT NULL,
                    file_path TEXT,
                    tags TEXT,
                    importance INTEGER DEFAULT 1
                );
                
                CREATE TABLE IF NOT EXISTS decisions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    question TEXT NOT NULL,
                    decision TEXT NOT NULL,
                    reasoning TEXT,
                    outcome TEXT
                );

                CREATE TABLE IF NOT EXISTS file_touches (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    action TEXT NOT NULL,
                    summary TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_memories_type ON memories(type);
                CREATE INDEX IF NOT EXISTS idx_memories_tags ON memories(tags);
                CREATE INDEX IF NOT EXISTS idx_file_touches_path ON file_touches(file_path);
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Adds an entry to short-term memory and persists to long-term.
        /// </summary>
        public void Remember(string type, string content, string filePath = null, string[] tags = null, int importance = 1)
        {
            var entry = new MemoryEntry
            {
                Timestamp = DateTime.Now,
                Type = type,
                Content = content,
                FilePath = filePath,
                Tags = tags ?? Array.Empty<string>(),
                Importance = importance
            };

            // Add to short-term
            _shortTermMemory.Add(entry);
            if (_shortTermMemory.Count > _maxShortTermSize)
            {
                _shortTermMemory.RemoveAt(0);
            }

            // Persist to long-term
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO memories (timestamp, type, content, file_path, tags, importance)
                VALUES ($timestamp, $type, $content, $filePath, $tags, $importance)
            ";
            cmd.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("$type", type);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$filePath", filePath ?? "");
            cmd.Parameters.AddWithValue("$tags", string.Join(",", entry.Tags));
            cmd.Parameters.AddWithValue("$importance", importance);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Records a decision made by the agent.
        /// </summary>
        public void RecordDecision(string question, string decision, string reasoning = null)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO decisions (timestamp, question, decision, reasoning)
                VALUES ($timestamp, $question, $decision, $reasoning)
            ";
            cmd.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$question", question);
            cmd.Parameters.AddWithValue("$decision", decision);
            cmd.Parameters.AddWithValue("$reasoning", reasoning ?? "");
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Records when a file is touched by the agent.
        /// </summary>
        public void RecordFileTouch(string filePath, string action, string summary = null)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO file_touches (timestamp, file_path, action, summary)
                VALUES ($timestamp, $filePath, $action, $summary)
            ";
            cmd.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$filePath", filePath);
            cmd.Parameters.AddWithValue("$action", action);
            cmd.Parameters.AddWithValue("$summary", summary ?? "");
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Checks if a file has been touched before.
        /// </summary>
        public bool HasTouchedFile(string filePath)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM file_touches WHERE file_path = $path";
            cmd.Parameters.AddWithValue("$path", filePath);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        /// <summary>
        /// Gets the history of a file.
        /// </summary>
        public List<(DateTime Time, string Action, string Summary)> GetFileHistory(string filePath)
        {
            var results = new List<(DateTime, string, string)>();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT timestamp, action, summary FROM file_touches WHERE file_path = $path ORDER BY timestamp DESC";
            cmd.Parameters.AddWithValue("$path", filePath);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add((
                    DateTime.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2)
                ));
            }

            return results;
        }

        /// <summary>
        /// Searches memories by keyword.
        /// </summary>
        public List<MemoryEntry> Search(string keyword, int limit = 10)
        {
            var results = new List<MemoryEntry>();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT timestamp, type, content, file_path, tags, importance 
                FROM memories 
                WHERE content LIKE $keyword OR tags LIKE $keyword
                ORDER BY timestamp DESC
                LIMIT $limit
            ";
            cmd.Parameters.AddWithValue("$keyword", $"%{keyword}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new MemoryEntry
                {
                    Timestamp = DateTime.Parse(reader.GetString(0)),
                    Type = reader.GetString(1),
                    Content = reader.GetString(2),
                    FilePath = reader.GetString(3),
                    Tags = reader.GetString(4).Split(',', StringSplitOptions.RemoveEmptyEntries),
                    Importance = reader.GetInt32(5)
                });
            }

            return results;
        }

        /// <summary>
        /// Gets recent decisions.
        /// </summary>
        public List<(string Question, string Decision, string Reasoning)> GetRecentDecisions(int count = 5)
        {
            var results = new List<(string, string, string)>();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT question, decision, reasoning FROM decisions ORDER BY timestamp DESC LIMIT $count";
            cmd.Parameters.AddWithValue("$count", count);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)
                ));
            }

            return results;
        }

        /// <summary>
        /// Gets short-term memory context for LLM.
        /// </summary>
        public string GetContextSummary()
        {
            if (_shortTermMemory.Count == 0)
                return "No recent memory.";

            var summary = new System.Text.StringBuilder();
            summary.AppendLine("=== RECENT CONTEXT ===");

            foreach (var entry in _shortTermMemory.TakeLast(5))
            {
                summary.AppendLine($"[{entry.Timestamp:HH:mm}] {entry.Type}: {Truncate(entry.Content, 100)}");
            }

            return summary.ToString();
        }

        private string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        public class MemoryEntry
        {
            public DateTime Timestamp { get; set; }
            public string Type { get; set; }
            public string Content { get; set; }
            public string FilePath { get; set; }
            public string[] Tags { get; set; }
            public int Importance { get; set; }
        }
    }
}
