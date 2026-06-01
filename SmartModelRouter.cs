using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalCursor.Services.Core
{
    public class SmartModelRouter
    {
        private readonly ModelRegistry _registry;

        public SmartModelRouter(ModelRegistry registry)
        {
            _registry = registry;
        }

        public ModelMetadata Route(RoutingContext context)
        {
            var models = _registry.GetAllModels().ToList();
            if (!models.Any()) throw new InvalidOperationException("No models available for routing.");

            var scoredModels = models.Select(m => new { Model = m, Score = CalculateScore(m, context) })
                                    .OrderByDescending(x => x.Score)
                                    .ToList();

            // Log routing decision (Simulation of output requirement)
            Console.WriteLine($"[ROUTER] Deciding for {context.Role}: Selected {scoredModels.First().Model.Id} (Score: {scoredModels.First().Score})");

            return scoredModels.First().Model;
        }

        private double CalculateScore(ModelMetadata model, RoutingContext context)
        {
            double score = 0;

            // 1. Quality / Accuracy Score
            double qualityBase = model.IsLocal ? 70 : 95; // Cloud models generally higher base quality
            if (model.Capabilities.HasFlag(ModelCapabilities.ToolCalling)) qualityBase += 10;
            
            // 2. Speed Score
            double speedBase = model.IsLocal ? 90 : 60; // Local models are faster for simple tasks

            // 3. Weighting based on Role
            switch (context.Role)
            {
                case AgentRole.Planner:
                    score = (qualityBase * 0.8) + (speedBase * 0.2);
                    break;
                case AgentRole.Executor:
                    score = (qualityBase * 0.4) + (speedBase * 0.6);
                    if (model.IsLocal) score += 20; // Prefer local for execution
                    break;
                case AgentRole.Reviewer:
                    score = (qualityBase * 0.9) + (speedBase * 0.1);
                    break;
                case AgentRole.SecurityReviewer:
                    if (model.IsLocal && !model.Capabilities.HasFlag(ModelCapabilities.ToolCalling)) score -= 50;
                    score = (qualityBase * 0.95) + (speedBase * 0.05);
                    break;
            }

            // 4. Contextual Adjustments
            if (context.RequiresSpeed && model.IsLocal) score += 30;
            if (context.RequiresAccuracy && !model.IsLocal) score += 30;
            if (context.IsSecurityCritical && !model.Capabilities.HasFlag(ModelCapabilities.ToolCalling)) score -= 100;

            return score;
        }
    }
}
