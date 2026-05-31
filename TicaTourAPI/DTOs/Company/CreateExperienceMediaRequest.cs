namespace TicaTourAPI.DTOs.Company;

public sealed class CreateExperienceMediaRequest
{
    public string Type { get; set; } = string.Empty; // image | video
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public string? AltText { get; set; }
}