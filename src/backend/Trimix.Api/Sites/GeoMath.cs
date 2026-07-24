namespace Trimix.Api.Sites;

/// <summary>
/// Geo without PostGIS (architecture overview §5): SQL-translatable bounding-box prefilter, then
/// haversine ranking in memory on the prefiltered set. PostGIS upgrade is post-slice.
/// </summary>
public static class GeoMath
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Great-circle distance between two coordinates in kilometers.</summary>
    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>
    /// Bounding box around a point: ± <paramref name="latDelta"/> degrees of latitude, with the
    /// longitude delta widened by 1/cos(lat) so the box stays roughly square in kilometers.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLng, double MaxLng) BoundingBox(
        double lat, double lng, double latDelta)
    {
        var cosLat = Math.Max(Math.Cos(ToRadians(lat)), 0.1);
        var lngDelta = latDelta / cosLat;
        return (lat - latDelta, lat + latDelta, lng - lngDelta, lng + lngDelta);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
