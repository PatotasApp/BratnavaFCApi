using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// O aviso em tempo real é o que permitiu ao front parar de chamar o unread-count de minuto
/// em minuto — sem ele, o badge do sininho não sobe até a próxima reconexão. Estes testes
/// cobrem as três garantias do caminho: avisa quem recebeu, não avisa se não persistiu, e
/// nunca deixa uma falha de realtime derrubar o push.
/// </summary>
public class PushServiceRealtimeTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    [Fact]
    public async Task Notifies_every_recipient_after_persisting()
    {
        var realtime = new Mock<IRealtimeNotifier>();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using var db = DbContextFactory.Create(nameof(Notifies_every_recipient_after_persisting));
        var service = NewService(db, realtime);

        await service.SendDataOnlyToUsersAsync(
            [userA, userB],
            new Dictionary<string, string> { ["title"] = "Partida criada", ["type"] = "match_created" },
            GroupId,
            CancellationToken.None);

        foreach (var userId in new[] { userA, userB })
        {
            realtime.Verify(
                r => r.NotificationCreatedAsync(
                    userId, GroupId, "Partida criada", "match_created", It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public void Does_not_carry_the_unread_count()
    {
        // Deliberado: calcular a contagem exigiria um COUNT por usuário do lote, recolocando no
        // banco a carga que a mudança veio remover. O cliente incrementa local. Se alguém um dia
        // acrescentar um parâmetro de contagem aqui, este teste precisa ser reavaliado junto.
        typeof(IRealtimeNotifier)
            .GetMethod(nameof(IRealtimeNotifier.NotificationCreatedAsync))!
            .GetParameters()
            .Select(p => p.Name)
            .Should().BeEquivalentTo("userId", "groupId", "title", "notificationType", "ct");
    }

    [Fact]
    public async Task Persists_the_inbox_row_before_notifying()
    {
        var realtime = new Mock<IRealtimeNotifier>();
        var userId = Guid.NewGuid();
        var persistedWhenNotified = -1;

        await using var db = DbContextFactory.Create(nameof(Persists_the_inbox_row_before_notifying));

        realtime
            .Setup(r => r.NotificationCreatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => persistedWhenNotified = db.UserNotifications.Count())
            .Returns(Task.CompletedTask);

        await NewService(db, realtime).SendDataOnlyToUsersAsync(
            [userId],
            new Dictionary<string, string> { ["title"] = "Ola" },
            GroupId,
            CancellationToken.None);

        // Avisar antes de gravar faria o cliente incrementar um badge que a próxima leitura
        // desmentiria.
        persistedWhenNotified.Should().Be(1);
    }

    [Fact]
    public async Task A_realtime_failure_never_breaks_the_push()
    {
        var realtime = new Mock<IRealtimeNotifier>();
        var userId = Guid.NewGuid();

        realtime
            .Setup(r => r.NotificationCreatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub fora do ar"));

        await using var db = DbContextFactory.Create(nameof(A_realtime_failure_never_breaks_the_push));

        var act = async () => await NewService(db, realtime).SendDataOnlyToUsersAsync(
            [userId],
            new Dictionary<string, string> { ["title"] = "Ola" },
            GroupId,
            CancellationToken.None);

        // A notificação já está no banco; o badge sobe no próximo resync de conexão. Perder o
        // aviso é degradação aceitável, derrubar o envio não é.
        await act.Should().NotThrowAsync();
        db.UserNotifications.Should().HaveCount(1);
    }

    [Fact]
    public async Task Does_not_notify_when_there_is_nothing_to_persist()
    {
        var realtime = new Mock<IRealtimeNotifier>();

        await using var db = DbContextFactory.Create(nameof(Does_not_notify_when_there_is_nothing_to_persist));

        // Sem title não há linha de inbox — e sem linha, avisar faria o badge contar algo
        // que não existe.
        await NewService(db, realtime).SendDataOnlyToUsersAsync(
            [Guid.NewGuid()],
            new Dictionary<string, string> { ["body"] = "sem titulo" },
            GroupId,
            CancellationToken.None);

        db.UserNotifications.Should().BeEmpty();
        realtime.Verify(
            r => r.NotificationCreatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PushService NewService(AppDbContext db, Mock<IRealtimeNotifier> realtime)
        => new(db, realtime.Object, NullLogger<PushService>.Instance);
}
