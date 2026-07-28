using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Trimix.Core.Sites;

/// <summary>
/// One bundled read-only atlas entry. <see cref="Id"/> is deterministic (SHA-256 over
/// name|location) so a dive's <c>SiteId</c> stays stable across installs and app versions —
/// re-imported dives re-match onto the same id.
/// </summary>
public sealed record AtlasSite(
    Guid Id,
    string Name,
    string Location,
    double Lat,
    double Lon,
    string Fact);

/// <summary>
/// The on-device seed site atlas (serverless replacement for the removed server site DB + PostGIS
/// geo-search). Feeds dive-detail's "NEAR THIS SITE" carousel, the Sites tab's enrichment, and
/// <c>ImportService</c>'s auto-match. Matching is a bounding-box prefilter + in-memory haversine —
/// exactly the shape the rejected PostGIS backend was traded for. Read-only: the community atlas /
/// "N divers logged here" lines are bundled demo copy, not live community data.
/// </summary>
public sealed class SiteAtlas
{
    /// <summary>Metres per degree of latitude (spherical approximation, ample for a 2 km radius).</summary>
    private const double KmPerDegreeLat = 111.32;

    private static readonly IReadOnlyList<AtlasSite> Seed =
    [
        Entry("Liberty Wreck", "Tulamben", -8.2744, 115.5931,
            "The USAT Liberty wreck hosts 400+ reef species — bumpheads patrol the stern at dawn."),
        Entry("Coral Garden", "Tulamben", -8.2775, 115.5892,
            "Shallow reef with resident ribbon eels and a small artificial-reef temple."),
        Entry("Drop-off", "Amed", -8.3364, 115.6631,
            "A sheer wall from 5m to 40m+ — watch the current split at the corner."),
        Entry("Manta Point", "Nusa Penida", -8.7924, 115.5261,
            "Cleaning station for reef mantas — keep to the rubble line and let them come to you."),
        Entry("Crystal Bay", "Nusa Penida", -8.7145, 115.4585,
            "Mola mola season Jul–Oct; thermoclines can drop 5°C in seconds."),
        Entry("Blue Corner", "Palau", 7.1358, 134.2206,
            "Reef-hook territory — grey reef sharks stack up in the incoming current."),
        Entry("Batu Bolong", "Komodo", -8.5292, 119.5947,
            "A pinnacle in the strait; dive the lee side and spiral up with the fish tornado."),
        Entry("Castle Rock", "Komodo", -8.4488, 119.5378,
            "Submerged seamount — giant trevally hunt in the surge over the top plate."),
        Entry("Shark Point", "Phuket", 7.7477, 98.5164,
            "Three limestone pinnacles wrapped in soft coral — leopard sharks rest on the sand."),
        Entry("Richelieu Rock", "Andaman Sea", 9.3624, 98.0223,
            "A horseshoe pinnacle famous for whale-shark passes and purple soft coral."),
        Entry("Barracuda Point", "Sipadan", 4.1148, 118.6289,
            "The barracuda vortex forms over the ledge when the current pushes in."),
        Entry("Yolanda Reef", "Ras Mohammed", 27.7239, 34.2570,
            "Cargo of the Jolanda — bathtubs and pipes scattered along the reef plate."),
        Entry("Chac Mool", "Playa del Carmen", 20.5945, -87.1189,
            "Cavern light beams through the halocline — stay on the guideline at all times."),
        Entry("Punta Sur", "Cozumel", 20.2711, -87.0244,
            "Drift dive over the Devil's Throat swim-through — advanced, current-swept."),
    ];

    /// <summary>Every bundled site (read-only).</summary>
    public IReadOnlyList<AtlasSite> All => Seed;

    /// <summary>Resolves a dive's matched <c>SiteId</c> back to its atlas entry.</summary>
    public AtlasSite? GetById(Guid id) => Seed.FirstOrDefault(s => s.Id == id);

    /// <summary>Case-insensitive exact name match (secondary matcher next to the geo path).</summary>
    public AtlasSite? FindByName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Seed.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Nearest site within <paramref name="radiusKm"/> of the fix, or null. Bounding-box prefilter
    /// first (cheap degree math), haversine only on the survivors.
    /// </summary>
    public AtlasSite? FindNearest(double lat, double lon, double radiusKm)
    {
        var latDelta = radiusKm / KmPerDegreeLat;
        var lonScale = Math.Cos(lat * Math.PI / 180.0);
        // Near the poles the lon box degenerates; fall back to scanning all longitudes.
        var lonDelta = lonScale < 1e-6 ? 180.0 : radiusKm / (KmPerDegreeLat * lonScale);

        AtlasSite? best = null;
        var bestKm = double.MaxValue;
        foreach (var site in Seed)
        {
            if (Math.Abs(site.Lat - lat) > latDelta || Math.Abs(LonDiff(site.Lon, lon)) > lonDelta)
            {
                continue; // bounding-box prefilter
            }

            var km = HaversineKm(lat, lon, site.Lat, site.Lon);
            if (km <= radiusKm && km < bestKm)
            {
                best = site;
                bestKm = km;
            }
        }

        return best;
    }

    /// <summary>
    /// The three "NEAR THIS SITE" carousel cards for a site (prototype copy): the site fact, a
    /// deterministic conditions line, and a bundled community-atlas line. Deterministic per site —
    /// no randomness, no network.
    /// </summary>
    public IReadOnlyList<string> FactsFor(AtlasSite site)
    {
        var seed = SeedFor(site);
        var conditions = string.Create(
            CultureInfo.InvariantCulture,
            $"Avg viz {10 + seed % 15}m · {(seed % 2 == 1 ? "mild current" : "moderate current, dive at slack")} · best {(seed % 2 == 1 ? "at dawn" : "mid-morning")}.");
        var community = string.Create(
            CultureInfo.InvariantCulture,
            $"Community atlas · {120 + seed * 7} dives logged here by {30 + seed * 3} divers · updated this week.");
        return [site.Fact, conditions, community];
    }

    /// <summary>Header coordinates, prototype format: <c>8.274°S 115.593°E</c>.</summary>
    public static string CoordinatesLabel(AtlasSite site)
    {
        var latHemi = site.Lat >= 0 ? "N" : "S";
        var lonHemi = site.Lon >= 0 ? "E" : "W";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Abs(site.Lat):0.000}°{latHemi} {Math.Abs(site.Lon):0.000}°{lonHemi}");
    }

    /// <summary>Great-circle distance (spherical earth, R = 6371 km).</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusKm = 6371.0;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180.0;

    private static double LonDiff(double a, double b)
    {
        var d = a - b;
        while (d > 180)
        {
            d -= 360;
        }

        while (d < -180)
        {
            d += 360;
        }

        return d;
    }

    private static int SeedFor(AtlasSite site) =>
        (site.Name.Length * 13 + site.Location.Length * 31 + (int)Math.Abs(Math.Round(site.Lat))) % 89;

    private static AtlasSite Entry(string name, string location, double lat, double lon, string fact) =>
        new(DeterministicSiteId(name, location), name, location, lat, lon, fact);

    /// <summary>Stable site identity: a GUID derived from (name, location) — same across installs.</summary>
    internal static Guid DeterministicSiteId(string name, string location)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"trimix.site|{name}|{location}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
