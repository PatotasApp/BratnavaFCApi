namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class CreatePollDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool AllowMultipleVotes { get; set; }
    public bool ShowVotes { get; set; }
    public string? DeadlineDate { get; set; } // yyyy-MM-dd
    public string? DeadlineTime { get; set; } // HH:mm
    public bool AddToCalendar { get; set; }   // cria lembrete no calendário com a data do prazo
}
