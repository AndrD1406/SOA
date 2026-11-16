using System.Text.Json;
using Microsoft.SemanticKernel;

namespace MedStorage.AiClient.Guardrails;

// Runs around every tool call the model requests during auto function calling.
// Guardrails enforced here do not depend on the model obeying the system prompt:
// - Level 2 (call chain): add_medicine_to_kit is rejected unless check_medicine_exists was called
//   for the same medicine earlier in this turn, so "check -> add" is guaranteed, not just requested;
// - Level 3: a maximum number of tool calls per turn stops runaway loops;
// - Level 1: usage of each intermediate LLM request is charged to the session token budget.
public class ToolChainFilter(TurnState turnState, TokenBudget budget, int maxToolCallsPerTurn) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        // One LLM response may request several tools; charge its usage once, on the first of them
        if (context.FunctionSequenceIndex == 0)
            budget.Add(context.ChatMessageContent);

        var name = context.Function.Name;
        var args = string.Join(", ", context.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? []);

        if (++turnState.ToolCalls > maxToolCallsPerTurn || budget.IsExhausted)
        {
            Log(ConsoleColor.Red, $"  [guardrail] stop: tool-call limit ({maxToolCallsPerTurn}) or token budget reached");
            context.Result = new FunctionResult(context.Function, "Limit reached. Stop calling tools.");
            context.Terminate = true;
            return;
        }

        if (name == "add_medicine_to_kit" && CheckChainOrder(context) is { } violation)
        {
            Log(ConsoleColor.Red, $"  [guardrail] rejected {name}({args}): {violation}");
            // The model gets the reason as the tool result and can repair the chain itself
            context.Result = new FunctionResult(context.Function, new { error = violation });
            return;
        }

        Log(ConsoleColor.DarkCyan, $"  -> {name}({args})");
        await next(context);
        Log(ConsoleColor.DarkGray, $"  <- {Truncate(JsonSerializer.Serialize(context.Result.GetValue<object>()))}");
    }

    private string? CheckChainOrder(AutoFunctionInvocationContext context)
    {
        var tradeName = context.Arguments?["tradeName"]?.ToString() ?? "";
        return turnState.GetCheck(tradeName) switch
        {
            null => $"Chain violation: call check_medicine_exists(\"{tradeName}\") first.",
            { InCatalog: false } => $"'{tradeName}' is not in the drug catalog and cannot be added.",
            _ => null
        };
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";

    private static void Log(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
