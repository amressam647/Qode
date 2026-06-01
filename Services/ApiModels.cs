
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LocalCursor.Services
{
    // SimpleMessage moved to Core.AgentEngineModels.cs

    public class ModelInfo
    {
        public string Name { get; set; } = "";
        public string Provider { get; set; } = "";
        public string? BaseUrl { get; set; }
        public bool IsLocal { get; set; }
    }

    public class OpenAiRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "gpt-3.5-turbo";

        [JsonPropertyName("messages")]
        public List<SimpleMessage> Messages { get; set; } = new();
    }

    public class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public SimpleMessage Message { get; set; }
    }

    public class OpenAiResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice> Choices { get; set; }
    }
}
