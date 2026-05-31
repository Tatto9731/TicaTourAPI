namespace TicaTourAPI.DTOs.Reviews;

public sealed class CreateReviewRequest
{
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
}