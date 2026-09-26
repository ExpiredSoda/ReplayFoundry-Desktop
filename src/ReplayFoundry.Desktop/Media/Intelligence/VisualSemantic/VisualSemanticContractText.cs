using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Media.Intelligence.Moments;
using ReplayFoundry.Desktop.Media.Moments;

namespace ReplayFoundry.Desktop.Media.Intelligence.VisualSemantic;

internal static class VisualSemanticContractText
{
    public static string Required(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Visual-semantic text cannot be blank.",
                parameterName);
        }

        string result = value.Trim();

        if (result.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Visual-semantic text cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return result;
    }

    public static string? Optional(
        string? value,
        string parameterName,
        int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, parameterName, maximumLength);
}
