using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LocalCursor.Services.Core
{
    public class RuntimeValidator
    {
        private readonly AgentRuntime _runtime;

        public RuntimeValidator(AgentRuntime runtime)
        {
            _runtime = runtime;
        }

        public async Task<bool> ValidateTestCase1()
        {
            // Simulate: "Create file and build"
            string input = "Create a file named app.cs and run dotnet build";
            
            Console.WriteLine($"Starting Validation Test: {input}");
            
            try 
            {
                await _runtime.RunAsync(input);
                
                // VALIDATION RULES
                var events = _runtime.EventLog;
                var observations = _runtime.Observations;

                // 1. Check FSM Transitions
                bool hasPlanning = events.Any(e => e.ToState == AgentState.Planning);
                bool hasExecuting = events.Any(e => e.ToState == AgentState.Executing);
                bool hasObserving = events.Any(e => e.ToState == AgentState.Observing);
                
                if (!hasPlanning || !hasExecuting || !hasObserving)
                {
                    Console.WriteLine("FAIL: Missing mandatory FSM states.");
                    return false;
                }

                // 2. Check Observation Structure
                if (observations.Any(o => o.Result == null || string.IsNullOrEmpty(o.ToolName)))
                {
                    Console.WriteLine("FAIL: Malformed observations.");
                    return false;
                }

                Console.WriteLine("PASS: Runtime Contract Validated.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: Runtime Exception: {ex.Message}");
                return false;
            }
        }
    }
}
