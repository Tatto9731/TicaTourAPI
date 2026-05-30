namespace TicaTourAPI.DTOs;

public sealed class ExperienceListRow
{
    public string PublicCode { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceCurrency { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public decimal Rating { get; set; }
    public int Reviews { get; set; }
    public string? Difficulty { get; set; }
    public string? Image { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string? NextSlot { get; set; }
    public bool Promoted { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string CompanySlug { get; set; } = string.Empty;
    public bool CompanyVerified { get; set; }

    public string CategoryName { get; set; } = string.Empty;
    public string CategorySlug { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }
}