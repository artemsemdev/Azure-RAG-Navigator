using System.Text.RegularExpressions;

namespace RAGNavigator.Application.Services;

/// <summary>
/// Sanitizes user input to mitigate prompt injection and control character attacks.
/// Returns a <see cref="SanitizationResult"/> containing cleaned text and a flag
/// indicating whether suspicious prompt injection patterns were detected.
/// </summary>
public static partial class InputSanitizer
{
    /// <summary>
    /// Patterns that indicate an attempt to override system instructions.
    /// Matched case-insensitively against the user's input.
    /// </summary>
    private static readonly string[] InjectionPatterns =
    [
        @"ignore\s+(all\s+)?(previous|prior|above)\s+(instructions|rules|prompts)",
        @"disregard\s+(all\s+)?(previous|prior|above)\s+(instructions|rules|prompts)",
        @"forget\s+(all\s+)?(previous|prior|above)\s+(instructions|rules|prompts)",
        @"you\s+are\s+now\s+(a|an|the)\b",
        @"new\s+instructions?\s*:",
        @"system\s*prompt\s*:",
        @"override\s+(system|instructions|rules)",
        @"\bdo\s+not\s+follow\s+(the\s+)?(system|previous|above)",
        @"act\s+as\s+(a|an|if)\b",
        @"pretend\s+(you\s+are|to\s+be)",
        @"output\s+(the\s+)?(system|full|entire)\s+prompt",
        @"reveal\s+(the\s+)?(system|hidden)\s+(prompt|instructions)",
        @"repeat\s+(the\s+)?(text|words|instructions)\s+above",
    ];

    private static readonly Regex[] CompiledPatterns = InjectionPatterns
        .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex ControlCharPattern();

    [GeneratedRegex(@"[\u200B-\u200F\u2028-\u202F\uFEFF]")]
    private static partial Regex InvisibleUnicodePattern();

    /// <summary>
    /// Sanitizes user input by stripping control characters and detecting prompt injection patterns.
    /// </summary>
    public static SanitizationResult Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new SanitizationResult(string.Empty, false, []);

        // Step 1: Strip ASCII control characters (except \t, \n, \r)
        var cleaned = ControlCharPattern().Replace(input, string.Empty);

        // Step 2: Strip invisible Unicode characters (zero-width spaces, etc.)
        cleaned = InvisibleUnicodePattern().Replace(cleaned, string.Empty);

        // Step 3: Normalize whitespace
        cleaned = cleaned.Trim();

        // Step 4: Detect prompt injection patterns
        var matchedPatterns = new List<string>();
        foreach (var pattern in CompiledPatterns)
        {
            if (pattern.IsMatch(cleaned))
                matchedPatterns.Add(pattern.ToString());
        }

        return new SanitizationResult(cleaned, matchedPatterns.Count > 0, matchedPatterns);
    }
}

public sealed record SanitizationResult(
    string SanitizedInput,
    bool IsSuspicious,
    IReadOnlyList<string> MatchedPatterns);
