namespace Trimix.Core.Abstractions;

/// <summary>Navigation abstraction over Shell routes so viewmodels stay MAUI-free.</summary>
public interface INavigator
{
    Task GoToAsync(string route, IReadOnlyDictionary<string, object>? parameters = null);

    Task GoBackAsync();
}
