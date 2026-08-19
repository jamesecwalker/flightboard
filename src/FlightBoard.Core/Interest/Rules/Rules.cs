using FlightBoard.Core.Geo;

namespace FlightBoard.Core.Interest.Rules;

public static class Categories
{
    public const string Emergency = "emergency";
    public const string Military = "military";
    public const string Watch = "watch";
    public const string FirstSighting = "first-sighting";
    public const string UnusualType = "unusual-type";
    public const string Private = "private";
    public const string Oddity = "oddity";
}

/// <summary>Squawk 7700/7600/7500 or an ADS-B emergency status. Always wins.</summary>
public sealed class EmergencyRule : IInterestRule
{
    public string Name => "emergency";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var f = ctx.Flight;
        if (!f.Emergency) yield break;
        var label = f.Squawk switch
        {
            "7700" => "EMERGENCY",
            "7600" => "RADIO FAILURE",
            "7500" => "HIJACK",
            _ => "EMERGENCY",
        };
        yield return new InterestTag(label, 100, Categories.Emergency);
    }
}

/// <summary>Military / government / state flights, via readsb's military flag, hex ranges, callsign prefixes and serial-style registrations.</summary>
public sealed class MilitaryGovRule : IInterestRule
{
    public string Name => "military";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var o = ctx.Options;
        var f = ctx.Flight;
        var cs = ctx.Callsign?.ToUpperInvariant();

        if (cs is not null)
        {
            foreach (var (prefix, label) in o.MilitaryCallsignPrefixes)
            {
                if (cs.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new InterestTag(label, 80, Categories.Military);
                    yield break;
                }
            }
        }
        foreach (var range in o.MilitaryHexRanges)
        {
            if (range.Contains(f.Hex))
            {
                yield return new InterestTag(range.Label, 80, Categories.Military);
                yield break;
            }
        }
        var reg = ctx.Registration?.ToUpperInvariant();
        if (reg is not null && reg.Length >= 4 && !reg.Contains('-') && o.MilitaryRegistrationPrefixes.Any(p => reg.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new InterestTag("UK MILITARY", 80, Categories.Military);
            yield break;
        }
        if (f.MilitaryFlagged)
            yield return new InterestTag("MILITARY", 75, Categories.Military);
    }
}

/// <summary>Registrations or hexes the user specifically asked to be told about.</summary>
public sealed class WatchListRule : IInterestRule
{
    public string Name => "watch-list";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var o = ctx.Options;
        if (o.WatchHexes.TryGetValue(ctx.Flight.Hex, out var byHex)) yield return new InterestTag(byHex, 85, Categories.Watch);
        var reg = ctx.Registration;
        if (reg is not null && o.WatchRegistrations.TryGetValue(reg, out var byReg)) yield return new InterestTag(byReg, 85, Categories.Watch);
    }
}

/// <summary>Never seen this airframe / airline / origin / type before (once the DB has warmed up).</summary>
public sealed class FirstSightingRule : IInterestRule
{
    public string Name => "first-sighting";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var s = ctx.Sightings;
        if ((ctx.Now - s.FirstRunAt).TotalDays < ctx.Options.FirstSightingWarmupDays) yield break;

        var e = ctx.Enriched;
        if (e.AirlineIcao is not null && !s.HasSeenAirline(e.AirlineIcao)) yield return new InterestTag("NEW AIRLINE", 62, Categories.FirstSighting);
        var type = ctx.TypeIcao;
        if (type is not null && !s.HasSeenType(type)) yield return new InterestTag("NEW TYPE " + type, 61, Categories.FirstSighting);
        if (e.OriginIcao is not null && !s.HasSeenOrigin(e.OriginIcao)) yield return new InterestTag("NEW ROUTE", 60, Categories.FirstSighting);
        if (!s.HasSeenHex(ctx.Flight.Hex)) yield return new InterestTag("FIRST VISIT", 55, Categories.FirstSighting);
    }
}

/// <summary>A380s, 747s, Hercs, helicopters, balloons... whatever is on the configured list.</summary>
public sealed class UnusualTypeRule : IInterestRule
{
    public string Name => "unusual-type";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var o = ctx.Options;
        var type = ctx.TypeIcao;
        if (type is not null && o.UnusualTypes.TryGetValue(type, out var label))
            yield return new InterestTag(label, 50, Categories.UnusualType);
        var cat = ctx.Flight.Category;
        if (cat is not null && o.UnusualCategories.TryGetValue(cat, out var catLabel))
            yield return new InterestTag(catLabel, 50, Categories.UnusualType);
    }
}

/// <summary>Business jets: small/medium emitter category, no scheduled route, bizjet type (or unknown type).</summary>
public sealed class PrivateJetRule : IInterestRule
{
    public string Name => "private-jet";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var f = ctx.Flight;
        var e = ctx.Enriched;
        var smallish = f.Category is "A1" or "A2" or null;
        if (!smallish) yield break;
        if (e.RouteFound) yield break; // a scheduled route means an airline, not a private owner
        var type = ctx.TypeIcao;
        var bizType = type is null || ctx.Options.BizjetTypePrefixes.Any(p => type.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (!bizType) yield break;
        // Airliner-sized A1/A2 is rare; helicopters/GA without a type we would rather not mislabel.
        if (type is null && f.Category is null) yield break;
        yield return new InterestTag("PRIVATE JET", 30, Categories.Private);
    }
}

/// <summary>Go-arounds, very long-haul origins, diversions.</summary>
public sealed class OdditiesRule : IInterestRule
{
    public string Name => "oddities";
    public IEnumerable<InterestTag> Evaluate(InterestContext ctx)
    {
        var f = ctx.Flight;
        var e = ctx.Enriched;
        var o = ctx.Options;

        if (f.WentAround) yield return new InterestTag("GO AROUND", 45, Categories.Oddity);

        if (e.OriginLat is { } lat && e.OriginLon is { } lon)
        {
            var km = GeoPoint.HaversineKm(ctx.Home, new GeoPoint(lat, lon));
            if (km >= o.LongHaulKm) yield return new InterestTag($"LONG HAUL {Math.Round(km / 100) / 10:0.#}K KM", 20, Categories.Oddity);
        }

        if (o.DetectDiversions && e.RouteFound && e.DestinationIcao is not null && !string.IsNullOrEmpty(o.HomeAirportIcao)
            && !string.Equals(e.DestinationIcao, o.HomeAirportIcao, StringComparison.OrdinalIgnoreCase))
        {
            yield return new InterestTag("DIVERTED", 35, Categories.Oddity);
        }
    }
}
