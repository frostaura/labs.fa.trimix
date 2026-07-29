namespace Trimix.Core.Connectors.Demo;

/// <summary>
/// Marker for the DEBUG-only demo connectors (<see cref="Abstractions.DemoModeState"/>). The provider
/// sign-in sheet uses it to short-circuit the ToS-risk disclosure and the credential exchange —
/// demo auth is instantly Granted, no consent ceremony, no network. Real connectors never implement
/// this; the demo family is additionally marked by the <c>demo_</c> connector-id prefix.
/// </summary>
public interface IDemoConnector;
