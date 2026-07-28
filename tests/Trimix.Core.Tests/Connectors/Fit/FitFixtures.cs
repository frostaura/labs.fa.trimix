using System.Runtime.CompilerServices;
using Trimix.Core.Connectors.Fit;

namespace Trimix.Core.Tests.Connectors.Fit;

/// <summary>
/// Loads the committed, deterministic FIT fixture that sits next to this source file (no network, no build
/// copy step — resolved from the source tree via <see cref="CallerFilePathAttribute"/>). Retained from the
/// removed file-import branch because the FIT decode chain survives as the cloud connectors' parser.
/// </summary>
internal static class FitFixtures
{
    public static PickedFile Load(string fileName) =>
        new(fileName, File.ReadAllBytes(Path.Combine(FixturesDir(), fileName)));

    public static PickedFile Fit => Load("sample.fit");

    private static string FixturesDir([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures");
}
