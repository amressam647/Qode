using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LocalCursor.Services
{
    /// <summary>
    /// Invariant Checker - Enforces rules that can NEVER be broken.
    /// Provides formal safety guarantees beyond heuristics.
    /// </summary>
    public class InvariantChecker
    {
        private readonly List<Invariant> _invariants;
        private readonly ImmutableAuditLog? _auditLog;

        public InvariantChecker(ImmutableAuditLog? auditLog = null)
        {
            _auditLog = auditLog;
            _invariants = new List<Invariant>
            {
                // Critical file protection
                new Invariant
                {
                    Name = "AUTH_PROTECTION",
                    Description = "Cannot modify authentication files without explicit flag",
                    Pattern = @"(auth|authentication|login|oauth|jwt|token)",
                    AppliesTo = InvariantScope.FilePath,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = "ALLOW_AUTH_MODIFY"
                },
                new Invariant
                {
                    Name = "LICENSE_PROTECTION",
                    Description = "Cannot modify licensing files",
                    Pattern = @"(license|licensing|subscription|payment)",
                    AppliesTo = InvariantScope.FilePath,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = "ALLOW_LICENSE_MODIFY"
                },
                new Invariant
                {
                    Name = "ENCRYPTION_PROTECTION",
                    Description = "Cannot modify encryption/crypto files",
                    Pattern = @"(encrypt|decrypt|crypto|cipher|hash)",
                    AppliesTo = InvariantScope.FilePath,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = "ALLOW_CRYPTO_MODIFY"
                },
                new Invariant
                {
                    Name = "No DROP statements",
                    Description = "DROP TABLE/DATABASE is forbidden",
                    Pattern = @"\bDROP\s+(TABLE|DATABASE|INDEX|SCHEMA)\b",
                    AppliesTo = InvariantScope.SqlContent,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = "ALLOW_DROP"
                },
                new Invariant
                {
                    Name = "No TRUNCATE",
                    Description = "TRUNCATE TABLE is forbidden",
                    Pattern = @"\bTRUNCATE\s+TABLE\b",
                    AppliesTo = InvariantScope.SqlContent,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = "ALLOW_TRUNCATE"
                },
                new Invariant
                {
                    Name = "No DELETE without WHERE",
                    Description = "DELETE must have WHERE clause",
                    Pattern = @"\bDELETE\s+FROM\s+\w+\s*(?!WHERE)",
                    AppliesTo = InvariantScope.SqlContent,
                    Severity = InvariantSeverity.High,
                    RequiresFlag = "ALLOW_DELETE_ALL"
                },
                new Invariant
                {
                    Name = "No system file access",
                    Description = "Cannot access Windows system directories",
                    Pattern = @"(C:\\Windows|C:\\Program Files|\\System32)",
                    AppliesTo = InvariantScope.FilePath,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = null // Never allowed
                },
                new Invariant
                {
                    Name = "No hardcoded secrets",
                    Description = "Cannot write hardcoded secrets in code",
                    Pattern = @"(password\s*=\s*[""'][^""']{4,}|apikey\s*=\s*[""'][^""']{10,}|secret\s*=\s*[""'][^""']{8,})",
                    AppliesTo = InvariantScope.FileContent,
                    Severity = InvariantSeverity.High,
                    RequiresFlag = "ALLOW_HARDCODED_SECRET"
                },
                new Invariant
                {
                    Name = "No format/diskpart commands",
                    Description = "Destructive disk commands forbidden",
                    Pattern = @"\b(format|diskpart|fdisk|mkfs)\b",
                    AppliesTo = InvariantScope.CommandContent,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = null // Never allowed
                },
                new Invariant
                {
                    Name = "No registry modifications",
                    Description = "Registry modifications forbidden",
                    Pattern = @"\b(reg\s+delete|regedit|Remove-ItemProperty.*Registry)\b",
                    AppliesTo = InvariantScope.CommandContent,
                    Severity = InvariantSeverity.Critical,
                    RequiresFlag = "ALLOW_REGISTRY"
                }
            };
        }

        private readonly HashSet<string> _activeFlags = new();

        /// <summary>
        /// Sets a flag to allow specific operations.
        /// </summary>
        public void SetFlag(string flag)
        {
            _activeFlags.Add(flag.ToUpper());
            _auditLog?.Log(new AuditEntry
            {
                Type = "FLAG_SET",
                User = "ADMIN",
                Action = "SET",
                Target = flag,
                Details = ""
            });
        }

        /// <summary>
        /// Clears a flag.
        /// </summary>
        public void ClearFlag(string flag)
        {
            _activeFlags.Remove(flag.ToUpper());
        }

        /// <summary>
        /// Clears all flags.
        /// </summary>
        public void ClearAllFlags()
        {
            _activeFlags.Clear();
        }

        /// <summary>
        /// Checks if an operation violates any invariants.
        /// </summary>
        public InvariantCheckResult Check(InvariantScope scope, string content)
        {
            var violations = new List<InvariantViolation>();

            foreach (var invariant in _invariants)
            {
                if (invariant.AppliesTo != scope) continue;

                if (Regex.IsMatch(content, invariant.Pattern, RegexOptions.IgnoreCase))
                {
                    // Check if flag allows this
                    if (invariant.RequiresFlag != null && _activeFlags.Contains(invariant.RequiresFlag))
                    {
                        continue; // Allowed by flag
                    }

                    var violation = new InvariantViolation
                    {
                        Invariant = invariant,
                        Content = Truncate(content, 100),
                        DetectedAt = DateTime.Now
                    };
                    violations.Add(violation);

                    // Log to audit
                    _auditLog?.Log(new AuditEntry
                    {
                        Type = "INVARIANT_VIOLATION",
                        User = "AGENT",
                        Action = invariant.Name,
                        Target = scope.ToString(),
                        Details = Truncate(content, 50)
                    });
                }
            }

            return new InvariantCheckResult
            {
                IsValid = violations.Count == 0,
                Violations = violations,
                CheckedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Gets a summary of active invariants for LLM context.
        /// </summary>
        public string GetInvariantContext()
        {
            return @"
[INVARIANTS - NEVER VIOLATE]
- No DROP/TRUNCATE without flag
- No DELETE without WHERE
- No auth/license/crypto file modification
- No Windows system paths
- No hardcoded secrets
- No format/diskpart commands
- No registry modifications
";
        }

        /// <summary>
        /// Adds a custom invariant.
        /// </summary>
        public void AddInvariant(Invariant invariant)
        {
            _invariants.Add(invariant);
        }

        private string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }

    public class Invariant
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty; // Regex pattern
        public InvariantScope AppliesTo { get; set; }
        public InvariantSeverity Severity { get; set; }
        public string? RequiresFlag { get; set; } // Flag that allows bypass, null = never allowed
    }

    public enum InvariantScope
    {
        FilePath,
        FileContent,
        SqlContent,
        CommandContent
    }

    public enum InvariantSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class InvariantViolation
    {
        public Invariant Invariant { get; set; } = new();
        public string Content { get; set; } = string.Empty;
        public DateTime DetectedAt { get; set; }

        public override string ToString()
        {
            return $"[{Invariant.Severity}] {Invariant.Name}: {Invariant.Description}";
        }
    }

    public class InvariantCheckResult
    {
        public bool IsValid { get; set; }
        public List<InvariantViolation> Violations { get; set; } = new();
        public DateTime CheckedAt { get; set; }

        public string GetViolationSummary()
        {
            if (IsValid) return "No violations";
            return string.Join("\n", Violations.ConvertAll(v => v.ToString()));
        }
    }
}
