using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Stats;

/// <summary>
/// One physical dive can land in the logbook from several sources (BLE + HealthKit + a FIT file),
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
    /// <summary>Two dives may be the same physical dive only if their starts are within this many seconds.</summary>
    public const double StartToleranceSeconds = 90;

    /// <summary>…and their durations are within this many seconds.</summary>
    public const double DurationToleranceSeconds = 90;

    /// <summary>…and their max depths agree within this many metres.</summary>
    public const double MaxDepthToleranceMeters = 1.5;

    /// <summary>Vendor / source token → friendly badge label. Case-insensitive.</summary>
    private static readonly IReadOnlyDictionary<string, string> VendorLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["shearwater"] = "Shearwater",
            ["apple_health"] = "Apple Watch",
            ["garmin_fit"] = "Garmin",
            ["garmin"] = "Garmin",
            ["suunto_json"] = "Suunto",
            ["suunto"] = "Suunto",
            ["uddf"] = "UDDF",
            ["dan_dl7"] = "DAN DL7",
            ["file_import"] = "File",
            ["mares"] = "Mares",
            ["scubapro"] = "ScubaPro",
            ["aqualung"] = "Aqualung",
            ["manual"] = "Manual",
        };

    /// <summary>
    /// The §3 heuristic: same physical dive iff start, duration, and max depth all agree within their
    /// tolerances. Symmetric.
    /// </summary>
    public static bool IsSamePhysicalDive(DiveDto a, DiveDto b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var dStart = Math.Abs((a.StartUtc - b.StartUtc).TotalSeconds);
        var dDuration = Math.Abs((double)(a.DurationSec - b.DurationSec));
        var dDepth = Math.Abs(a.MaxDepthM - b.MaxDepthM);
        return dStart <= StartToleranceSeconds
            && dDuration <= DurationToleranceSeconds
            && dDepth <= MaxDepthToleranceMeters;
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

        var badges = cluster
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
