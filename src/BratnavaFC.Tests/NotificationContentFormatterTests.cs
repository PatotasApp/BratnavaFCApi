using BratnavaFC.Application.Services;
using Xunit;

namespace BratnavaFC.Tests;

public class NotificationContentFormatterTests
{
    [Fact]
    public void MatchInviteBody_ShowsBrazilDateTimeAndLocation()
    {
        var playedAtUtc = new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc);

        var body = NotificationContentFormatter.MatchInviteBody(
            playedAtUtc, "Boca Jrs");

        Assert.Contains("11/08/2026 às 20:30", body);
        Assert.Contains("Boca Jrs", body);
    }

    [Fact]
    public void MatchInviteBody_OmitsEmptyLocation()
    {
        var playedAtUtc = new DateTime(2026, 8, 11, 23, 30, 0, DateTimeKind.Utc);

        var body = NotificationContentFormatter.MatchInviteBody(playedAtUtc, " ");

        Assert.Equal("11/08/2026 às 20:30. Confirme sua presença.", body);
    }

    [Theory]
    [InlineData(false, "Votação: Melhor uniforme. Participe e registre seu voto.")]
    [InlineData(true, "Evento: Churrasco da patota. Confirme se você vai participar.")]
    public void PollInviteBody_ShowsEntityTitle(bool isEvent, string expected)
    {
        var title = isEvent ? "Churrasco da patota" : "Melhor uniforme";

        var body = NotificationContentFormatter.PollInviteBody(title, isEvent);

        Assert.Equal(expected, body);
    }
}
