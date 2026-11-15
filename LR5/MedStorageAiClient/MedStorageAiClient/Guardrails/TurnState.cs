namespace MedStorage.AiClient.Guardrails;

// Per-turn memory of what the LLM has already verified; reset before every user request.
// Used by ToolChainFilter to enforce the "check -> then add" order of the call chain.
public class TurnState
{
    private readonly Dictionary<string, (bool InCatalog, bool InKit)> _checked = new(StringComparer.OrdinalIgnoreCase);

    public int ToolCalls { get; set; }

    public void MarkChecked(string tradeName, bool existsInCatalog, bool existsInKit) =>
        _checked[tradeName.Trim()] = (existsInCatalog, existsInKit);

    public (bool InCatalog, bool InKit)? GetCheck(string tradeName) =>
        _checked.TryGetValue(tradeName.Trim(), out var result) ? result : null;

    public void Reset()
    {
        _checked.Clear();
        ToolCalls = 0;
    }
}
