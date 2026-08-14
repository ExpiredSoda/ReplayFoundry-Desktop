namespace ReplayFoundry.Desktop.Platform;

internal static class ExplicitRuntimeEnvironment
{
    public static string? Read(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new ArgumentException(
                "An explicit runtime variable name is required.",
                nameof(variableName));
        }

        string? processValue =
            Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(processValue))
        {
            return processValue.Trim();
        }

        string? userValue = Environment.GetEnvironmentVariable(
            variableName,
            EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(userValue)
            ? null
            : userValue.Trim();
    }
}
