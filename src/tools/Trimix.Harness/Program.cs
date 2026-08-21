using System.Text.Json;
using Trimix.Harness;

// Dev harness: drives the reverse-engineered Garmin Dive flow against the live API so request/response
// shapes can be recorded before any of it is folded into Trimix.Core. Read-only — it only ever GETs dives.
//
//   GARMIN_USERNAME / GARMIN_PASSWORD  credentials (raw-login attempt)
//   GARMIN_TICKET                      a fresh ST-…-sso to skip the Cloudflare-guarded login leg
//   TRIMIX_PROXY                       e.g. http://127.0.0.1:8080 to watch this run through mitmproxy
//   TRIMIX_DUMP                        output directory (default ./dump)

var username = Environment.GetEnvironmentVariable("GARMIN_USERNAME");
var password = Environment.GetEnvironmentVariable("GARMIN_PASSWORD");
var ticket = Environment.GetEnvironmentVariable("GARMIN_TICKET");
var proxy = Environment.GetEnvironmentVariable("TRIMIX_PROXY");
var dumpDir = Environment.GetEnvironmentVariable("TRIMIX_DUMP")
              ?? Path.Combine(AppContext.BaseDirectory, "dump");

Directory.CreateDirectory(dumpDir);
using var probe = new GarminDiveProbe(proxy);

void Report(ProbeStep step)
{
    var verdict = step.Ok ? "OK " : "ERR";
    Console.WriteLine($"[{verdict}] {step.Status,3}  {step.What}");
    if (step.Note is not null)
    {
        Console.WriteLine($"        note: {step.Note}");
    }
}

void Dump(string name, string content)
{
    var path = Path.Combine(dumpDir, name);
    File.WriteAllText(path, content);
    Console.WriteLine($"        dumped {content.Length,8} chars -> {path}");
}

var mode = Environment.GetEnvironmentVariable("TRIMIX_MODE");

// TRIMIX_MODE=scubapro drives the real ScubaPro/LogTRAK (Firebase) connector.
if (string.Equals(mode, "scubapro", StringComparison.OrdinalIgnoreCase))
{
    var spUser = Environment.GetEnvironmentVariable("LOGTRAK_USERNAME") ?? username;
    var spPass = Environment.GetEnvironmentVariable("LOGTRAK_PASSWORD") ?? password;
    if (string.IsNullOrWhiteSpace(spUser) || string.IsNullOrWhiteSpace(spPass))
    {
        Console.Error.WriteLine("ScubaPro mode needs LOGTRAK_USERNAME + LOGTRAK_PASSWORD.");
        return 2;
    }

    Console.WriteLine("Trimix.Core ScubaProCloudSourceConnector (LogTRAK / Firebase) — live run");
    Console.WriteLine(new string('-', 78));
    return await ConnectorRun.ExecuteScubaProAsync(spUser, spPass);
}

// TRIMIX_MODE=connector drives the real Trimix.Core Garmin connector; default is raw endpoint discovery.
if (string.Equals(mode, "connector", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("Connector mode needs GARMIN_USERNAME + GARMIN_PASSWORD.");
        return 2;
    }

    Console.WriteLine("Trimix.Core GarminCloudSourceConnector — live run");
    Console.WriteLine(new string('-', 78));
    return await ConnectorRun.ExecuteAsync(username, password);
}

Console.WriteLine($"Garmin Dive probe — proxy: {proxy ?? "(direct)"}");
Console.WriteLine(new string('-', 78));

// ---------------------------------------------------------------- 1. get a service ticket
if (string.IsNullOrWhiteSpace(ticket))
{
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("Set GARMIN_USERNAME + GARMIN_PASSWORD, or GARMIN_TICKET.");
        return 2;
    }

    Console.WriteLine("STEP 1 — raw login (expected to be Cloudflare-blocked; proving it either way)");
    var login = await probe.TryRawLoginAsync(username, password);
    Report(login);
    Dump("01-login-response.txt", login.Body);

    // Field name is unknown until we see one succeed, so try every plausible spelling.
    ticket = login.JsonString("serviceTicketId", "serviceTicket", "ticket", "st", "ticketId");
    if (string.IsNullOrWhiteSpace(ticket))
    {
        var found = System.Text.RegularExpressions.Regex.Match(login.Body, @"ST-[A-Za-z0-9\-_]+");
        ticket = found.Success ? found.Value : null;
    }

    if (string.IsNullOrWhiteSpace(ticket))
    {
        Console.WriteLine();
        Console.WriteLine("No service ticket obtained — the WebView path is required.");
        Console.WriteLine("Capture a fresh ST-…-sso and re-run with GARMIN_TICKET=<ticket>.");
        Console.WriteLine("Tickets are single-use and short-lived, so it must be fresh.");
        return 1;
    }

    Console.WriteLine($"        service ticket: {ticket[..Math.Min(18, ticket.Length)]}…");
}
else
{
    Console.WriteLine($"STEP 1 — skipped, using supplied GARMIN_TICKET ({ticket[..Math.Min(18, ticket.Length)]}…)");
}

// ---------------------------------------------------------------- 2. ticket -> bearer JWT
Console.WriteLine();
Console.WriteLine("STEP 2 — exchange service ticket for bearer JWT");
var token = await probe.ExchangeTicketAsync(ticket);
Report(token);
Dump("02-token-response.json", token.Body);

var jwt = token.JsonString("access_token", "accessToken");
if (string.IsNullOrWhiteSpace(jwt))
{
    Console.Error.WriteLine("No access_token in the exchange response — see 02-token-response.json.");
    return 1;
}

Console.WriteLine($"        JWT acquired ({jwt.Length} chars)");

// ---------------------------------------------------------------- 3. read dives (read-only)
Console.WriteLine();
Console.WriteLine("STEP 3 — read the logbook");

var endpoints = new (string Name, string Path)[]
{
    ("03-dive-summary.json", "/diving/v1/dive/summary?requestedPage=0&resultsPerPage=100"),
    ("04-dive-tags.json", "/diving/v1/dive/tags"),
};

string? summaryBody = null;
foreach (var (name, path) in endpoints)
{
    var step = await probe.GetAsync(path, jwt);
    Report(step);
    Dump(name, step.Body);
    if (name.Contains("summary", StringComparison.Ordinal) && step.Ok)
    {
        summaryBody = step.Body;
    }
}

// Pull dive ids out of the summary and batch-fetch their detail, exactly as the app does.
if (summaryBody is not null)
{
    // Shape confirmed live: { totalCount, pageSize, pageNumber, diveActivities: [ { id, … } ] }.
    var ids = new List<string>();
    try
    {
        using var doc = JsonDocument.Parse(summaryBody);
        if (doc.RootElement.TryGetProperty("diveActivities", out var activities)
            && activities.ValueKind == JsonValueKind.Array)
        {
            foreach (var dive in activities.EnumerateArray())
            {
                if (dive.TryGetProperty("id", out var idEl))
                {
                    ids.Add(idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString() ?? "");
                }
            }
        }

        ids.RemoveAll(string.IsNullOrWhiteSpace);
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"        could not parse summary for ids: {ex.Message}");
    }

    var distinct = ids.Distinct().Take(25).ToList();
    Console.WriteLine($"        dive ids found: {ids.Distinct().Count()} (fetching detail for {distinct.Count})");

    if (distinct.Count > 0)
    {
        var query = string.Join("&", distinct.Select(id => $"diveActivityId={Uri.EscapeDataString(id)}"));
        var detail = await probe.GetAsync($"/diving/v1/dive/thin/detail/bulk?{query}", jwt);
        Report(detail);
        Dump("05-dive-detail-bulk.json", detail.Body);

        // "thin" implies a fat variant. The per-sample depth/temp profile is what gives Trimix
        // FullProfile fidelity, so probe for where it lives. 404s here are findings, not failures.
        Console.WriteLine();
        Console.WriteLine($"STEP 4 — probe for the full sample profile (dive {distinct[0]})");
        var candidates = new (string Name, string Path)[]
        {
            ("06-detail-single.json", $"/diving/v1/dive/detail/{distinct[0]}"),
            ("07-dive-by-id.json", $"/diving/v1/dive/{distinct[0]}"),
            ("08-detail-bulk-fat.json", $"/diving/v1/dive/detail/bulk?diveActivityId={distinct[0]}"),
            ("09-samples.json", $"/diving/v1/dive/{distinct[0]}/samples"),
        };

        foreach (var (name, path) in candidates)
        {
            var step = await probe.GetAsync(path, jwt);
            Report(step);
            if (step.Ok)
            {
                Dump(name, step.Body);
            }
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Done. Raw responses in {dumpDir}");
return 0;
