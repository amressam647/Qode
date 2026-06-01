using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LocalCursor.Services.Core;

namespace LocalCursor.Services
{
    /// <summary>
    /// Handles model non-determinism for reproducibility.
    /// Provides output hashing, divergence detection via Jaccard similarity,
    /// and role-based temperature recommendations.
    /// </summary>
    public class NonDeterminismHandler
    {
        private static readonly Regex WordTokenizer = new(@"\b\w+\b", RegexOptions.Compiled);

        /// <summary>
        /// Computes a SHA-256 hash of the given output string for reproducibility tracking.
        /// </summary>
        /// <param name="output">The model output to hash.</param>
        /// <returns>A lowercase hexadecimal SHA-256 hash string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="output"/> is null.</exception>
        public string HashOutput(string output)
        {
            ArgumentNullException.ThrowIfNull(output);

            try
            {
                var bytes = Encoding.UTF8.GetBytes(output);
                var hashBytes = SHA256.HashData(bytes);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (Exception ex) when (ex is not ArgumentNullException)
            {
                // Fallback: return a deterministic placeholder indicating failure
                return $"hash-error-{output.Length}";
            }
        }

        /// <summary>
        /// Determines whether two model outputs have diverged significantly.
        /// Uses Jaccard similarity on word-level tokens. Outputs are considered
        /// significantly divergent when their Jaccard similarity falls below 0.6.
        /// </summary>
        /// <param name="output1">The first model output.</param>
        /// <param name="output2">The second model output.</param>
        /// <returns>
        /// <c>true</c> if Jaccard similarity is below 0.6 (significant divergence);
        /// <c>false</c> otherwise.
        /// </returns>
        public bool HasSignificantDivergence(string output1, string output2)
        {
            try
            {
                if (string.IsNullOrEmpty(output1) && string.IsNullOrEmpty(output2))
                    return false;

                if (string.IsNullOrEmpty(output1) || string.IsNullOrEmpty(output2))
                    return true;

                var tokens1 = TokenizeToSet(output1);
                var tokens2 = TokenizeToSet(output2);

                if (tokens1.Count == 0 && tokens2.Count == 0)
                    return false;

                if (tokens1.Count == 0 || tokens2.Count == 0)
                    return true;

                var intersectionCount = tokens1.Count(t => tokens2.Contains(t));
                var unionCount = tokens1.Count + tokens2.Count - intersectionCount;

                if (unionCount == 0)
                    return false;

                var jaccardSimilarity = (double)intersectionCount / unionCount;
                return jaccardSimilarity < 0.6;
            }
            catch (Exception)
            {
                // On error, assume divergence to be safe
                return true;
            }
        }

        /// <summary>
        /// Returns the recommended temperature for a given <see cref="AgentRole"/>.
        /// Lower temperatures yield more deterministic outputs; higher temperatures
        /// allow more creative variation.
        /// </summary>
        /// <param name="role">The agent role to get the temperature for.</param>
        /// <returns>
        /// The temperature value:
        /// <list type="bullet">
        ///   <item><description>Planner: 0.7</description></item>
        ///   <item><description>PlanReviewer: 0.3</description></item>
        ///   <item><description>Executor: 0.1</description></item>
        ///   <item><description>Reviewer: 0.3</description></item>
        ///   <item><description>SecurityReviewer: 0.1</description></item>
        ///   <item><description>Default: 0.5</description></item>
        /// </list>
        /// </returns>
        public static double GetTemperatureForRole(AgentRole role)
        {
            return role switch
            {
                AgentRole.Planner => 0.7,
                AgentRole.PlanReviewer => 0.3,
                AgentRole.Executor => 0.1,
                AgentRole.Reviewer => 0.3,
                AgentRole.SecurityReviewer => 0.1,
                _ => 0.5
            };
        }

        /// <summary>
        /// Tokenizes a string into a set of unique lowercase word tokens.
        /// </summary>
        /// <param name="text">The text to tokenize.</param>
        /// <returns>A set of unique lowercase word tokens.</returns>
        private static HashSet<string> TokenizeToSet(string text)
        {
            var matches = WordTokenizer.Matches(text);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in matches)
            {
                set.Add(match.Value.ToLowerInvariant());
            }

            return set;
        }
    }
}
