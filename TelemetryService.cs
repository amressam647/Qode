using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LocalCursor.Services
{
    /// <summary>
    /// Structured telemetry for observability and session replay.
    /// </summary>
    public class TelemetryService
    {
        private readonly string _sessionPath;
        private readonly string _sessionId;
        private readonly List<TelemetryEvent> _events = new();
        private readonly Stopwatch _sessionTimer;
        private readonly object _lock = new();

        public TelemetryService(string workspacePath)
        {
            _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var telemetryDir = Path.Combine(workspacePath, ".agent_telemetry");
            if (!Directory.Exists(telemetryDir))
                Directory.CreateDirectory(telemetryDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            _sessionPath = Path.Combine(telemetryDir, $"session_{timestamp}_{_sessionId}.jsonl");
            _sessionTimer = Stopwatch.StartNew();

            // Record session start
            Record(new TelemetryEvent
            {
                EventType = "SESSION_START",
                AgentState = "Initializing",
                Data = new { SessionId = _sessionId }
            });
        }

        /// <summary>
        /// Records a telemetry event.
        /// </summary>
        public void Record(TelemetryEvent evt)
        {
            evt.Timestamp = DateTime.Now;
            evt.SessionId = _sessionId;
            evt.ElapsedMs = _sessionTimer.ElapsedMilliseconds;

            lock (_lock)
            {
                _events.Add(evt);

                // Append to session file (JSONL format for streaming)
                try
                {
                    var json = JsonSerializer.Serialize(evt);
                    File.AppendAllText(_sessionPath, json + "\n");
                }
                catch { }
            }
        }

        /// <summary>
        /// Records a tool execution with timing.
        /// </summary>
        public IDisposable TrackToolExecution(string toolType, string target)
        {
            return new ToolExecutionTracker(this, toolType, target);
        }

        /// <summary>
        /// Records agent state transition.
        /// </summary>
        public void RecordStateChange(string fromState, string toState, string? reason = null)
        {
            Record(new TelemetryEvent
            {
                EventType = "STATE_CHANGE",
                AgentState = toState,
                Data = new { From = fromState, To = toState, Reason = reason }
            });
        }

        /// <summary>
        /// Records LLM interaction.
        /// </summary>
        public void RecordLlmCall(int inputTokens, int outputTokens, long durationMs, string model)
        {
            Record(new TelemetryEvent
            {
                EventType = "LLM_CALL",
                AgentState = "Processing",
                DurationMs = durationMs,
                Data = new { InputTokens = inputTokens, OutputTokens = outputTokens, Model = model }
            });
        }

        /// <summary>
        /// Gets session summary.
        /// </summary>
        public SessionSummary GetSummary()
        {
            var summary = new SessionSummary
            {
                SessionId = _sessionId,
                TotalEvents = _events.Count,
                DurationMs = _sessionTimer.ElapsedMilliseconds,
                ToolCalls = new Dictionary<string, int>(),
                Errors = 0
            };

            foreach (var evt in _events)
            {
                if (evt.EventType == "TOOL_EXECUTION")
                {
                    var tool = evt.Tool ?? "unknown";
                    summary.ToolCalls.TryGetValue(tool, out var count);
                    summary.ToolCalls[tool] = count + 1;
                }
                if (evt.EventType == "ERROR")
                {
                    summary.Errors++;
                }
            }

            return summary;
        }

        /// <summary>
        /// Ends the session and writes final summary.
        /// </summary>
        public void EndSession(string outcome)
        {
            _sessionTimer.Stop();
            
            var summary = GetSummary();
            Record(new TelemetryEvent
            {
                EventType = "SESSION_END",
                AgentState = "Completed",
                DurationMs = _sessionTimer.ElapsedMilliseconds,
                Data = new { Outcome = outcome, Summary = summary }
            });
        }

        /// <summary>
        /// Loads a session for replay.
        /// </summary>
        public static List<TelemetryEvent> LoadSession(string sessionPath)
        {
            var events = new List<TelemetryEvent>();
            
            if (File.Exists(sessionPath))
            {
                foreach (var line in File.ReadLines(sessionPath))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var evt = JsonSerializer.Deserialize<TelemetryEvent>(line);
                        if (evt != null) events.Add(evt);
                    }
                }
            }

            return events;
        }

        private class ToolExecutionTracker : IDisposable
        {
            private readonly TelemetryService _telemetry;
            private readonly string _toolType;
            private readonly string _target;
            private readonly Stopwatch _timer;

            public ToolExecutionTracker(TelemetryService telemetry, string toolType, string target)
            {
                _telemetry = telemetry;
                _toolType = toolType;
                _target = target;
                _timer = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _timer.Stop();
                _telemetry.Record(new TelemetryEvent
                {
                    EventType = "TOOL_EXECUTION",
                    AgentState = "ExecutingTool",
                    Tool = _toolType,
                    Target = _target,
                    DurationMs = _timer.ElapsedMilliseconds,
                    Result = "Completed"
                });
            }
        }
    }

    public class TelemetryEvent
    {
        public DateTime Timestamp { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public long ElapsedMs { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string AgentState { get; set; } = string.Empty;
        public string? Tool { get; set; }
        public string? Target { get; set; }
        public long DurationMs { get; set; }
        public string? Result { get; set; }
        public object? Data { get; set; }
    }

    public class SessionSummary
    {
        public string SessionId { get; set; } = string.Empty;
        public int TotalEvents { get; set; }
        public long DurationMs { get; set; }
        public Dictionary<string, int> ToolCalls { get; set; } = new();
        public int Errors { get; set; }
    }
}
