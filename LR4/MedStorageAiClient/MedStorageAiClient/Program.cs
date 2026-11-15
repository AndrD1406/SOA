using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using MedStorage.AiClient.Api;
using MedStorage.AiClient.Guardrails;
using MedStorage.AiClient.Plugins;
using MedStorage.AiClient.Prompts;

// LR4, Task 1: console LLM client with function calling over the MedStorage REST API.
//   dotnet run                                   -> interactive chat
//   dotnet run -- "Check if Paracetamol is in my kit, if not add it, expires 2027-05-31"   -> one request

Console.OutputEncoding = Encoding.UTF8;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var llm = config.GetSection("Llm");
var apiKey = llm["ApiKey"] is { Length: > 0 } key ? key : Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var endpoint = llm["Endpoint"];
var maxTokensPerRequest = llm.GetValue("MaxTokensPerRequest", 400);
var maxToolCallsPerTurn = llm.GetValue("MaxToolCallsPerTurn", 6);

if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(endpoint))
{
    Console.Error.WriteLine("Set OPENAI_API_KEY (or Llm__ApiKey), or Llm__Endpoint for a local OpenAI-compatible server.");
    return 1;
}

var kitId = config.GetValue<Guid>("MedStorage:DefaultKitId");
var api = new MedStorageApiClient(
    new HttpClient { BaseAddress = new Uri(config["MedStorage:CatalogUrl"]!), Timeout = TimeSpan.FromSeconds(10) },
    new HttpClient { BaseAddress = new Uri(config["MedStorage:InventoryUrl"]!), Timeout = TimeSpan.FromSeconds(10) });

var turnState = new TurnState();
var budget = new TokenBudget(llm.GetValue("MaxTokensPerSession", 20000));

// ---- Kernel: model + tools + filters ----
var builder = Kernel.CreateBuilder();
if (string.IsNullOrEmpty(endpoint))
    builder.AddOpenAIChatCompletion(llm["ModelId"]!, apiKey!);
else
    builder.AddOpenAIChatCompletion(llm["ModelId"]!, new Uri(endpoint), apiKey ?? "local");

builder.Plugins.AddFromObject(new MedStorageKitPlugin(api, kitId, turnState), "kit");
builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(new ToolChainFilter(turnState, budget, maxToolCallsPerTurn));
var kernel = builder.Build();

var chat = kernel.GetRequiredService<IChatCompletionService>();
var settings = new OpenAIPromptExecutionSettings
{
    // Guardrail (Level 1): hard cap on the completion size of each LLM request
    MaxTokens = maxTokensPerRequest,
    Temperature = 0,
    // Auto function calling: SK executes requested tools and sends the results back to the model
    // in a loop, which is what lets the model build the check -> add chain within one user turn
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

var history = new ChatHistory(SystemPrompt.Build(DateOnly.FromDateTime(DateTime.Now)));

Console.WriteLine($"MedStorage AI client | model {llm["ModelId"]} | kit {kitId} | token budget {budget.Remaining}");

if (args.Length > 0)
{
    await HandleAsync(string.Join(' ', args));
    return 0;
}

Console.WriteLine("Type a request (empty line to exit).");
while (Console.ReadLine() is { Length: > 0 } line)
    await HandleAsync(line);
return 0;

async Task HandleAsync(string userInput)
{
    // Guardrail (Level 1): no new LLM calls once the session budget is spent
    if (budget.IsExhausted)
    {
        Console.WriteLine("[guardrail] Session token budget is exhausted. Restart the client.");
        return;
    }

    // Guardrail (Level 3): input validation - blocked requests never reach the model
    var verdict = PromptInjectionGuard.Check(userInput);
    if (!verdict.Allowed)
    {
        Console.WriteLine($"[guardrail] Request rejected: {verdict.Reason}");
        return;
    }

    turnState.Reset();
    history.AddUserMessage(PromptInjectionGuard.Wrap(verdict.Text));

    try
    {
        var reply = await chat.GetChatMessageContentAsync(history, settings, kernel);
        budget.Add(reply);

        // Guardrail (Level 3): output check - a reply that leaks the system prompt is not shown
        var text = reply.Content ?? "";
        if (text.Contains(SystemPrompt.Canary, StringComparison.OrdinalIgnoreCase))
            text = "I can only help with your medicine kit.";
        else if (reply.Role == AuthorRole.Tool)
            text = "The request was stopped by a guardrail.";

        history.AddAssistantMessage(text);
        Console.WriteLine($"Assistant: {text}");
    }
    catch (HttpOperationException ex)
    {
        // The LLM provider failed (bad key, quota, network); the user message stays unanswered
        history.RemoveAt(history.Count - 1);
        Console.WriteLine($"[error] LLM request failed: {ex.Message}");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"[error] MedStorage API is unreachable: {ex.Message}");
    }

    Console.WriteLine($"  (tokens used {budget.Used}, remaining {budget.Remaining})");
}
