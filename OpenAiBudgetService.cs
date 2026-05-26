using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ImagePromptStudio;

public sealed class OpenAiBudgetService
{
    private static readonly Uri CostsEndpoint = new("https://api.openai.com/v1/organization/costs");

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    public async Task<OpenAiBudgetSnapshot> GetMonthToDateAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = OpenAiEnvironment.CostsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return OpenAiBudgetSnapshot.Unavailable("No API key");
        }

        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var url = $"{CostsEndpoint}?start_time={monthStart.ToUnixTimeSeconds()}&end_time={now.ToUnixTimeSeconds()}&limit=31";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? OpenAiBudgetSnapshot.Unavailable(OpenAiEnvironment.HasAdminKey ? "Costs denied" : "Costs need admin key")
                : OpenAiBudgetSnapshot.Unavailable($"Costs HTTP {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var totals = ReadCostTotals(document.RootElement);
        if (totals.Count == 0)
        {
            return new OpenAiBudgetSnapshot("OpenAI: $0.00 MTD", "No costs reported for this month.", true, now);
        }

        var display = string.Join(" + ", totals
            .OrderBy(total => total.Key, StringComparer.OrdinalIgnoreCase)
            .Select(total => FormatCurrency(total.Value, total.Key)));
        return new OpenAiBudgetSnapshot(
            $"OpenAI: {display} MTD",
            $"Month-to-date spend from OpenAI organization costs. Updated {DateTime.Now:t}.",
            true,
            now);
    }

    private static Dictionary<string, decimal> ReadCostTotals(JsonElement root)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("data", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
        {
            return totals;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("amount", out var amount)
                    || !amount.TryGetProperty("value", out var value)
                    || !amount.TryGetProperty("currency", out var currency))
                {
                    continue;
                }

                var code = currency.GetString();
                if (string.IsNullOrWhiteSpace(code) || !value.TryGetDecimal(out var decimalValue))
                {
                    continue;
                }

                totals[code] = totals.GetValueOrDefault(code) + decimalValue;
            }
        }

        return totals;
    }

    private static string FormatCurrency(decimal amount, string currency)
    {
        return currency.Equals("usd", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"))
            : $"{amount:0.00} {currency.ToUpperInvariant()}";
    }
}

public sealed record OpenAiBudgetSnapshot(string Text, string Detail, bool IsAvailable, DateTimeOffset UpdatedAt)
{
    public static OpenAiBudgetSnapshot Unavailable(string detail)
    {
        return new OpenAiBudgetSnapshot("", detail, false, DateTimeOffset.UtcNow);
    }
}
