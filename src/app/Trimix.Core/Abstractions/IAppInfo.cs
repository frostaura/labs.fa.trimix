namespace Trimix.Core.Abstractions;

/// <summary>Static facts about the running app, stamped onto every analytics event.</summary>
public interface IAppInfo
{
    /// <summary>Store platform id, e.g. <c>android</c> / <c>ios</c> (or <c>dev</c> outside MAUI).</summary>
    string Platform { get; }

    /// <summary>Semantic app version, e.g. <c>0.1.0</c>.</summary>
    string AppVersion { get; }
}
