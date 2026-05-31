namespace TicaTourAPI.DTOs.Categories;

public sealed class UpdateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
}