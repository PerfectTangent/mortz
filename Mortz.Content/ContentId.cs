using System.Text.RegularExpressions;

namespace Mortz.Content;

public static partial class ContentId
{
    public const string PATTERN = "^[a-z0-9][a-z0-9_-]*$";

    public static bool IsValid(string value) => Pattern().IsMatch(value);

    [GeneratedRegex(PATTERN, RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
