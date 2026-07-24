using System.Runtime.CompilerServices;
using Trimix.Core.Connectors.FileImport;

namespace Trimix.Core.Tests.Connectors.FileImport;

/// <summary>
/// Loads the committed, deterministic fixture files that sit next to this source file (no network, no build
/// copy step — resolved from the source tree via <see cref="CallerFilePathAttribute"/>). The fixtures are
/// real-shape samples of each supported dive-log format.
/// </summary>
internal static class FileImportFixtures
{
    public static PickedFile Load(string fileName) =>
        new(fileName, File.ReadAllBytes(Path.Combine(FixturesDir(), fileName)));

    public static PickedFile Fit => Load("sample.fit");

    public static PickedFile Uddf => Load("sample.uddf");

    public static PickedFile Dl7 => Load("sample.dl7");

    public static PickedFile SuuntoJson => Load("suunto-sample.json");

    private static string FixturesDir([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}

/// <summary>Test <see cref="IFilePicker"/> that returns a preset file (or null to simulate a cancelled pick).</summary>
internal sealed class FakeFilePicker(PickedFile? file) : IFilePicker
{
    public int PickCalls { get; private set; }

    public Task<PickedFile?> PickDiveFileAsync(CancellationToken cancellationToken = default)
    {
        PickCalls++;
        return Task.FromResult(file);
    }
}
