using Microsoft.SemanticKernel;
using ChatTokenUsage = OpenAI.Chat.ChatTokenUsage;

namespace MedStorage.AiClient.Guardrails;

// Guardrail (Level 1): token spending limits.
// - per request: MaxTokens in the execution settings caps every single completion (see Program.cs);
// - per session: this budget sums the usage of every LLM round-trip, including the intermediate
//   ones made during auto function calling, and refuses new turns once it is exhausted.
public class TokenBudget(int maxTokensPerSession)
{
    public int Used { get; private set; }
    public int Remaining => Math.Max(0, maxTokensPerSession - Used);
    public bool IsExhausted => Used >= maxTokensPerSession;

    public void Add(ChatMessageContent? message)
    {
        if (message?.Metadata?.TryGetValue("Usage", out var usage) == true && usage is ChatTokenUsage u)
            Used += u.TotalTokenCount;
    }
}
