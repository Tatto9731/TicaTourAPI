namespace TicaTourAPI.DTOs.Company;

public sealed class CreatePickupStopRequest
{
    public string Place { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int SortOrder { get; set; } = 0;
}