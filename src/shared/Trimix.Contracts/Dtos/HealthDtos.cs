namespace Trimix.Contracts.Dtos;

/// <summary><c>GET /healthz</c> response: liveness + db check.</summary>
public sealed record HealthzResponse(string Status, string Db);
