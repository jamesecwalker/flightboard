using System.Globalization;
using System.Text;

namespace FlightBoard.Core.Board;

/// <summary>Turns arbitrary text into something a limited-charset split-flap board can show.</summary>
public static class Transliterate
{
    private static readonly Dictionary<char, string> Special = new()
    {
        ['ß'] = "SS", ['Æ'] = "AE", ['æ'] = "AE", ['Ø'] = "O", ['ø'] = "O", ['Œ'] = "OE", ['œ'] = "OE",
        ['Ł'] = "L", ['ł'] = "L", ['Đ'] = "D", ['đ'] = "D", ['Þ'] = "TH", ['þ'] = "TH", ['ı'] = "I",
        ['–'] = "-", ['—'] = "-", ['‘'] = "'", ['’'] = "'", ['“'] = "\"", ['”'] = "\"", ['…'] = "...",
        ['×'] = "X", ['·'] = ".", [' '] = " ",
    };

    /// <summary>Upper-cases, strips diacritics, maps typographic punctuation, then drops anything not in <paramref name="charset"/>.</summary>
    public static string ToCharset(string? text, string charset)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            var piece = Special.TryGetValue(raw, out var s) ? s : raw.ToString();
            foreach (var ch in piece.ToUpperInvariant())
                sb.Append(charset.Contains(ch) ? ch : ' ');
        }
        return CollapseSpaces(sb.ToString());
    }

    public static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastSpace = false;
        foreach (var ch in s)
        {
            if (ch == ' ') { if (lastSpace) continue; lastSpace = true; }
            else lastSpace = false;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }
}
