using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos.Moderation;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Moq;

namespace BranavaFC.Tests;

public sealed class ModerationServiceTests
{
    private static UserEntity User(string name) => new(name, name, "X", $"{name}@t.com", "hash", null, null);

    [Fact]
    public async Task Denuncia_SalvaEAvisaAdminsDaPatota_SemDuplicarEnquantoPendente()
    {
        await using var db = DbContextFactory.Create(nameof(Denuncia_SalvaEAvisaAdminsDaPatota_SemDuplicarEnquantoPendente));
        var reporter = User("ana");
        db.Users.Add(reporter);
        await db.SaveChangesAsync();
        var push = new Mock<IPushService>();
        var sut = new ModerationService(db, push.Object);
        var groupId = Guid.NewGuid();
        var clipId = Guid.NewGuid();
        var dto = new CreateContentReportDto(ContentReportTargetType.Replay, clipId, groupId, "Conteúdo ofensivo", "Xingamento no vídeo");

        (await sut.ReportAsync(reporter.Id, dto, default)).Success.Should().BeTrue();
        (await sut.ReportAsync(reporter.Id, dto, default)).Success.Should().BeTrue();

        db.ContentReports.Should().ContainSingle(r => r.TargetId == clipId && !r.IsResolved && r.GroupId == groupId);
        push.Verify(p => p.SendToGroupAdminsAsync(groupId, "Nova denúncia", It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Denuncia_SemMotivo_Falha()
    {
        await using var db = DbContextFactory.Create(nameof(Denuncia_SemMotivo_Falha));
        var sut = new ModerationService(db);

        var result = await sut.ReportAsync(Guid.NewGuid(),
            new CreateContentReportDto(ContentReportTargetType.User, Guid.NewGuid(), null, " ", null), default);

        result.Success.Should().BeFalse();
        db.ContentReports.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolverDenuncia_TiraDaListaDePendentes()
    {
        await using var db = DbContextFactory.Create(nameof(ResolverDenuncia_TiraDaListaDePendentes));
        var reporter = User("bia");
        db.Users.Add(reporter);
        await db.SaveChangesAsync();
        var sut = new ModerationService(db);
        await sut.ReportAsync(reporter.Id,
            new CreateContentReportDto(ContentReportTargetType.User, Guid.NewGuid(), null, "Spam", null), default);
        var report = (await sut.GetReportsAsync(true, default)).Data!.Single();
        report.ReporterName.Should().Be("bia X");

        (await sut.ResolveReportAsync(report.Id, Guid.NewGuid(), "Removido", default)).Success.Should().BeTrue();

        (await sut.GetReportsAsync(true, default)).Data.Should().BeEmpty();
        (await sut.GetReportsAsync(false, default)).Data!.Single().IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task Bloqueio_ValeNosDoisSentidos_EDesbloquearRemove()
    {
        await using var db = DbContextFactory.Create(nameof(Bloqueio_ValeNosDoisSentidos_EDesbloquearRemove));
        var a = User("caio");
        var b = User("duda");
        db.Users.AddRange(a, b);
        await db.SaveChangesAsync();
        var sut = new ModerationService(db);

        (await sut.BlockAsync(a.Id, b.Id, default)).Success.Should().BeTrue();
        (await sut.BlockAsync(a.Id, b.Id, default)).Success.Should().BeTrue();

        db.UserBlocks.Should().ContainSingle();
        (await sut.IsBlockedEitherWayAsync(b.Id, a.Id, default)).Should().BeTrue();
        (await sut.GetBlockedAsync(a.Id, default)).Data!.Single().UserId.Should().Be(b.Id);

        (await sut.UnblockAsync(a.Id, b.Id, default)).Success.Should().BeTrue();
        (await sut.IsBlockedEitherWayAsync(a.Id, b.Id, default)).Should().BeFalse();
    }

    [Fact]
    public async Task PerfilPublico_DeQuemBloqueou_NaoAparece()
    {
        await using var db = DbContextFactory.Create(nameof(PerfilPublico_DeQuemBloqueou_NaoAparece));
        var a = User("fabi");
        var b = User("gui");
        db.Users.AddRange(a, b);
        await db.SaveChangesAsync();
        await new ModerationService(db).BlockAsync(a.Id, b.Id, default);
        var profile = new ProfileService(db, Mock.Of<IConquistaService>(),
            Mock.Of<BratnavaFC.Infrastructure.Cloudflare.IImageStorageService>());

        await FluentActions.Awaiting(() => profile.GetPublicProfileAsync(a.Id, b.Id, false))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => profile.GetPublicProfileAsync(b.Id, a.Id, false))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Bloqueio_ASiMesmo_Falha()
    {
        await using var db = DbContextFactory.Create(nameof(Bloqueio_ASiMesmo_Falha));
        var a = User("edu");
        db.Users.Add(a);
        await db.SaveChangesAsync();

        (await new ModerationService(db).BlockAsync(a.Id, a.Id, default)).Success.Should().BeFalse();
    }
}
