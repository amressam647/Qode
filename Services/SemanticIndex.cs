using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LocalCursor.Services
{
    /// <summary>
    /// Represents a single semantic search match result.
    /// </summary>
    /// <param name="FilePath">Relative path to the matched file.</param>
    /// <param name="Score">Cosine similarity score between query and document.</param>
    /// <param name="Snippet">Relevant text snippet from the file (max 500 chars).</param>
    /// <param name="LineStart">Starting line number of the snippet.</param>
    /// <param name="LineEnd">Ending line number of the snippet.</param>
    public record SemanticMatch(string FilePath, double Score, string Snippet, int LineStart, int LineEnd);

    /// <summary>
    /// A lightweight TF-IDF based semantic index for workspace files.
    /// Supports indexing, incremental updates, querying via cosine similarity,
    /// and building compact context strings for LLM prompt injection.
    /// Zero external dependencies — uses only System.Text.Json for persistence.
    /// </summary>
    public class SemanticIndex
    {
        private const int MaxSnippetLength = 500;
        private const string IndexDirectoryName = ".qode_index";
        private const string IndexFileName = "semantic.json";

        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".xaml", ".json", ".md", ".txt", ".xml", ".csproj"
        };

        private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "bin", "obj", "node_modules", ".agent_"
        };

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "shall", "can", "need", "dare", "ought",
            "used", "to", "of", "in", "for", "on", "with", "at", "by", "from",
            "as", "into", "through", "during", "before", "after", "above", "below",
            "between", "out", "off", "over", "under", "again", "further", "then",
            "once", "here", "there", "when", "where", "why", "how", "all", "both",
            "each", "few", "more", "most", "other", "some", "such", "no", "nor",
            "not", "only", "own", "same", "so", "than", "too", "very", "just",
            "because", "but", "and", "or", "if", "while", "about", "up", "it",
            "its", "this", "that", "these", "those", "i", "me", "my", "we", "our",
            "you", "your", "he", "him", "his", "she", "her", "they", "them", "their",
            "what", "which", "who", "whom", "get", "set", "new", "var"
        };

        private static readonly Regex TokenSplitter = new(@"[\s\p{P}]+", RegexOptions.Compiled);

        private readonly string _workspacePath;
        private readonly string _indexFilePath;

        /// <summary>
        /// In-memory document store: relative file path → document data.
        /// </summary>
        private Dictionary<string, DocumentEntry> _documents = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Global IDF values for all terms across the corpus.
        /// </summary>
        private Dictionary<string, double> _idfCache = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of <see cref="SemanticIndex"/>.
        /// Attempts to load a previously persisted index from disk.
        /// </summary>
        /// <param name="workspacePath">Absolute path to the workspace root directory.</param>
        public SemanticIndex(string workspacePath)
        {
            _workspacePath = Path.GetFullPath(workspacePath);
            _indexFilePath = Path.Combine(_workspacePath, IndexDirectoryName, IndexFileName);

            TryLoadIndex();
        }

        /// <summary>
        /// Scans the workspace directory for supported files, tokenizes their content,
        /// computes TF-IDF vectors, and stores everything in memory.
        /// Previously indexed data is replaced.
        /// </summary>
        /// <param name="workspacePath">Absolute path to the workspace root to index.</param>
        public void IndexWorkspace(string workspacePath)
        {
            var rootPath = Path.GetFullPath(workspacePath);
            _documents.Clear();
            _idfCache.Clear();

            var files = EnumerateSupportedFiles(rootPath);

            foreach (var filePath in files)
            {
                try
                {
                    var content = File.ReadAllText(filePath);
                    var relativePath = Path.GetRelativePath(rootPath, filePath);
                    var tokens = Tokenize(content);
                    var tf = ComputeTf(tokens);

                    _documents[relativePath] = new DocumentEntry
                    {
                        Content = content,
                        Tokens = tokens,
                        TermFrequency = tf
                    };
                }
                catch (Exception)
                {
                    // Skip files that cannot be read (locked, permissions, encoding issues)
                }
            }

            RecomputeIdf();
            PersistIndex();
        }

        /// <summary>
        /// Incrementally updates the index for a single file.
        /// If <paramref name="content"/> is null or empty the file is removed from the index.
        /// </summary>
        /// <param name="relativePath">Relative path of the file within the workspace.</param>
        /// <param name="content">New content of the file.</param>
        public void UpdateFile(string relativePath, string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                _documents.Remove(relativePath);
            }
            else
            {
                var tokens = Tokenize(content);
                var tf = ComputeTf(tokens);

                _documents[relativePath] = new DocumentEntry
                {
                    Content = content,
                    Tokens = tokens,
                    TermFrequency = tf
                };
            }

            RecomputeIdf();
            PersistIndex();
        }

        /// <summary>
        /// Finds the most relevant files/snippets for the given query using cosine similarity
        /// between query token TF-IDF vector and each document's TF-IDF vector.
        /// </summary>
        /// <param name="query">Natural language query string.</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <returns>A list of <see cref="SemanticMatch"/> results ordered by descending similarity score.</returns>
        public List<SemanticMatch> Query(string query, int topK = 10)
        {
            if (_documents.Count == 0)
                return new List<SemanticMatch>();

            var queryTokens = Tokenize(query);
            if (queryTokens.Count == 0)
                return new List<SemanticMatch>();

            var queryTf = ComputeTf(queryTokens);
            var queryTfIdf = ComputeTfIdfVector(queryTf);

            var results = new List<SemanticMatch>();

            foreach (var (filePath, doc) in _documents)
            {
                var docTfIdf = ComputeTfIdfVector(doc.TermFrequency);
                var score = CosineSimilarity(queryTfIdf, docTfIdf);

                if (score <= 0)
                    continue;

                var (snippet, lineStart, lineEnd) = ExtractBestSnippet(doc.Content, queryTokens);

                results.Add(new SemanticMatch(filePath, score, snippet, lineStart, lineEnd));
            }

            return results
                .OrderByDescending(m => m.Score)
                .Take(topK)
                .ToList();
        }

        /// <summary>
        /// Builds a compact string containing relevant file snippets suitable for injection
        /// into LLM prompts. Token budget is estimated as text.Length / 4.
        /// </summary>
        /// <param name="taskDescription">Description of the task to find relevant context for.</param>
        /// <param name="maxTokens">Maximum estimated token budget (default 4000).</param>
        /// <returns>A formatted string of relevant context snippets.</returns>
        public string BuildRelevantContext(string taskDescription, int maxTokens = 4000)
        {
            var matches = Query(taskDescription, topK: 20);

            if (matches.Count == 0)
                return string.Empty;

            var maxChars = maxTokens * 4;
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("=== Relevant Context ===");

            foreach (var match in matches)
            {
                var entry = $"\n--- {match.FilePath} (score: {match.Score:F3}, lines {match.LineStart}-{match.LineEnd}) ---\n{match.Snippet}\n";

                if (builder.Length + entry.Length > maxChars)
                    break;

                builder.Append(entry);
            }

            return builder.ToString();
        }

        #region Private Helpers

        /// <summary>
        /// Tokenizes text by splitting on whitespace and punctuation, lowercasing,
        /// and filtering out stop words and very short tokens.
        /// </summary>
        private static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return TokenSplitter.Split(text)
                .Select(t => t.ToLowerInvariant())
                .Where(t => t.Length > 1 && !StopWords.Contains(t))
                .ToList();
        }

        /// <summary>
        /// Computes term frequency (TF) for a list of tokens.
        /// TF = occurrences of term / total number of terms.
        /// </summary>
        private static Dictionary<string, double> ComputeTf(List<string> tokens)
        {
            if (tokens.Count == 0)
                return new Dictionary<string, double>();

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in tokens)
            {
                counts.TryGetValue(token, out var count);
                counts[token] = count + 1;
            }

            var total = (double)tokens.Count;
            return counts.ToDictionary(kv => kv.Key, kv => kv.Value / total, StringComparer.Ordinal);
        }

        /// <summary>
        /// Recomputes IDF values for all terms across the entire document corpus.
        /// IDF = log(total documents / documents containing term).
        /// </summary>
        private void RecomputeIdf()
        {
            _idfCache.Clear();
            var totalDocs = _documents.Count;

            if (totalDocs == 0)
                return;

            var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var doc in _documents.Values)
            {
                foreach (var term in doc.TermFrequency.Keys)
                {
                    documentFrequency.TryGetValue(term, out var count);
                    documentFrequency[term] = count + 1;
                }
            }

            foreach (var (term, df) in documentFrequency)
            {
                _idfCache[term] = Math.Log((double)totalDocs / df);
            }
        }

        /// <summary>
        /// Computes a TF-IDF vector from a term frequency dictionary using the global IDF cache.
        /// </summary>
        private Dictionary<string, double> ComputeTfIdfVector(Dictionary<string, double> tf)
        {
            var vector = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var (term, tfValue) in tf)
            {
                if (_idfCache.TryGetValue(term, out var idf))
                {
                    vector[term] = tfValue * idf;
                }
                else
                {
                    // Term not seen in corpus — use max IDF approximation
                    vector[term] = tfValue * Math.Log(_documents.Count + 1);
                }
            }

            return vector;
        }

        /// <summary>
        /// Computes the cosine similarity between two sparse TF-IDF vectors.
        /// </summary>
        private static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
        {
            double dotProduct = 0;
            double magnitudeA = 0;
            double magnitudeB = 0;

            foreach (var (term, value) in a)
            {
                magnitudeA += value * value;
                if (b.TryGetValue(term, out var bValue))
                {
                    dotProduct += value * bValue;
                }
            }

            foreach (var value in b.Values)
            {
                magnitudeB += value * value;
            }

            var denominator = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);

            if (denominator == 0)
                return 0;

            return dotProduct / denominator;
        }

        /// <summary>
        /// Extracts the best matching snippet from document content based on query tokens.
        /// Uses a sliding window to find the region with the highest density of query terms.
        /// </summary>
        private static (string Snippet, int LineStart, int LineEnd) ExtractBestSnippet(
            string content, List<string> queryTokens)
        {
            var lines = content.Split('\n');
            if (lines.Length == 0)
                return (string.Empty, 0, 0);

            var querySet = new HashSet<string>(queryTokens, StringComparer.OrdinalIgnoreCase);

            // Score each line by how many query tokens it contains
            var lineScores = new double[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var lineTokens = Tokenize(lines[i]);
                lineScores[i] = lineTokens.Count(t => querySet.Contains(t));
            }

            // Find the best window of ~10 lines
            int windowSize = Math.Min(10, lines.Length);
            int bestStart = 0;
            double bestScore = 0;

            double currentScore = 0;
            for (int i = 0; i < windowSize && i < lines.Length; i++)
                currentScore += lineScores[i];

            bestScore = currentScore;

            for (int i = 1; i + windowSize <= lines.Length; i++)
            {
                currentScore -= lineScores[i - 1];
                currentScore += lineScores[i + windowSize - 1];
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestStart = i;
                }
            }

            int bestEnd = Math.Min(bestStart + windowSize - 1, lines.Length - 1);

            var snippet = string.Join('\n', lines[bestStart..(bestEnd + 1)]);
            if (snippet.Length > MaxSnippetLength)
            {
                snippet = snippet[..MaxSnippetLength];
            }

            // Lines are 1-indexed for the caller
            return (snippet, bestStart + 1, bestEnd + 1);
        }

        /// <summary>
        /// Enumerates all supported files under the given root, skipping excluded directories.
        /// </summary>
        private static IEnumerable<string> EnumerateSupportedFiles(string rootPath)
        {
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                // Enumerate child directories
                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var subDir in subDirs)
                {
                    var dirName = Path.GetFileName(subDir);
                    if (!ShouldSkipDirectory(dirName))
                    {
                        stack.Push(subDir);
                    }
                }

                // Enumerate files
                string[] files;
                try
                {
                    files = Directory.GetFiles(dir);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file);
                    if (SupportedExtensions.Contains(ext))
                    {
                        yield return file;
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether a directory should be skipped during indexing.
        /// Matches exact names and prefix patterns (e.g., ".agent_").
        /// </summary>
        private static bool ShouldSkipDirectory(string dirName)
        {
            if (SkippedDirectories.Contains(dirName))
                return true;

            // Handle prefix-based patterns like ".agent_"
            if (dirName.StartsWith(".agent_", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Persists the current index to disk as JSON.
        /// </summary>
        private void PersistIndex()
        {
            try
            {
                var indexDir = Path.GetDirectoryName(_indexFilePath)!;
                Directory.CreateDirectory(indexDir);

                var data = new PersistedIndex
                {
                    Documents = _documents.ToDictionary(
                        kv => kv.Key,
                        kv => new PersistedDocument
                        {
                            TermFrequency = kv.Value.TermFrequency
                        },
                        StringComparer.OrdinalIgnoreCase),
                    Idf = _idfCache
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(_indexFilePath, json);
            }
            catch (Exception)
            {
                // Persistence failure is non-critical — index lives in memory
            }
        }

        /// <summary>
        /// Attempts to load a previously persisted index from disk.
        /// On success, rehydrates content from the actual files.
        /// </summary>
        private void TryLoadIndex()
        {
            try
            {
                if (!File.Exists(_indexFilePath))
                    return;

                var json = File.ReadAllText(_indexFilePath);
                var data = JsonSerializer.Deserialize<PersistedIndex>(json);

                if (data?.Documents is null)
                    return;

                _documents.Clear();
                _idfCache = data.Idf ?? new Dictionary<string, double>(StringComparer.Ordinal);

                foreach (var (relativePath, persisted) in data.Documents)
                {
                    var fullPath = Path.Combine(_workspacePath, relativePath);
                    string content;

                    try
                    {
                        content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
                    }
                    catch
                    {
                        content = string.Empty;
                    }

                    var tokens = Tokenize(content);

                    _documents[relativePath] = new DocumentEntry
                    {
                        Content = content,
                        Tokens = tokens,
                        TermFrequency = persisted.TermFrequency
                            ?? new Dictionary<string, double>(StringComparer.Ordinal)
                    };
                }
            }
            catch (Exception)
            {
                // If loading fails, start with an empty index
                _documents.Clear();
                _idfCache.Clear();
            }
        }

        #endregion

        #region Internal Models

        /// <summary>
        /// In-memory representation of an indexed document.
        /// </summary>
        private class DocumentEntry
        {
            /// <summary>Raw file content.</summary>
            public string Content { get; init; } = string.Empty;

            /// <summary>Tokenized terms from the content.</summary>
            public List<string> Tokens { get; init; } = new();

            /// <summary>Term frequency dictionary: term → TF value.</summary>
            public Dictionary<string, double> TermFrequency { get; init; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Serializable representation of the full index for disk persistence.
        /// </summary>
        private class PersistedIndex
        {
            public Dictionary<string, PersistedDocument>? Documents { get; set; }
            public Dictionary<string, double>? Idf { get; set; }
        }

        /// <summary>
        /// Serializable representation of a single document's index data.
        /// Content is not persisted — it is rehydrated from disk on load.
        /// </summary>
        private class PersistedDocument
        {
            public Dictionary<string, double>? TermFrequency { get; set; }
        }

        #endregion
    }
}
