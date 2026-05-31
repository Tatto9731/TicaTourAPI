namespace TicaTourAPI.DTOs.Leads;

public sealed class UpdateLeadStatusRequest
{
    public string Status { get; set; } = string.Empty; // New | Contacted | Confirmed | Cancelled
}