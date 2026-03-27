namespace OpenDeepWiki.Infrastructure;

public static class EnvironmentValueResolver
{
    public static string? Get(string key)
    {
        return Resolve(
            Environment.GetEnvironmentVariable(key),
            Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine));
    }

    public static string? Resolve(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
