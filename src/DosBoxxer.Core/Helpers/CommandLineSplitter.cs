using System.Text;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Splits a user supplied argument string into individual arguments so they can be handed to
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>. This keeps the arguments out
/// of any shell, which is what prevents command injection: the string is never concatenated
/// into a command line, it is passed as a vector to <c>exec</c>/<c>CreateProcess</c>.
/// </summary>
public static class CommandLineSplitter
{
    public static IReadOnlyList<string> Split(string? arguments)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasContent = false;

        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];

            switch (c)
            {
                case '"':
                    inQuotes = !inQuotes;
                    hasContent = true;
                    break;

                case '\\' when inQuotes && i + 1 < arguments.Length && arguments[i + 1] == '"':
                    current.Append('"');
                    i++;
                    hasContent = true;
                    break;

                case ' ' or '\t' or '\r' or '\n' when !inQuotes:
                    if (hasContent)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        hasContent = false;
                    }

                    break;

                default:
                    current.Append(c);
                    hasContent = true;
                    break;
            }
        }

        if (hasContent)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
