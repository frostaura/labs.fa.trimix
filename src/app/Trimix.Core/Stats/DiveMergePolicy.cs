using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Stats;

/// <summary>
/// One physical dive can land in the logbook from several provider sources,
/// each keyed to a distinct deterministic id, so the raw store holds duplicates. This policy is the
/// single home for the cross-source identity rule (aggregator architecture §3 / §8.2) and its
/// tolerance constants — nothing else redefines them.
///
/// <para>Merge is a <b>presentation-layer union</b>, never a destructive row rewrite: underlying rows
/// are retained on <see cref="MergedDive.Sources"/>; the logbook shows one entry badged
/// "sources: Shearwater + Apple Watch" with richest-fidelity-per-field wins.</para>
/// </summary>
public static class DiveMergePolicy
{
    /// <summary>
    /// Padding added to each end of a dive's time window before testing for overlap, as a fraction of the
    /// <b>geometric mean</b> of the two dives' durations (√(durA·durB) — the root of their product, which
    /// keeps the margin in seconds and stays symmetric). Two computers on the same dive can start and stop
    /// minutes apart — up to ~10 minutes — so the margin is deliberately generous and grows with dive
    /// length. At 0.30 a typical 30–60 min dive lands at the 10-minute ceiling; a short bounce dive gets
    /// only a couple of minutes. Clamped by <see cref="OverlapPadFloorSeconds"/> /
    /// <see cref="OverlapPadCeilingSeconds"/> — the ceiling stops a large pad from bridging a short surface
    /// interval and merging two genuinely separate repetitive dives.
    /// </summary>
    public const double OverlapPadFraction = 0.30;

    /// <summary>Lower clamp on the overlap padding (seconds) — even short dives get this much slack.</summary>
    public const double OverlapPadFloorSeconds = 120;

    /// <summary>Upper clamp on the overlap padding (seconds) — a computer pair can be ~10 min apart.</summary>
    public const double OverlapPadCeilingSeconds = 600;

    /// <summary>Vendor / source token → friendly badge label. Case-insensitive.</summary>
    private static readonly IReadOnlyDictionary<string, string> VendorLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["shearwater"] = "Shearwater",
            ["apple_health"] = "Apple Watch",
            ["garmin_fit"] = "Garmin",
            ["garmin"] = "Garmin",
            // Live cloud-connector ids (== DiveDto.SourceVendor for synced dives).
            ["garmin_connect"] = "Garmin",
            ["suunto_cloud"] = "Suunto",
            ["oceanic_cloud"] = "Oceanic+",
            ["suunto_json"] = "Suunto",
            ["suunto"] = "Suunto",
            ["uddf"] = "UDDF",
            ["dan_dl7"] = "DAN DL7",
            ["file_import"] = "File",
            ["mares"] = "Mares",
            ["scubapro"] = "ScubaPro",
            ["manual"] = "Manual",
        };

    /// <summary>
    /// The §3 heuristic, time-interval based: same physical dive iff the two dives' time windows overlap
    /// once each end is padded (<see cref="OverlapPadFraction"/> of the shorter dive, clamped to
    /// [<see cref="OverlapPadFloorSeconds"/>, <see cref="OverlapPadCeilingSeconds"/>]). You cannot be on
    /// two dives at once, so overlapping windows is the decisive signal; the padding only absorbs
    /// inter-device clock/detection skew. Depth is deliberately not part of the match (it still feeds
    /// <see cref="FidelityScore"/>). Symmetric.
    /// </summary>
    public static bool IsSamePhysicalDive(DiveDto a, DiveDto b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var startA = a.StartUtc;
        var endA = a.StartUtc.AddSeconds(Math.Max(0, a.DurationSec));
        var startB = b.StartUtc;
        var endB = b.StartUtc.AddSeconds(Math.Max(0, b.DurationSec));

        var pad = TimeSpan.FromSeconds(Math.Clamp(
            OverlapPadFraction * Math.Min(Math.Max(0, a.DurationSec), Math.Max(0, b.DurationSec)),
            OverlapPadFloorSeconds,
            OverlapPadCeilingSeconds));

        // Padded-interval overlap: A starts before B ends (+pad) AND B starts before A ends (+pad).
        return startA < endB + pad && startB < endA + pad;
    }

    /// <summary>
    /// Empirical fidelity rank used to break "both non-null" ties and to pick the samples/cylinders
    /// contributor: one point per distinct non-null rich sample channel present, plus one each for
    /// having samples at all, cylinders, and a gas-mix string. A <c>FullProfile</c> source therefore
    /// out-ranks a depth-temp-only source with zero dependency on the connector layer. The integrator
    /// may pass its own delegate (e.g. driven by the connector <c>ProfileFidelity</c>) to <see cref="Merge"/>.
    /// </summary>
    public static int FidelityScore(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);
        var score = 0;
        var samples = dive.ProfileSamples;
        if (samples.Count > 0)
        {
            score++;
        }

        bool temp = false, ppo2 = false, ndl = false, ceiling = false, ascent = false,
             cns = false, tank = false, setpoint = false, hr = false;
        foreach (var s in samples)
        {
            temp |= s.TempC is not null;
            ppo2 |= s.PpO2 is not null;
            ndl |= s.Ndl is not null;
            ceiling |= s.CeilingM is not null;
            ascent |= s.AscentRateMPerMin is not null;
            cns |= s.Cns is not null;
            tank |= s.TankPressureBar is not null;
            setpoint |= s.Setpoint is not null;
            hr |= s.HeartRate is not null;
        }

        foreach (var present in new[] { temp, ppo2, ndl, ceiling, ascent, cns, tank, setpoint, hr })
        {
            if (present)
            {
                score++;
            }
        }

        if (dive.Cylinders is { Count: > 0 })
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(dive.GasMix))
        {
            score++;
        }

        return score;
    }

    /// <summary>
    /// Cluster the dives by physical identity and return one <see cref="MergedDive"/> per cluster.
    /// Deterministic and non-destructive: sort by <c>StartUtc</c>; a dive joins the first cluster whose
    /// anchor (the dive that opened it) it matches, else it opens a new cluster. Matching against the
    /// fixed anchor (not the last-added member) prevents transitive drift/chaining.
    /// </summary>
    public static IReadOnlyList<MergedDive> Merge(
        IReadOnlyList<DiveDto> dives, Func<DiveDto, int>? fidelity = null)
    {
        ArgumentNullException.ThrowIfNull(dives);
        var fid = fidelity ?? FidelityScore;
        var ordered = dives.OrderBy(d => d.StartUtc).ToList();

        var clusters = new List<List<DiveDto>>();
        var anchors = new List<DiveDto>();
        foreach (var dive in ordered)
        {
            var matched = -1;
            for (var i = 0; i < clusters.Count; i++)
            {
                if (IsSamePhysicalDive(dive, anchors[i]))
                {
                    matched = i;
                    break;
                }
            }

            if (matched < 0)
            {
                clusters.Add([dive]);
                anchors.Add(dive);
            }
            else
            {
                clusters[matched].Add(dive);
            }
        }

        var result = new List<MergedDive>(clusters.Count);
        foreach (var cluster in clusters)
        {
            result.Add(BuildMerged(cluster, fid));
        }

        return result;
    }

    private static MergedDive BuildMerged(List<DiveDto> cluster, Func<DiveDto, int> fid)
    {
        // Stable: OrderByDescending keeps insertion (start-time) order for equal fidelity.
        var ranked = cluster.OrderByDescending(fid).ToList();
        var top = ranked[0];

        // Union: samples/cylinders come wholesale from the top-fidelity dive (already carried by `top`);
        // every scalar is filled from the highest-fidelity source that has it non-null.
        var primary = top with
        {
            AvgDepthM = PickStruct(ranked, d => d.AvgDepthM),
            MinTempC = PickStruct(ranked, d => d.MinTempC),
            GasMix = PickString(ranked, d => d.GasMix),
            SiteId = PickStruct(ranked, d => d.SiteId),
            ConditionsJson = PickString(ranked, d => d.ConditionsJson),
            GearCsv = PickString(ranked, d => d.GearCsv),
            BuddiesCsv = PickString(ranked, d => d.BuddiesCsv),
            Notes = PickString(ranked, d => d.Notes),
            Rating = PickStruct(ranked, d => d.Rating),
            Visibility = PickString(ranked, d => d.Visibility),
            Current = PickString(ranked, d => d.Current),
            Weather = PickString(ranked, d => d.Weather),
            EntryType = PickString(ranked, d => d.EntryType),
            WaterType = PickString(ranked, d => d.WaterType),
            ExposureSuit = PickString(ranked, d => d.ExposureSuit),
            WeightKg = PickStruct(ranked, d => d.WeightKg),
            UpdatedAtUtc = cluster.Max(d => d.UpdatedAtUtc),
        };

        // Badges follow the same ranking that picked the primary, so the primary provider's badge is first
        // and the rest follow priority/fidelity order (stable within equal rank = start-time order).
        var badges = ranked
            .Select(d => new DiveSourceBadge(d.Source, d.SourceVendor, BadgeLabel(d)))
            .DistinctBy(b => (b.Source, b.Vendor ?? string.Empty))
            .ToList();

        return new MergedDive(primary, cluster, badges);
    }

    private static string? PickString(IEnumerable<DiveDto> ranked, Func<DiveDto, string?> select)
    {
        foreach (var dive in ranked)
        {
            var value = select(dive);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static T? PickStruct<T>(IEnumerable<DiveDto> ranked, Func<DiveDto, T?> select)
        where T : struct
    {
        foreach (var dive in ranked)
        {
            if (select(dive) is T value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Friendly badge label for a dive's source/vendor. See <see cref="VendorLabels"/>.</summary>
    public static string BadgeLabel(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);
        if (!string.IsNullOrEmpty(dive.SourceVendor))
        {
            return VendorLabels.TryGetValue(dive.SourceVendor, out var label)
                ? label
                : TitleCase(dive.SourceVendor);
        }

        return string.Equals(dive.Source, DiveSources.Manual, StringComparison.OrdinalIgnoreCase)
            ? "Manual"
            : "Import";
    }

    private static string TitleCase(string token)
    {
        var cleaned = token.Replace('_', ' ').Trim();
        if (cleaned.Length == 0)
        {
            return "Import";
        }

        return string.Join(' ', cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}

/// <summary>
/// A presentation-layer union of the dives judged to be one physical dive. <see cref="Primary"/> is the
/// synthesized richest-per-field view whose <c>Id</c> is the top-fidelity contributor's real id (so
/// dive-detail navigation still resolves); <see cref="Sources"/> retains every underlying row for
/// provenance; <see cref="Badges"/> lists the distinct contributing sources.
/// </summary>
public sealed record MergedDive(
    DiveDto Primary,
    IReadOnlyList<DiveDto> Sources,
    IReadOnlyList<DiveSourceBadge> Badges);

/// <summary>One contributing source of a <see cref="MergedDive"/>: its raw source, vendor, and friendly label.</summary>
public sealed record DiveSourceBadge(string Source, string? Vendor, string Label);
