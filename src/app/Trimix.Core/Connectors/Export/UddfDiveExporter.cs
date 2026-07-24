using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Export;

/// <summary>
/// Exports a dive to UDDF 3.2.0 (Universal Dive Data Format, XML) — the broadest-interop open standard.
/// Built with <see cref="XDocument"/> so output is always well-formed. UDDF conventions: depth in metres,
/// dive time in seconds, temperature in <em>Kelvin</em>, gas fractions in [0,1]. Full-profile fidelity.
/// </summary>
public sealed class UddfDiveExporter : IDiveExporter
{
    /// <summary>The UDDF 3.2 XML namespace URI (public so exported documents can be asserted/parsed by callers).</summary>
    public const string Ns = "http://www.streit.cc/uddf/3.2/";
    private const double KelvinOffset = 273.15;

    public DiveExportFormat Format => DiveExportFormat.Uddf;

    public string ContentType => "application/x-uddf+xml";

    public string FileExtension => "uddf";

    public ProfileFidelity Fidelity => ProfileFidelity.FullProfile;

    public ExportedDive Export(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);
        XNamespace n = Ns;

        var gases = GasMixParser.Gases(dive);
        var mixElements = new List<XElement>(gases.Count);
        for (var i = 0; i < gases.Count; i++)
        {
            var g = gases[i];
            mixElements.Add(new XElement(n + "mix",
                new XAttribute("id", MixId(i)),
                new XElement(n + "name", GasName(g)),
                new XElement(n + "o2", Num(g.O2)),
                new XElement(n + "n2", Num(g.N2)),
                new XElement(n + "he", Num(g.He))));
        }

        var waypoints = new List<XElement>(dive.ProfileSamples.Count);
        foreach (var s in dive.ProfileSamples)
        {
            var wp = new XElement(n + "waypoint",
                new XElement(n + "depth", Num(s.DepthM)),
                new XElement(n + "divetime", Int(s.TSeconds)));
            if (s.TempC is { } tempC)
            {
                wp.Add(new XElement(n + "temperature", Num(tempC + KelvinOffset)));
            }

            if (s.TankPressureBar is { } bar)
            {
                // UDDF tankpressure is Pascal; 1 bar = 100000 Pa.
                wp.Add(new XElement(n + "tankpressure", Num(bar * 100000.0)));
            }

            waypoints.Add(wp);
        }

        var afterDive = new XElement(n + "informationafterdive",
            new XElement(n + "greatestdepth", Num(dive.MaxDepthM)),
            new XElement(n + "diveduration", Int(dive.DurationSec)));
        if (dive.AvgDepthM is { } avg)
        {
            afterDive.Add(new XElement(n + "averagedepth", Num(avg)));
        }

        if (dive.MinTempC is { } minTemp)
        {
            afterDive.Add(new XElement(n + "lowesttemperature", Num(minTemp + KelvinOffset)));
        }

        if (!string.IsNullOrWhiteSpace(dive.Notes))
        {
            afterDive.Add(new XElement(n + "notes", new XElement(n + "para", dive.Notes)));
        }

        var diveElement = new XElement(n + "dive",
            new XAttribute("id", "dive-" + dive.Id.ToString("N")),
            new XElement(n + "informationbeforedive",
                new XElement(n + "datetime", DiveTime(dive.StartUtc))),
            new XElement(n + "samples", waypoints),
            afterDive);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(n + "uddf",
                new XAttribute("version", "3.2.0"),
                new XElement(n + "generator",
                    new XElement(n + "name", "Trimix"),
                    new XElement(n + "manufacturer", new XElement(n + "name", "FrostAura")),
                    new XElement(n + "version", "1.0"),
                    new XElement(n + "datetime", DiveTime(dive.StartUtc))),
                new XElement(n + "gasdefinitions", mixElements),
                new XElement(n + "profiledata",
                    new XElement(n + "repetitiongroup",
                        new XAttribute("id", "rg-1"),
                        diveElement))));

        var bytes = Serialize(doc);
        var fileName = ExportNaming.FileName(dive, FileExtension);
        return new ExportedDive(Format, fileName, ContentType, bytes);
    }

    private static byte[] Serialize(XDocument doc)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            doc.Save(writer);
        }

        return stream.ToArray();
    }

    private static string MixId(int index) => "mix-" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static string GasName(GasFraction g)
    {
        var o2 = (int)Math.Round(g.O2 * 100.0);
        var he = (int)Math.Round(g.He * 100.0);
        return (o2, he) switch
        {
            (_, > 0) => $"{o2}/{he}",
            (21, _) => "Air",
            _ => $"EAN{o2}",
        };
    }

    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string DiveTime(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}
