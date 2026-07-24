namespace Trimix.Contracts.Dtos;

// Passkey (WebAuthn/FIDO2) auth is the ONLY account sign-in method (no email/password, no OAuth).
// The WebAuthn creation/request options and the authenticator's attestation/assertion responses are
// carried as opaque JSON strings (the platform authenticator + the server's FIDO2 relying party each
// speak WebAuthn JSON), so the app never has to model the credential internals. A short-lived
// server-side challenge is keyed by ChallengeId across the begin→finish round-trip.

/// <summary><c>POST /auth/passkey/register/begin</c> — start creating a passkey. DisplayName labels the
/// credential in the platform's passkey manager (optional; a default is used when omitted).</summary>
public sealed record PasskeyRegisterBeginRequest(string? DisplayName);

/// <summary>Server's WebAuthn <c>PublicKeyCredentialCreationOptions</c> (JSON) + the challenge handle.</summary>
public sealed record PasskeyRegisterBeginResponse(string ChallengeId, string CreationOptionsJson);

/// <summary><c>POST /auth/passkey/register/finish</c> — the authenticator's attestation response (JSON).</summary>
public sealed record PasskeyRegisterFinishRequest(string ChallengeId, string AttestationResponseJson);

/// <summary><c>POST /auth/passkey/login/begin</c> — start a usernameless (discoverable) passkey assertion.</summary>
public sealed record PasskeyLoginBeginResponse(string ChallengeId, string RequestOptionsJson);

/// <summary><c>POST /auth/passkey/login/finish</c> — the authenticator's assertion response (JSON).</summary>
public sealed record PasskeyLoginFinishRequest(string ChallengeId, string AssertionResponseJson);
