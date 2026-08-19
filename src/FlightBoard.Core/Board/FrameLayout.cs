using FlightBoard.Core.Model;

namespace FlightBoard.Core.Board;

/// <summary>Renders a <see cref="BoardMessage"/> to a <see cref="BoardFrame"/> for a given board size/charset.</summary>
public static class FrameLayout
{
    public static BoardFrame Render(BoardMessage m, BoardCapabilities caps)
    {
        var rows = new string[caps.Rows];
        var accent = new bool[caps.Rows][];
        for (var i = 0; i < caps.Rows; i++) { rows[i] = ""; accent[i] = new bool[caps.Cols]; }

        if (m.IsIdle) RenderIdle(m, caps, rows);
        else RenderFlight(m, caps, rows, accent);

        for (var i = 0; i < caps.Rows; i++) rows[i] = Fit(rows[i], caps.Cols);
        var anyAccent = accent.Any(r => r.Any(x => x));
        return new BoardFrame(rows, caps.Colour && anyAccent ? accent : null);
    }

    private static void RenderIdle(BoardMessage m, BoardCapabilities caps, string[] rows)
    {
        var lines = new[] { m.IdleLine1, m.IdleLine2, m.IdleLine3 }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => T(l!, caps)).ToList();
        if (caps.Rows == 1) { rows[0] = string.Join("  ", lines); return; }
        // Vertically centre the idle lines.
        var start = Math.Max(0, (caps.Rows - lines.Count) / 2);
        for (var i = 0; i < lines.Count && start + i < caps.Rows; i++) rows[start + i] = Centre(lines[i], caps.Cols);
    }

    private static void RenderFlight(BoardMessage m, BoardCapabilities caps, string[] rows, bool[][] accent)
    {
        var flight = T(m.Flight, caps);
        var airline = T(m.Airline, caps);
        var origin = T(m.Origin, caps);
        var type = T(m.Type ?? "", caps);
        var tag = m.Tag is null ? "" : Decorate(T(m.Tag.Label, caps), caps);
        var from = origin.Length == 0 ? "" : T((m.IsDeparture ? "TO " : "FROM ") + origin, caps);
        var wantAccent = m.Tag?.Accent == true;
        var plus = m.QueuedBehind > 0 && caps.Charset.Contains('+') ? "+" + m.QueuedBehind : "";
        var low = m.IsLow ? (m.AltitudeFt is { } a ? $"LOW {a}FT" : "LOW") : "";

        switch (caps.Rows)
        {
            case 1:
                rows[0] = string.Join(" ", new[] { flight, airline, from, tag, low.Length > 0 ? "LOW" : "", plus }.Where(s => s.Length > 0));
                if (wantAccent) Array.Fill(accent[0], true);
                break;
            case 2:
                rows[0] = LeftRight(flight, low.Length > 0 ? "LOW" : airline, caps.Cols);
                rows[1] = LeftRight(from, tag.Length > 0 ? tag : plus, caps.Cols);
                if (wantAccent) Array.Fill(accent[1], true);
                if (low.Length > 0) MarkRange(accent[0], caps.Cols - 3, 3, caps.Cols);
                break;
            case 3:
                rows[0] = LeftRight(flight, tag.Length > 0 ? tag : type, caps.Cols);
                rows[1] = LeftRight(airline, plus, caps.Cols);
                rows[2] = LeftRight(from, low.Length > 0 ? "LOW" : "", caps.Cols);
                if (wantAccent) Array.Fill(accent[0], true);
                if (low.Length > 0) MarkRange(accent[2], caps.Cols - 3, 3, caps.Cols);
                break;
            case 4:
                // flight/type, airline, origin, then a shared status row: LOW (left) / tag (centre) / +N (right).
                rows[0] = LeftRight(flight, type, caps.Cols);
                rows[1] = airline;
                rows[2] = from;
                rows[3] = ThreeUp(low, tag, plus, caps.Cols, out var lowLen);
                if (wantAccent) Array.Fill(accent[3], true);
                else if (lowLen > 0) MarkRange(accent[3], 0, lowLen, caps.Cols);
                break;
            default:
                // 5+ rows: the alert tag gets its own full-width line, LOW / +N sit on a status line beneath it.
                var top = caps.Rows >= 7 ? 1 : 0; // leave a breathing row on tall boards
                rows[top + 0] = LeftRight(flight, type, caps.Cols);
                rows[top + 1] = airline;
                rows[top + 2] = from;
                rows[top + 3] = Centre(tag, caps.Cols);
                rows[top + 4] = LeftRight(low, plus, caps.Cols);
                if (wantAccent) Array.Fill(accent[top + 3], true);
                if (low.Length > 0) MarkRange(accent[top + 4], 0, low.Length, caps.Cols);
                break;
        }
    }

    private static string T(string s, BoardCapabilities caps) => Transliterate.ToCharset(s, caps.Charset);

    /// <summary>Wraps a tag in decoration chars the board actually has, e.g. "* A380 *".</summary>
    private static string Decorate(string label, BoardCapabilities caps)
    {
        if (label.Length == 0) return label;
        var deco = "*>-".FirstOrDefault(c => caps.Charset.Contains(c));
        return deco == default ? label : $"{deco} {label} {deco}";
    }

    /// <summary>left | centred middle | right, shrinking the left ("LOW 1300FT" → "LOW") and dropping the right if they do not fit.</summary>
    public static string ThreeUp(string left, string middle, string right, int cols, out int leftLen)
    {
        if (middle.Length == 0) { leftLen = left.Length; return LeftRight(left, right, cols); }
        var candidates = new[] { left, left.Length > 3 ? left[..3] : left, "" }.Distinct();
        foreach (var l in candidates)
        {
            var line = Centre(middle, cols);
            if (l.Length > 0)
            {
                if (l.Length + 1 > (cols - middle.Length) / 2) continue;          // would collide with the centred tag
                line = l + line[l.Length..];
            }
            line = line.TrimEnd();
            if (right.Length > 0)
            {
                if (line.Length + 1 + right.Length > cols) { if (l.Length > 0) continue; leftLen = 0; return Fit(line, cols); }
                line = line + new string(' ', cols - line.Length - right.Length) + right;
            }
            leftLen = l.Length;
            return Fit(line, cols);
        }
        leftLen = 0;
        return Fit(Centre(middle, cols), cols);
    }

    private static void MarkRange(bool[] row, int start, int length, int cols)
    {
        for (var i = Math.Max(0, start); i < Math.Min(cols, start + length); i++) row[i] = true;
    }

    public static string LeftRight(string left, string right, int cols)
    {
        if (right.Length == 0) return left;
        if (left.Length == 0) return right.PadLeft(cols);
        var gap = cols - left.Length - right.Length;
        if (gap >= 1) return left + new string(' ', gap) + right;
        // Not enough room: keep the left, truncate the right to whatever fits with one space.
        var room = cols - left.Length - 1;
        return room >= 3 ? left + " " + right[..room] : left;
    }

    public static string Centre(string s, int cols)
    {
        if (s.Length >= cols) return s;
        var pad = (cols - s.Length) / 2;
        return new string(' ', pad) + s;
    }

    public static string Fit(string s, int cols) => s.Length > cols ? s[..cols] : s.PadRight(cols);
}
