namespace TicaTourAPI.DTOs.Bookings;

public sealed class CreateBookingRequest
{
    public string ExperienceSlug { get; set; } = string.Empty;
    public int GuestsAdults { get; set; } = 1;
    public int GuestsChildren { get; set; } = 0;
    public DateTime? BookingDate { get; set; }
    public string SlotLabel { get; set; } = string.Empty;
    public string? MeetingPoint { get; set; }
    public string? Notes { get; set; }
}