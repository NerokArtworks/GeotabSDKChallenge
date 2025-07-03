using Geotab.Checkmate.ObjectModel;
using System.Globalization;

public class VehicleWithOdometer
{
    public Id? Id { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? Vin { get; set; } = String.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Odometer { get; set; }
    public string CsvHead { get; } = "Id,Timestamp,VIN,Latitude,Longitude,Odometer";
    public string ToCsv()
    {
        var invariant = CultureInfo.InvariantCulture;

        return $"{Id},{Timestamp?.ToString("o", invariant)},{Vin}," +
               $"{Latitude?.ToString(invariant)}," +
               $"{Longitude?.ToString(invariant)}," +
               $"{GetParsedOdometer()?.ToString(invariant)}";
    }

    private double? GetParsedOdometer()
    {
        if (Odometer == null) return null;

        return Math.Round(
            RegionInfo.CurrentRegion.IsMetric
                ? (Odometer.Value / 1000)
                : Distance.ToImperial(Odometer.Value / 1000),
            0);
    }
}