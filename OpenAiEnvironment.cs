using Microsoft.Win32;

namespace ImagePromptStudio;

public static class OpenAiEnvironment
{
    public const string ApiKeyVariable = "OPENAI_API_KEY";
    public const string AdminKeyVariable = "OPENAI_ADMIN_KEY";

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
    public static bool HasAdminKey => !string.IsNullOrWhiteSpace(AdminKey);
    public static string ApiKey => GetVariable(ApiKeyVariable);
    public static string AdminKey => GetVariable(AdminKeyVariable);

    public static string CostsApiKey => HasAdminKey ? AdminKey : ApiKey;

    public static string GetVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return "";
        }

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(root == Registry.CurrentUser
                ? "Environment"
                : @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
            value = key?.GetValue(name) as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                Environment.SetEnvironmentVariable(name, value);
                return value;
            }
        }

        return "";
    }

    public static void SetUserApiKey(string apiKey)
    {
        var trimmed = apiKey.Trim();
        Environment.SetEnvironmentVariable(ApiKeyVariable, trimmed);

        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(ApiKeyVariable, trimmed, EnvironmentVariableTarget.User);
        }
    }
}
