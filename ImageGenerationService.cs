using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ImagePromptStudio;

public sealed class ImageGenerationService
{
    private const string GenerateEndpoint = "https://api.openai.com/v1/images/generations";
    private const string EditEndpoint = "https://api.openai.com/v1/images/edits";
    private const string EmptyEditPrompt = "Create a new edited version of the provided image. Preserve the source image faithfully unless other prompt details specify a change.";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    private static readonly HttpClient ModelsHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public bool HasApiKey => OpenAiEnvironment.HasApiKey;

    public async Task<IReadOnlyList<string>> GetAvailableImageModelsAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = OpenAiEnvironment.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await ModelsHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var models = document.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .OfType<string>()
            .Where(SupportsImageEdit)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ModelSortKey)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return models;
    }

    public string? ValidateForRun(GenerationSettings settings)
    {
        if (!OpenAiEnvironment.HasApiKey)
        {
            return "OPENAI_API_KEY is not set. Set it in your Windows environment and restart the app.";
        }

        if (settings.Mode == MainWindow.ModeEdit)
        {
            if (string.IsNullOrWhiteSpace(settings.Reference))
            {
                return "Choose a reference image or switch Mode to Generate new image.";
            }

            if (!File.Exists(settings.Reference))
            {
                return $"Reference image not found:\n{settings.Reference}";
            }
        }

        return null;
    }

    public static bool SupportsImageEdit(string model)
    {
        return !string.IsNullOrWhiteSpace(model) && model.StartsWith("gpt-image-", StringComparison.OrdinalIgnoreCase);
    }

    private static int ModelSortKey(string model)
    {
        return model switch
        {
            "gpt-image-1.5" => 0,
            "gpt-image-2" => 1,
            "gpt-image-1" => 2,
            "gpt-image-1-mini" => 3,
            _ => 100,
        };
    }

    public string BuildLogPreview(string prompt, string constraints, string negative, string outputPath, GenerationSettings settings)
    {
        var effectivePrompt = ComposePrompt(prompt, constraints, negative, settings.Mode);
        var lines = new List<string>
        {
            $"Endpoint: {(settings.Mode == MainWindow.ModeEdit ? EditEndpoint : GenerateEndpoint)}",
            $"Model: {settings.Model}",
            $"Size: {settings.Size}",
            $"Quality: {settings.Quality}",
            $"Background: {settings.Background}",
            $"Output: {settings.OutputFormat}",
        };

        if (settings.Mode == MainWindow.ModeEdit)
        {
            lines.Add($"Reference: {settings.Reference}");
            if (settings.Model != "gpt-image-2")
            {
                lines.Add($"Input fidelity: {settings.InputFidelity}");
            }
        }

        lines.Add($"Augment: {settings.Augment}");
        lines.Add($"Out: {outputPath}");
        lines.Add("");
        lines.Add("Prompt:");
        lines.Add(effectivePrompt);
        return string.Join(Environment.NewLine, lines);
    }

    public async Task<GenerationResult> RunAsync(
        string prompt,
        string constraints,
        string negative,
        string outputPath,
        GenerationSettings settings,
        CancellationToken cancellationToken,
        Action<string> onLog)
    {
        try
        {
            var apiKey = OpenAiEnvironment.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new GenerationResult(false, "OPENAI_API_KEY is not set.");
            }

            var effectivePrompt = ComposePrompt(prompt, constraints, negative, settings.Mode);
            var isEdit = settings.Mode == MainWindow.ModeEdit;

            onLog($"POST {(isEdit ? EditEndpoint : GenerateEndpoint)} ({settings.Model})" + Environment.NewLine);

            using var request = new HttpRequestMessage(HttpMethod.Post, isEdit ? EditEndpoint : GenerateEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            if (isEdit)
            {
                request.Content = BuildEditMultipart(effectivePrompt, settings);
            }
            else
            {
                request.Content = BuildGenerateJson(effectivePrompt, settings);
            }

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = TryExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}";
                onLog($"Error: {errorMessage}" + Environment.NewLine);
                return new GenerationResult(false, errorMessage);
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                return new GenerationResult(false, "OpenAI response had no image data.");
            }

            var first = data[0];
            if (!first.TryGetProperty("b64_json", out var b64) || b64.ValueKind != JsonValueKind.String)
            {
                return new GenerationResult(false, "OpenAI response did not include b64_json image data.");
            }

            var bytes = Convert.FromBase64String(b64.GetString() ?? "");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);

            onLog($"Saved {bytes.Length:N0} bytes." + Environment.NewLine);
            return new GenerationResult(true, null);
        }
        catch (OperationCanceledException)
        {
            return new GenerationResult(false, "Generation canceled.") { WasCanceled = true };
        }
        catch (Exception ex)
        {
            return new GenerationResult(false, ex.Message);
        }
    }

    private static HttpContent BuildGenerateJson(string prompt, GenerationSettings settings)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("model", settings.Model);
            writer.WriteString("prompt", prompt);
            writer.WriteNumber("n", 1);
            WriteIfSet(writer, "size", settings.Size);
            WriteIfSet(writer, "quality", settings.Quality);
            WriteIfSet(writer, "background", settings.Background);
            WriteIfSet(writer, "output_format", settings.OutputFormat);
            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(ms.ToArray());
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static HttpContent BuildEditMultipart(string prompt, GenerationSettings settings)
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(settings.Model), "model");
        multipart.Add(new StringContent(prompt), "prompt");
        multipart.Add(new StringContent("1"), "n");
        AddIfSet(multipart, "size", settings.Size);
        AddIfSet(multipart, "quality", settings.Quality);
        AddIfSet(multipart, "background", settings.Background);
        AddIfSet(multipart, "output_format", settings.OutputFormat);
        if (settings.Model != "gpt-image-2")
        {
            AddIfSet(multipart, "input_fidelity", settings.InputFidelity);
        }

        var referencePath = settings.Reference!;
        var fileBytes = File.ReadAllBytes(referencePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(referencePath));
        multipart.Add(fileContent, "image", Path.GetFileName(referencePath));

        return multipart;
    }

    private static void AddIfSet(MultipartFormDataContent form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            form.Add(new StringContent(value), name);
        }
    }

    private static void WriteIfSet(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(name, value);
        }
    }

    private static string GuessMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    private static string ComposePrompt(string prompt, string constraints, string negative, string mode)
    {
        var builder = new StringBuilder();
        var basePrompt = prompt?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(basePrompt) && mode == MainWindow.ModeEdit)
        {
            basePrompt = EmptyEditPrompt;
        }

        builder.Append(basePrompt);

        if (!string.IsNullOrWhiteSpace(constraints))
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("Required elements / constraints: ").Append(constraints.Trim());
        }

        if (!string.IsNullOrWhiteSpace(negative))
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("Avoid: ").Append(negative.Trim());
        }

        return builder.ToString();
    }

    public static string PromptSlug(string prompt)
    {
        var builder = new StringBuilder();
        foreach (var ch in (prompt ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch is ' ' or '-' or '_')
            {
                builder.Append('_');
            }
        }

        var slug = builder.ToString();
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        slug = slug.Trim('_');
        if (slug.Length == 0)
        {
            return "image";
        }

        return slug.Length <= 42 ? slug : slug[..42];
    }

    private static string? TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch
        {
            // fall through
        }

        return body.Length > 400 ? body[..400] : body;
    }
}

public sealed record GenerationResult(bool Success, string? Error)
{
    public bool WasCanceled { get; init; }
}
