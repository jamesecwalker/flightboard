using FlightBoard.Core.Board;
using FlightBoard.Core.Interest;
using FlightBoard.Core.Model;

namespace FlightBoard.Tests;

public class FrameLayoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    private static BoardMessage Msg(InterestTag? tag = null, string airline = "easyJet", string origin = "Alicante") =>
        new("U2 8123", airline, origin, "G-EZTD", "A320", tag, Now);

    [Fact]
    public void Four_row_board_uses_flight_airline_origin_tag()
    {
        var caps = BoardCapabilities.Default(4, 22);
        var f = FrameLayout.Render(Msg(new InterestTag("A380", 50, "unusual-type")), caps);
        Assert.Equal(4, f.Rows.Length);
        Assert.All(f.Rows, r => Assert.Equal(22, r.Length));
        Assert.StartsWith("U2 8123", f.Rows[0]);
        Assert.EndsWith("A320", f.Rows[0]);
        Assert.StartsWith("EASYJET", f.Rows[1]);
        Assert.StartsWith("FROM ALICANTE", f.Rows[2]);
        Assert.Contains("* A380 *", f.Rows[3]);
        Assert.NotNull(f.Accent);
        Assert.All(f.Accent![3], a => Assert.True(a));
        Assert.All(f.Accent[0], a => Assert.False(a));
    }

    [Fact]
    public void No_tag_means_no_accent_and_blank_tag_row()
    {
        var f = FrameLayout.Render(Msg(), BoardCapabilities.Default(4, 22));
        Assert.Null(f.Accent);
        Assert.Equal(new string(' ', 22), f.Rows[3]);
    }

    [Fact]
    public void Low_score_tag_shows_label_but_no_accent()
    {
        var f = FrameLayout.Render(Msg(new InterestTag("PRIVATE JET", 30, "private")), BoardCapabilities.Default(4, 22));
        Assert.Contains("PRIVATE JET", f.Rows[3]);
        Assert.Null(f.Accent);
    }

    [Fact]
    public void Three_row_board_puts_tag_on_first_row_right()
    {
        var f = FrameLayout.Render(Msg(new InterestTag("RAF", 80, "military")), BoardCapabilities.Default(3, 20));
        Assert.Equal(3, f.Rows.Length);
        Assert.StartsWith("U2 8123", f.Rows[0]);
        Assert.EndsWith("* RAF *", f.Rows[0]);
        Assert.StartsWith("EASYJET", f.Rows[1]);
        Assert.StartsWith("FROM ALICANTE", f.Rows[2]);
    }

    [Fact]
    public void Two_row_board_packs_flight_and_airline()
    {
        var f = FrameLayout.Render(Msg(), BoardCapabilities.Default(2, 16));
        Assert.Equal("U2 8123  EASYJET", f.Rows[0]);
        Assert.Equal("FROM ALICANTE   ", f.Rows[1]);
    }

    [Fact]
    public void Long_text_is_truncated_not_wrapped()
    {
        var f = FrameLayout.Render(Msg(airline: "Norwegian Air Shuttle ASA", origin: "Palma de Mallorca"), BoardCapabilities.Default(4, 16));
        Assert.All(f.Rows, r => Assert.Equal(16, r.Length));
        Assert.Equal("NORWEGIAN AIR SH", f.Rows[1]);
        Assert.Equal("FROM PALMA DE MA", f.Rows[2]);
    }

    [Fact]
    public void Accents_and_typography_are_transliterated_to_the_charset()
    {
        Assert.Equal("MALAGA", Transliterate.ToCharset("Málaga", BoardCapabilities.DefaultCharset));
        Assert.Equal("ZURICH", Transliterate.ToCharset("Zürich", BoardCapabilities.DefaultCharset));
        Assert.Equal("DUSSELDORF", Transliterate.ToCharset("Düsseldorf", BoardCapabilities.DefaultCharset));
        Assert.Equal("ROME-FIUMICINO", Transliterate.ToCharset("Rome–Fiumicino", BoardCapabilities.DefaultCharset));
        Assert.Equal("STRASSE", Transliterate.ToCharset("Straße", BoardCapabilities.DefaultCharset));
        // Characters the board does not have become spaces (and runs collapse).
        Assert.Equal("A B", Transliterate.ToCharset("A#@B", " AB"));
    }

    [Fact]
    public void Decoration_falls_back_when_board_has_no_star()
    {
        var caps = new BoardCapabilities(4, 22, " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-", false, TimeSpan.Zero);
        var f = FrameLayout.Render(Msg(new InterestTag("A380", 50, "x")), caps);
        Assert.Contains("- A380 -", f.Rows[3]);
        Assert.Null(f.Accent); // no colour support
    }

    [Fact]
    public void Idle_frame_is_centred()
    {
        var m = BoardMessage.Idle(Now, "GATWICK ARRIVALS", "10:00", "NEXT BA 2723 IN 3 MIN");
        var f = FrameLayout.Render(m, BoardCapabilities.Default(4, 22));
        Assert.Equal("   GATWICK ARRIVALS   ", f.Rows[0]);
        Assert.Equal("        10:00         ", f.Rows[1]);
        Assert.Equal("NEXT BA 2723 IN 3 MIN ", f.Rows[2]);
    }

    [Theory]
    [InlineData("EZY8123", "EZY 8123")]
    [InlineData("U28123", "U2 8123")]
    [InlineData("BA2723", "BA 2723")]
    [InlineData("RYR3PW", "RYR 3PW")]
    [InlineData("G-EZTD", "G-EZTD")]
    [InlineData("N123AB", "N123AB")]
    [InlineData("M-EGGA", "M-EGGA")]
    [InlineData("UAE15", "UAE 15")]
    public void Flight_numbers_get_a_space(string input, string expected) =>
        Assert.Equal(expected, MessageBuilder.SpaceFlightNumber(input));

    [Fact]
    public void Iata_form_only_used_for_numeric_flight_numbers()
    {
        var o = new BoardOptions();
        var f = new FlightBoard.Core.Tracking.TrackedFlight { Hex = "40782b", Callsign = "AUR2LG", Type = "AT76" };
        var e = FlightBoard.Core.Enrichment.Enriched.Empty(f.Hex, f.Callsign) with { FlightIata = "GR2LG", AirlineName = "Aurigny" };
        var m = MessageBuilder.ForFlight(f, e, FlightBoard.Core.Interest.InterestResult.None, o, Now);
        Assert.Equal("AUR 2LG", m.Flight);

        f = new FlightBoard.Core.Tracking.TrackedFlight { Hex = "406a3b", Callsign = "EZY8123", Type = "A320" };
        e = FlightBoard.Core.Enrichment.Enriched.Empty(f.Hex, f.Callsign) with { FlightIata = "U28123", AirlineName = "easyJet" };
        m = MessageBuilder.ForFlight(f, e, FlightBoard.Core.Interest.InterestResult.None, o, Now);
        Assert.Equal("U2 8123", m.Flight);
    }

    [Theory]
    [InlineData("Pisa International Airport", "Pisa")]
    [InlineData("Valencia Airport", "Valencia")]
    [InlineData("London Gatwick Airport", "London Gatwick")]
    [InlineData("Airport", "Airport")]
    public void Airport_names_are_shortened_for_the_board(string input, string expected) =>
        Assert.Equal(expected, FlightBoard.Core.Enrichment.Enriched.ShortenAirportName(input));

    [Fact]
    public void Departures_are_detected_from_the_route_and_rendered_as_TO()
    {
        var e = FlightBoard.Core.Enrichment.Enriched.Empty("400e14", "EZY61TU") with
        { OriginIcao = "EGKK", OriginCity = "London", DestinationIcao = "LEBL", DestinationCity = "Barcelona", AirlineName = "easyJet", RouteFound = true };
        Assert.True(e.IsDepartureFrom("EGKK"));
        Assert.False(e.IsDepartureFrom("EGLL"));
        var f = new FlightBoard.Core.Tracking.TrackedFlight { Hex = "400e14", Callsign = "EZY61TU", Type = "A319" };
        var m = MessageBuilder.ForFlight(f, e, FlightBoard.Core.Interest.InterestResult.None, new BoardOptions(), Now, isDeparture: true);
        var frame = FrameLayout.Render(m, BoardCapabilities.Default(4, 22));
        Assert.StartsWith("TO BARCELONA", frame.Rows[2]);
    }

    [Theory]
    [InlineData("A169", "AW169")]
    [InlineData("A20N", "A320NEO")]
    [InlineData("B38M", "737 MAX 8")]
    [InlineData("A320", "A320")]
    [InlineData("ZZZZ", "ZZZZ")]
    [InlineData(null, null)]
    public void Type_codes_get_friendly_names(string? input, string? expected) =>
        Assert.Equal(expected, new BoardOptions().DisplayType(input));

    [Fact]
    public void Queue_count_shows_bottom_right_and_updates_only_those_tiles()
    {
        var caps = BoardCapabilities.Default(4, 22);
        var a = FrameLayout.Render(Msg() with { QueuedBehind = 2 }, caps);
        Assert.EndsWith("+2", a.Rows[3]);
        var b = FrameLayout.Render(Msg(new InterestTag("A380", 50, "x")) with { QueuedBehind = 1 }, caps);
        Assert.Contains("* A380 *", b.Rows[3]);
        Assert.EndsWith("+1", b.Rows[3]);
        var c = FrameLayout.Render(Msg() with { QueuedBehind = 0 }, caps);
        Assert.Equal(new string(' ', 22), c.Rows[3]);
        // Changing only the count leaves the other rows identical (so a physical board flips two tiles, not all).
        var d = FrameLayout.Render(Msg() with { QueuedBehind = 3 }, caps);
        Assert.Equal(a.Rows[0], d.Rows[0]);
        Assert.Equal(a.Rows[1], d.Rows[1]);
        Assert.Equal(a.Rows[2], d.Rows[2]);
        // Three-row boards put it on the airline row.
        var e = FrameLayout.Render(Msg() with { QueuedBehind = 2 }, BoardCapabilities.Default(3, 20));
        Assert.EndsWith("+2", e.Rows[1]);
    }

    [Fact]
    public void Low_alert_sits_bottom_left_with_amber_and_coexists_with_tag_and_queue()
    {
        var caps = BoardCapabilities.Default(4, 22);
        var f = FrameLayout.Render(Msg() with { AltitudeFt = 1300, IsLow = true }, caps);
        Assert.StartsWith("LOW 1300FT", f.Rows[3]);
        Assert.NotNull(f.Accent);
        Assert.True(f.Accent![3][0] && f.Accent[3][9]);
        Assert.False(f.Accent[3][10]);

        var g = FrameLayout.Render(Msg(new InterestTag("HELICOPTER", 50, "x")) with { AltitudeFt = 1300, IsLow = true, QueuedBehind = 2 }, caps);
        Assert.StartsWith("LOW", g.Rows[3]);                 // shrinks to fit beside the centred tag
        Assert.Contains("* HELICOPTER *", g.Rows[3]);
        Assert.EndsWith("+2", g.Rows[3]);
        Assert.Equal(22, g.Rows[3].Length);

        var h = FrameLayout.Render(Msg() with { AltitudeFt = 5500, IsLow = false }, caps);
        Assert.Equal(new string(' ', 22), h.Rows[3]);
    }
}
