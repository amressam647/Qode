using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalCursor.Services
{
    /// <summary>
    /// Capability-based security - Agent starts with minimal permissions.
    /// Must request additional capabilities.
    /// </summary>
    public class CapabilityManager
    {
        private readonly HashSet<string> _grantedCapabilities;
        private readonly List<CapabilityRequest> _pendingRequests;
        private readonly ImmutableAuditLog _auditLog;

        public event Action<CapabilityRequest> OnCapabilityRequested;

        // Predefined capability sets
        public static readonly string[] ReadOnlyCapabilities = { "READ", "LIST", "DB_QUERY", "GIT_STATUS", "GIT_DIFF", "GIT_LOG" };
        public static readonly string[] DeveloperCapabilities = { "READ", "LIST", "WRITE", "DB_QUERY", "GIT_STATUS", "GIT_DIFF", "GIT_ADD", "GIT_COMMIT" };
        public static readonly string[] FullCapabilities = { "READ", "LIST", "WRITE", "CMD", "PS", "DB_QUERY", "DB_EXECUTE", "GIT_STATUS", "GIT_DIFF", "GIT_ADD", "GIT_COMMIT", "GIT_PUSH", "GIT_PULL" };

        public CapabilityManager(ImmutableAuditLog auditLog = null)
        {
            _grantedCapabilities = new HashSet<string>(ReadOnlyCapabilities);
            _pendingRequests = new List<CapabilityRequest>();
            _auditLog = auditLog;
        }

        /// <summary>
        /// Checks if a capability is granted.
        /// </summary>
        public bool HasCapability(string capability)
        {
            return _grantedCapabilities.Contains(capability.ToUpper());
        }

        /// <summary>
        /// Requests a capability with reason.
        /// </summary>
        public CapabilityRequest RequestCapability(string capability, string reason)
        {
            var request = new CapabilityRequest
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Capability = capability.ToUpper(),
                Reason = reason,
                RequestedAt = DateTime.Now,
                Status = CapabilityStatus.Pending
            };

            _pendingRequests.Add(request);
            
            _auditLog?.Log(new AuditEntry
            {
                Type = "CAPABILITY_REQUEST",
                User = "AGENT",
                Action = "REQUEST",
                Target = capability,
                Details = reason
            });

            OnCapabilityRequested?.Invoke(request);
            return request;
        }

        /// <summary>
        /// Grants a capability (for UI approval).
        /// </summary>
        public void Grant(string requestId)
        {
            var request = _pendingRequests.FirstOrDefault(r => r.Id == requestId);
            if (request != null)
            {
                request.Status = CapabilityStatus.Granted;
                request.ResolvedAt = DateTime.Now;
                _grantedCapabilities.Add(request.Capability);

                _auditLog?.Log(new AuditEntry
                {
                    Type = "CAPABILITY_GRANT",
                    User = "ADMIN",
                    Action = "GRANT",
                    Target = request.Capability,
                    Details = request.Reason
                });
            }
        }

        /// <summary>
        /// Denies a capability request.
        /// </summary>
        public void Deny(string requestId, string reason = null)
        {
            var request = _pendingRequests.FirstOrDefault(r => r.Id == requestId);
            if (request != null)
            {
                request.Status = CapabilityStatus.Denied;
                request.ResolvedAt = DateTime.Now;
                request.DenialReason = reason;

                _auditLog?.Log(new AuditEntry
                {
                    Type = "CAPABILITY_DENY",
                    User = "ADMIN",
                    Action = "DENY",
                    Target = request.Capability,
                    Details = reason ?? "No reason provided"
                });
            }
        }

        /// <summary>
        /// Revokes a granted capability.
        /// </summary>
        public void Revoke(string capability)
        {
            if (_grantedCapabilities.Remove(capability.ToUpper()))
            {
                _auditLog?.Log(new AuditEntry
                {
                    Type = "CAPABILITY_REVOKE",
                    User = "ADMIN",
                    Action = "REVOKE",
                    Target = capability,
                    Details = ""
                });
            }
        }

        /// <summary>
        /// Grants a predefined capability set.
        /// </summary>
        public void GrantCapabilitySet(string setName)
        {
            var capabilities = setName.ToLower() switch
            {
                "readonly" => ReadOnlyCapabilities,
                "developer" => DeveloperCapabilities,
                "full" => FullCapabilities,
                _ => ReadOnlyCapabilities
            };

            foreach (var cap in capabilities)
            {
                _grantedCapabilities.Add(cap);
            }

            _auditLog?.Log(new AuditEntry
            {
                Type = "CAPABILITY_SET",
                User = "ADMIN",
                Action = "GRANT_SET",
                Target = setName,
                Details = string.Join(", ", capabilities)
            });
        }

        /// <summary>
        /// Resets to minimal capabilities.
        /// </summary>
        public void Reset()
        {
            _grantedCapabilities.Clear();
            foreach (var cap in ReadOnlyCapabilities)
            {
                _grantedCapabilities.Add(cap);
            }
            _pendingRequests.Clear();

            _auditLog?.Log(new AuditEntry
            {
                Type = "CAPABILITY_RESET",
                User = "SYSTEM",
                Action = "RESET",
                Target = "",
                Details = "Capabilities reset to ReadOnly"
            });
        }

        /// <summary>
        /// Gets current capabilities for LLM context.
        /// </summary>
        public string GetCapabilityContext()
        {
            return $"[CAPABILITIES] Granted: {string.Join(", ", _grantedCapabilities)}";
        }

        /// <summary>
        /// Gets pending requests.
        /// </summary>
        public IEnumerable<CapabilityRequest> GetPendingRequests()
        {
            return _pendingRequests.Where(r => r.Status == CapabilityStatus.Pending);
        }

        /// <summary>
        /// Validates a tool call against capabilities.
        /// </summary>
        public (bool Allowed, string Reason) ValidateToolCall(string toolType)
        {
            var cap = toolType.ToUpper();

            if (HasCapability(cap))
                return (true, "Capability granted");

            // Check for category capabilities (e.g., DB_ tools need DB capability)
            if (cap.StartsWith("DB_") && HasCapability("DB"))
                return (true, "Category capability granted");
            if (cap.StartsWith("GIT_") && HasCapability("GIT"))
                return (true, "Category capability granted");

            return (false, $"Missing capability: {cap}. Use RequestCapability() to request access.");
        }
    }

    public class CapabilityRequest
    {
        public string Id { get; set; }
        public string Capability { get; set; }
        public string Reason { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public CapabilityStatus Status { get; set; }
        public string DenialReason { get; set; }

        public override string ToString()
        {
            return $"[{Id}] {Capability}: {Reason} ({Status})";
        }
    }

    public enum CapabilityStatus
    {
        Pending,
        Granted,
        Denied
    }
}
