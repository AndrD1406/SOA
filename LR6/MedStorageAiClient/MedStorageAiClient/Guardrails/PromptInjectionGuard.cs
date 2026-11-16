using System.Text;
using System.Text.RegularExpressions;

namespace MedStorage.AiClient.Guardrails;

public record GuardVerdict(bool Allowed, string Text, string? Reason = null)
{
    public static GuardVerdict Block(string reason) => new(false, "", reason);
}

// Guardrail (Level 3): input validation against prompt injection, applied BEFORE the text reaches the LLM.
// Defence in depth - this filter is the first layer; the strict system prompt (SystemPrompt.cs),
// the tool argument validation and the chain filter are the next ones, so a phrasing that slips
// past these patterns still cannot make the model act outside the allowed tools.
public static partial class PromptInjectionGuard
{
    public const int MaxInputLength = 500;

    // Typical jailbreak / instruction-override phrasings (EN + UA + RU) and chat-template tokens
    [GeneratedRegex("""
        ignore\s+(all\s+|any\s+)?(previous|prior|above|earlier|the)\s+(instructions|rules|prompts?)
        | disregard\s+(the\s+|all\s+)?(previous|above|system)
        | forget\s+(all\s+|your\s+)?(instructions|rules)
        | (reveal|show|print|repeat)\s+(me\s+)?(the\s+|your\s+)?(system\s+prompt|instructions)
        | you\s+are\s+now | act\s+as | pretend\s+(to\s+be|you) | developer\s+mode | jailbreak | \bDAN\b
        | ігноруй | проігноруй | забудь\s+(всі|усі|попередні|свої) | ти\s+тепер | покажи\s+(системн|інструкц)
        | игнорируй | забудь\s+(все|предыдущие) | ты\s+теперь
        | <\|?(im_start|im_end|system|endoftext)\|?> | \[/?INST\] | ^\s*\#{2,}\s*system | </?user_request>
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline)]
    private static partial Regex InjectionPattern();

    public static GuardVerdict Check(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return GuardVerdict.Block("Empty request.");

        // Normalization defeats look-alike tricks: full-width letters, zero-width spaces, control chars
        var text = new string(input.Normalize(NormalizationForm.FormKC)
            .Where(c => !char.IsControl(c) && c is not ('​' or '‌' or '‍' or '⁠' or '﻿'))
            .ToArray()).Trim();

        if (text.Length > MaxInputLength)
            return GuardVerdict.Block($"Request is longer than {MaxInputLength} characters.");

        if (InjectionPattern().Match(text) is { Success: true } match)
            return GuardVerdict.Block($"Possible prompt injection detected: \"{match.Value.Trim()}\".");

        return new GuardVerdict(true, text);
    }

    // The user's text is passed to the model as delimited data, never concatenated into instructions
    public static string Wrap(string sanitizedInput) => $"<user_request>\n{sanitizedInput}\n</user_request>";
}
