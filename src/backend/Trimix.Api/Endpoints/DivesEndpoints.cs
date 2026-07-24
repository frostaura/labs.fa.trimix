using Microsoft.EntityFrameworkCore;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// POST /dives/sync (architecture overview §6): batch upsert of client-GUID dives with
/// last-write-wins on <c>UpdatedAtUtc</c>, soft deletes, and server changes since the client's
/// cursor in the response. Profile samples travel with their dive.
/// </summary>
public static class DivesEndpoints
{
    public static RouteGroupBuilder MapDivesEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Dives.Group);

        group.MapPost(ApiRoutes.Dives.Sync, async (
            DiveSyncRequest request,
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();
            var now = DateTime.UtcNow;

            foreach (var dto in request.Dives ?? [])
            {
                if (OversizedText(dto) is { } error)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: error);
                }
            }

            // Ids whose client write won (created/updated/deleted here): excluded from the response —
            // the client already has that state. Ids where the server version won ARE returned so the
            // client converges.
            var clientWon = new HashSet<Guid>();

            foreach (var dto in request.Dives ?? [])
            {
                var existing = await db.Dives
                    .Include(d => d.ProfileSamples)
                    .Include(d => d.Cylinders)
                    .SingleOrDefaultAsync(d => d.Id == dto.Id, cancellationToken);

                if (existing is null)
                {
                    db.Dives.Add(ToEntity(dto, userId, now));
                    clientWon.Add(dto.Id);
                }
                else if (existing.UserId != userId)
                {
                    // Client GUID collides with another user's dive — never touch foreign rows.
                    continue;
                }
                else if (dto.UpdatedAtUtc > existing.UpdatedAtUtc)
                {
                    Apply(dto, existing);
                    existing.ServerUpdatedAtUtc = now; // Server-stamped cursor time.
                    clientWon.Add(dto.Id);
                }
                // else: server version is newer or equal — last write wins, server row stands and is
                // returned below when it is within the client's cursor window.
            }

            foreach (var id in request.DeletedIds ?? [])
            {
                var existing = await db.Dives.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
                if (existing is null || existing.UserId != userId || existing.Deleted)
                {
                    continue;
                }

                existing.Deleted = true;
                existing.UpdatedAtUtc = now;
                existing.ServerUpdatedAtUtc = now;
                clientWon.Add(id);
            }

            await db.SaveChangesAsync(cancellationToken);

            // Cursor is the server-stamped column, NOT the client clock: a change is returned once its
            // server-receive time is past the caller's cursor, immune to client-vs-server clock skew.
            var since = AsUtc(request.Since ?? DateTime.MinValue);
            var changed = await db.Dives
                .Include(d => d.ProfileSamples)
                .Include(d => d.Cylinders)
                .Where(d => d.UserId == userId && d.ServerUpdatedAtUtc > since)
                .OrderBy(d => d.ServerUpdatedAtUtc)
                .ToListAsync(cancellationToken);

            var dives = changed
                .Where(d => !d.Deleted && !clientWon.Contains(d.Id))
                .Select(ToDto)
                .ToList();
            var deletedIds = changed
                .Where(d => d.Deleted && !clientWon.Contains(d.Id))
                .Select(d => d.Id)
                .ToList();

            return Results.Ok(new DiveSyncResponse(dives, deletedIds, now));
        }).RequireAuthorization();

        return api;
    }

    private static Dive ToEntity(DiveDto dto, Guid userId, DateTime serverNow)
    {
        var dive = new Dive { Id = dto.Id, UserId = userId, ServerUpdatedAtUtc = serverNow };
        Apply(dto, dive);
        return dive;
    }

    /// <summary>Rejects oversized user free-text before it can bloat the store; null when within caps.</summary>
    private static string? OversizedText(DiveDto dto)
    {
        if (dto.Notes is { Length: > AppDbContext.DiveNotesMaxLength })
        {
            return $"Dive notes exceed the {AppDbContext.DiveNotesMaxLength}-character limit.";
        }

        if (dto.ConditionsJson is { Length: > AppDbContext.DiveConditionsMaxLength })
        {
            return $"Dive conditions exceed the {AppDbContext.DiveConditionsMaxLength}-character limit.";
        }

        if (dto.GearCsv is { Length: > AppDbContext.DiveTextMaxLength }
            || dto.BuddiesCsv is { Length: > AppDbContext.DiveTextMaxLength })
        {
            return $"Dive gear/buddies text exceeds the {AppDbContext.DiveTextMaxLength}-character limit.";
        }

        return null;
    }

    /// <summary>Npgsql's <c>timestamptz</c> only accepts Kind=Utc; wire timestamps are UTC by contract.</summary>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static void Apply(DiveDto dto, Dive dive)
    {
        dive.StartUtc = AsUtc(dto.StartUtc);
        dive.DurationSec = dto.DurationSec;
        dive.MaxDepthM = dto.MaxDepthM;
        dive.AvgDepthM = dto.AvgDepthM;
        dive.MinTempC = dto.MinTempC;
        dive.GasMix = dto.GasMix;
        dive.Source = dto.Source;
        dive.SourceVendor = dto.SourceVendor;
        dive.SiteId = dto.SiteId;
        dive.ConditionsJson = dto.ConditionsJson;
        dive.GearCsv = dto.GearCsv;
        dive.BuddiesCsv = dto.BuddiesCsv;
        dive.Notes = dto.Notes;
        dive.Rating = dto.Rating;
        dive.Visibility = dto.Visibility;
        dive.Current = dto.Current;
        dive.Weather = dto.Weather;
        dive.EntryType = dto.EntryType;
        dive.WaterType = dto.WaterType;
        dive.ExposureSuit = dto.ExposureSuit;
        dive.WeightKg = dto.WeightKg;
        dive.UpdatedAtUtc = AsUtc(dto.UpdatedAtUtc);
        dive.Deleted = dto.Deleted;
        dive.ProfileSamples.Clear();
        dive.ProfileSamples.AddRange((dto.ProfileSamples ?? []).Select(s => new ProfileSample
        {
            DiveId = dto.Id,
            TSeconds = s.TSeconds,
            DepthM = s.DepthM,
            TempC = s.TempC,
            PpO2 = s.PpO2,
            Ndl = s.Ndl,
            CeilingM = s.CeilingM,
            AscentRateMPerMin = s.AscentRateMPerMin,
            Cns = s.Cns,
            TankPressureBar = s.TankPressureBar,
            Setpoint = s.Setpoint,
            HeartRate = s.HeartRate,
        }));
        dive.Cylinders.Clear();
        dive.Cylinders.AddRange((dto.Cylinders ?? []).Select((c, i) => new Cylinder
        {
            Id = Guid.NewGuid(),
            DiveId = dto.Id,
            Ordinal = i,
            O2 = c.O2,
            He = c.He,
            StartBar = c.StartBar,
            EndBar = c.EndBar,
            VolumeL = c.VolumeL,
            WorkingPressureBar = c.WorkingPressureBar,
        }));
    }

    private static DiveDto ToDto(Dive dive) => DiveMapping.ToDto(dive);
}
