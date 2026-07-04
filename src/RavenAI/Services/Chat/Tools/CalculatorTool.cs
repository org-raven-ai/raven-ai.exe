using System.Globalization;
using System.Text.Json;

namespace RavenAI.Services.Chat.Tools;

/// <summary>
/// Evaluates a plain arithmetic expression exactly, so the model doesn't have to guess at math.
/// Backed by <see cref="ArithmeticEvaluator"/> — pure parsing, no code execution.
/// </summary>
public sealed class CalculatorTool : IChatTool
{
    public string Name => "calculate";

    public string Description =>
        "Evaluate a numeric arithmetic expression and return the exact result. Supports + - * / % ^, " +
        "parentheses, and unary minus (e.g. \"(2+3)*4 - 10/2\"). Prefer this over doing arithmetic yourself.";

    public BinaryData ParametersSchema => BinaryData.FromString(
        """
        {
          "type": "object",
          "properties": {
            "expression": { "type": "string", "description": "The arithmetic expression to evaluate." }
          },
          "required": ["expression"],
          "additionalProperties": false
        }
        """);

    public Task<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetProperty("expression", out JsonElement expr) || expr.ValueKind != JsonValueKind.String)
            return Task.FromResult("Error: missing required 'expression' string argument.");

        string input = expr.GetString() ?? string.Empty;
        try
        {
            double value = ArithmeticEvaluator.Evaluate(input);
            return Task.FromResult(value.ToString("G15", CultureInfo.InvariantCulture));
        }
        catch (FormatException ex)
        {
            return Task.FromResult($"Error: could not evaluate \"{input}\": {ex.Message}");
        }
    }
}
