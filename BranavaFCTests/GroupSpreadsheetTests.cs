using System.Reflection;
using System.Security.Claims;
using System.Text;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Spreadsheets;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using BratnavaFC.Infrastructure.Spreadsheets;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BranavaFC.Tests;

/// <summary>Storage de planilhas em memória, para conferir o que foi gravado e apagado.</summary>
internal sealed class FakeSpreadsheetStorage : ISpreadsheetStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public bool FailOnSave { get; set; }

    public string Description => "fake";

    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        if (FailOnSave) throw new IOException("storage fora do ar");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Files[objectKey] = ms.ToArray();
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
        => Files.TryGetValue(objectKey, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(objectKey);

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        Files.Remove(objectKey);
        return Task.CompletedTask;
    }
}

/// <summary>Envio de planilhas pela patota, fila dos administradores e avisos.</summary>
public sealed class GroupSpreadsheetTests
{
    private static readonly byte[] XlsxBytes = [.. "PK\u0003\u0004"u8, .. new byte[64]];
    private static readonly byte[] PdfBytes = "%PDF-1.7\n..."u8.ToArray();
    private static readonly byte[] CsvBytes = Encoding.UTF8.GetBytes("data;placar\n01/01/2026;3x2\n");

    // ── Envio ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Envio_Valido_GuardaArquivoPrivadoELinhaRecebida()
    {
        var (db, storage, _, sut) = Build(nameof(Envio_Valido_GuardaArquivoPrivadoELinhaRecebida));
        var (group, admin) = await SeedAsync(db);

        var result = await Upload(sut, group.Id, admin.Id, "C:\\fakepath\\Jogos 2025.xlsx", XlsxBytes,
            GroupSpreadsheetKind.Matches, note: "  Só a aba 2025  ");

        result.Success.Should().BeTrue(result.Error);
        result.Status.Should().Be(ResultStatus.Created);
        var dto = result.Data!;
        dto.FileName.Should().Be("Jogos 2025.xlsx");
        dto.Status.Should().Be(GroupSpreadsheetStatus.Received);
        dto.Kind.Should().Be(GroupSpreadsheetKind.Matches);
        dto.Note.Should().Be("Só a aba 2025");
        dto.GroupName.Should().Be("Resenha FC");
        dto.UploadedByName.Should().Be("Alexandre Pires");
        dto.UploadedByEmail.Should().BeNull("a patota não precisa ver e-mail");

        var row = await db.GroupSpreadsheets.SingleAsync();
        row.StorageKey.Should().StartWith($"group-spreadsheets/{group.Id:N}/").And.EndWith(".xlsx");
        row.StorageKey.Should().NotContain("Jogos", "o nome original nunca entra na key");
        row.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        storage.Files.Should().ContainKey(row.StorageKey).WhoseValue.Should().Equal(XlsxBytes);
    }

    [Theory]
    [InlineData("pagamentos.csv")]
    [InlineData("pagamentos.CSV")]
    public async Task Envio_Csv_Aceito(string name)
    {
        var (db, _, _, sut) = Build(nameof(Envio_Csv_Aceito) + name);
        var (group, admin) = await SeedAsync(db);

        var result = await Upload(sut, group.Id, admin.Id, name, CsvBytes, GroupSpreadsheetKind.Payments);

        result.Success.Should().BeTrue(result.Error);
        (await db.GroupSpreadsheets.SingleAsync()).ContentType.Should().Be("text/csv");
    }

    [Fact]
    public async Task Envio_CsvUtf16ComBom_Aceito()
    {
        var (db, _, _, sut) = Build(nameof(Envio_CsvUtf16ComBom_Aceito));
        var (group, admin) = await SeedAsync(db);
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("a;b\n1;2")).ToArray();

        (await Upload(sut, group.Id, admin.Id, "x.csv", bytes, GroupSpreadsheetKind.Payments)).Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("virus.exe", "PK\u0003\u0004")]
    [InlineData("planilha", "PK\u0003\u0004")]
    [InlineData("foto.png", "PK\u0003\u0004")]
    [InlineData("disfarce.xlsx", "MZ executavel")]
    [InlineData("disfarce.pdf", "PK\u0003\u0004")]
    [InlineData("disfarce.xls", "PK\u0003\u0004")]
    public async Task Envio_TipoOuConteudoInvalido_Recusado(string name, string content)
    {
        var (db, storage, _, sut) = Build(nameof(Envio_TipoOuConteudoInvalido_Recusado) + name);
        var (group, admin) = await SeedAsync(db);

        var result = await Upload(sut, group.Id, admin.Id, name, Encoding.Latin1.GetBytes(content), GroupSpreadsheetKind.Matches);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        (await db.GroupSpreadsheets.AnyAsync()).Should().BeFalse();
        storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Envio_CsvBinario_Recusado()
    {
        var (db, _, _, sut) = Build(nameof(Envio_CsvBinario_Recusado));
        var (group, admin) = await SeedAsync(db);

        var result = await Upload(sut, group.Id, admin.Id, "x.csv", [0x4D, 0x5A, 0x00, 0x01], GroupSpreadsheetKind.Matches);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Envio_AcimaDe10MB_Recusado()
    {
        var (db, _, _, sut) = Build(nameof(Envio_AcimaDe10MB_Recusado));
        var (group, admin) = await SeedAsync(db);
        var big = new byte[SpreadsheetFileRules.MaxSizeBytes + 1];
        "PK\u0003\u0004"u8.CopyTo(big);

        var result = await Upload(sut, group.Id, admin.Id, "grande.xlsx", big, GroupSpreadsheetKind.Matches);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("10 MB");
    }

    [Fact]
    public async Task Envio_TipoDePlanilhaInexistente_Recusado()
    {
        var (db, _, _, sut) = Build(nameof(Envio_TipoDePlanilhaInexistente_Recusado));
        var (group, admin) = await SeedAsync(db);

        (await Upload(sut, group.Id, admin.Id, "a.xlsx", XlsxBytes, (GroupSpreadsheetKind)9)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Envio_PatotaInexistente_NotFound()
    {
        var (db, _, _, sut) = Build(nameof(Envio_PatotaInexistente_NotFound));
        var (_, admin) = await SeedAsync(db);

        var result = await Upload(sut, Guid.NewGuid(), admin.Id, "a.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Envio_LimiteDePendentesPorPatota()
    {
        var (db, _, _, sut) = Build(nameof(Envio_LimiteDePendentesPorPatota));
        var (group, admin) = await SeedAsync(db);
        for (var i = 0; i < SpreadsheetFileRules.MaxPendingPerGroup; i++)
            (await Upload(sut, group.Id, admin.Id, $"p{i}.pdf", PdfBytes, GroupSpreadsheetKind.Payments)).Success.Should().BeTrue();

        var result = await Upload(sut, group.Id, admin.Id, "extra.pdf", PdfBytes, GroupSpreadsheetKind.Payments);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task Envio_StorageFora_NaoGravaLinha()
    {
        var (db, storage, _, sut) = Build(nameof(Envio_StorageFora_NaoGravaLinha));
        var (group, admin) = await SeedAsync(db);
        storage.FailOnSave = true;

        var result = await Upload(sut, group.Id, admin.Id, "a.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches);

        result.Success.Should().BeFalse();
        (await db.GroupSpreadsheets.AnyAsync()).Should().BeFalse();
    }

    // ── Situação e avisos ────────────────────────────────────────────────────

    [Fact]
    public async Task Situacao_EmAndamentoEFinalizada_AvisamQuemEnviou()
    {
        var (db, _, push, sut) = Build(nameof(Situacao_EmAndamentoEFinalizada_AvisamQuemEnviou));
        var (group, admin) = await SeedAsync(db);
        var id = (await Upload(sut, group.Id, admin.Id, "jogos.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;
        var platformAdmin = Guid.NewGuid();

        var andamento = await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.InProgress, null), platformAdmin, default);

        andamento.Success.Should().BeTrue(andamento.Error);
        andamento.Data!.Status.Should().Be(GroupSpreadsheetStatus.InProgress);
        andamento.Data.StatusChangedAt.Should().NotBeNull();
        andamento.Data.UploadedByEmail.Should().Be("alexandre@test.com", "o painel mostra o e-mail");
        push.Verify(p => p.SendToUserAsync(
            admin.Id, "Importação em andamento",
            It.Is<string>(b => b.Contains("histórico de jogos") && b.Contains("jogos.xlsx")),
            It.Is<Dictionary<string, string>>(d => d["type"] == "group_spreadsheet" && d["spreadsheetId"] == id.ToString()
                                                  && d["groupId"] == group.Id.ToString() && d["status"] == "InProgress"),
            It.IsAny<CancellationToken>(), group.Id), Times.Once);

        await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.Finished, "Importamos 2023 a 2025."), platformAdmin, default);

        push.Verify(p => p.SendToUserAsync(
            admin.Id, "Planilha importada",
            It.Is<string>(b => b.Contains("Importamos 2023 a 2025.")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), group.Id), Times.Once);

        var row = await db.GroupSpreadsheets.AsNoTracking().SingleAsync();
        row.Status.Should().Be(GroupSpreadsheetStatus.Finished);
        row.AdminNote.Should().Be("Importamos 2023 a 2025.");
        row.StatusChangedByUserId.Should().Be(platformAdmin);
    }

    [Fact]
    public async Task Situacao_SemMudancaOuDeVoltaParaRecebida_NaoAvisa()
    {
        var (db, _, push, sut) = Build(nameof(Situacao_SemMudancaOuDeVoltaParaRecebida_NaoAvisa));
        var (group, admin) = await SeedAsync(db);
        var id = (await Upload(sut, group.Id, admin.Id, "a.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;

        await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.Received, "olhando"), Guid.NewGuid(), default);
        await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.InProgress, null), Guid.NewGuid(), default);
        await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.InProgress, "nota nova"), Guid.NewGuid(), default);
        await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.Received, null), Guid.NewGuid(), default);

        push.Verify(p => p.SendToUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task Situacao_FalhaNoAviso_NaoDesfazATroca()
    {
        var (db, _, push, sut) = Build(nameof(Situacao_FalhaNoAviso_NaoDesfazATroca));
        var (group, admin) = await SeedAsync(db);
        var id = (await Upload(sut, group.Id, admin.Id, "a.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;
        push.Setup(p => p.SendToUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ThrowsAsync(new InvalidOperationException("FCM fora"));

        var result = await sut.ChangeStatusAsync(id, new(GroupSpreadsheetStatus.InProgress, null), Guid.NewGuid(), default);

        result.Success.Should().BeTrue();
        (await db.GroupSpreadsheets.AsNoTracking().SingleAsync()).Status.Should().Be(GroupSpreadsheetStatus.InProgress);
    }

    [Fact]
    public async Task Situacao_Invalida_OuPlanilhaInexistente()
    {
        var (db, _, _, sut) = Build(nameof(Situacao_Invalida_OuPlanilhaInexistente));
        var (group, admin) = await SeedAsync(db);
        var id = (await Upload(sut, group.Id, admin.Id, "a.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;

        (await sut.ChangeStatusAsync(id, new((GroupSpreadsheetStatus)7, null), Guid.NewGuid(), default)).Success.Should().BeFalse();
        (await sut.ChangeStatusAsync(Guid.NewGuid(), new(GroupSpreadsheetStatus.InProgress, null), Guid.NewGuid(), default))
            .Status.Should().Be(ResultStatus.NotFound);
    }

    // ── Painel: lista e download ─────────────────────────────────────────────

    [Fact]
    public async Task Painel_ListaComFiltrosEEmail()
    {
        var (db, _, _, sut) = Build(nameof(Painel_ListaComFiltrosEEmail));
        var (group, admin) = await SeedAsync(db);
        var jogos = (await Upload(sut, group.Id, admin.Id, "j.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!;
        await Upload(sut, group.Id, admin.Id, "p.pdf", PdfBytes, GroupSpreadsheetKind.Payments);
        await sut.ChangeStatusAsync(jogos.Id, new(GroupSpreadsheetStatus.InProgress, null), Guid.NewGuid(), default);

        (await sut.GetAllAsync(null, null, null, default)).Data!.Should().HaveCount(2)
            .And.OnlyContain(d => d.UploadedByEmail == "alexandre@test.com");
        (await sut.GetAllAsync(GroupSpreadsheetStatus.Received, null, null, default)).Data!.Should().ContainSingle(d => d.FileName == "p.pdf");
        (await sut.GetAllAsync(null, GroupSpreadsheetKind.Matches, null, default)).Data!.Should().ContainSingle(d => d.FileName == "j.xlsx");
        (await sut.GetAllAsync(null, null, Guid.NewGuid(), default)).Data!.Should().BeEmpty();
        (await sut.GetForGroupAsync(group.Id, default)).Data!.Should().HaveCount(2).And.OnlyContain(d => d.UploadedByEmail == null);
    }

    [Fact]
    public async Task Download_DevolveOArquivoComNomeOriginal()
    {
        var (db, storage, _, sut) = Build(nameof(Download_DevolveOArquivoComNomeOriginal));
        var (group, admin) = await SeedAsync(db);
        var id = (await Upload(sut, group.Id, admin.Id, "Pagamentos 2025.pdf", PdfBytes, GroupSpreadsheetKind.Payments)).Data!.Id;

        var result = await sut.DownloadAsync(id, default);

        result.Success.Should().BeTrue();
        result.Data!.FileName.Should().Be("Pagamentos 2025.pdf");
        result.Data.ContentType.Should().Be("application/pdf");
        using var ms = new MemoryStream();
        await result.Data.Content.CopyToAsync(ms);
        ms.ToArray().Should().Equal(PdfBytes);

        storage.Files.Clear();
        (await sut.DownloadAsync(id, default)).Status.Should().Be(ResultStatus.NotFound);
        (await sut.DownloadAsync(Guid.NewGuid(), default)).Status.Should().Be(ResultStatus.NotFound);
    }

    // ── Remoção ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Patota_RemoveSoEnquantoRecebida_EApagaArquivo()
    {
        var (db, storage, _, sut) = Build(nameof(Patota_RemoveSoEnquantoRecebida_EApagaArquivo));
        var (group, admin) = await SeedAsync(db);
        var a = (await Upload(sut, group.Id, admin.Id, "a.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;
        var b = (await Upload(sut, group.Id, admin.Id, "b.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;
        await sut.ChangeStatusAsync(b, new(GroupSpreadsheetStatus.InProgress, null), Guid.NewGuid(), default);

        (await sut.DeleteForGroupAsync(group.Id, a, default)).Success.Should().BeTrue();
        (await sut.DeleteForGroupAsync(group.Id, b, default)).Status.Should().Be(ResultStatus.Conflict);
        (await sut.DeleteForGroupAsync(Guid.NewGuid(), b, default)).Status.Should().Be(ResultStatus.NotFound, "planilha de outra patota");

        (await db.GroupSpreadsheets.Select(s => s.Id).ToListAsync()).Should().Equal(b);
        storage.Files.Should().HaveCount(1);

        (await sut.DeleteAsync(b, default)).Success.Should().BeTrue("o painel remove em qualquer situação");
        storage.Files.Should().BeEmpty();
    }

    // ── Autorização ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Controller_MembroComum_Recebe403()
    {
        var (db, _, _, sut) = Build(nameof(Controller_MembroComum_Recebe403));
        var (group, _) = await SeedAsync(db);
        var member = Guid.NewGuid();
        db.Players.Add(new PlayerEntity("Membro", member, group.Id, 3m, false, false));
        await db.SaveChangesAsync();
        var controller = GroupController(sut, db, member, "User");

        (await controller.Upload(group.Id, FormFile("a.xlsx", XlsxBytes), GroupSpreadsheetKind.Matches, null, default))
            .Should().BeOfType<ForbidResult>();
        (await controller.List(group.Id, default)).Should().BeOfType<ForbidResult>();
        (await controller.Delete(group.Id, Guid.NewGuid(), default)).Should().BeOfType<ForbidResult>();
        (await db.GroupSpreadsheets.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Controller_AdminDeOutraPatota_Recebe403()
    {
        var (db, _, _, sut) = Build(nameof(Controller_AdminDeOutraPatota_Recebe403));
        var (group, _) = await SeedAsync(db);
        var (_, otherAdmin) = await SeedAsync(db, email: "outro@test.com");

        (await GroupController(sut, db, otherAdmin.Id, "User").Upload(
                group.Id, FormFile("a.xlsx", XlsxBytes), GroupSpreadsheetKind.Matches, null, default))
            .Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData("User", true)]
    [InlineData("Admin", false)]
    [InlineData("GodMode", false)]
    public async Task Controller_AdminDaPatotaOuDaPlataforma_Envia(string role, bool groupAdmin)
    {
        var (db, _, _, sut) = Build(nameof(Controller_AdminDaPatotaOuDaPlataforma_Envia) + role);
        var (group, admin) = await SeedAsync(db);
        var userId = groupAdmin ? admin.Id : (await SeedUserAsync(db, "plataforma@test.com")).Id;

        var result = await GroupController(sut, db, userId, role).Upload(
            group.Id, FormFile("a.xlsx", XlsxBytes), GroupSpreadsheetKind.Payments, "obs", default);

        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(201);
        (await db.GroupSpreadsheets.SingleAsync()).UploadedByUserId.Should().Be(userId);
        (await GroupController(sut, db, userId, role).List(group.Id, default))
            .Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Controller_EnvioSemArquivo_400()
    {
        var (db, _, _, sut) = Build(nameof(Controller_EnvioSemArquivo_400));
        var (group, admin) = await SeedAsync(db);

        (await GroupController(sut, db, admin.Id, "User").Upload(group.Id, null, GroupSpreadsheetKind.Matches, null, default))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Painel_SoAdminEGodMode()
    {
        var roles = typeof(AdminSpreadsheetsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles!
            .Split(',').Select(r => r.Trim());
        roles.Should().BeEquivalentTo("Admin", "GodMode");

        // Nenhuma ação do painel pode afrouxar a regra da classe.
        typeof(AdminSpreadsheetsController).GetMethods()
            .Where(m => m.DeclaringType == typeof(AdminSpreadsheetsController))
            .Should().OnlyContain(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null
                                    && m.GetCustomAttribute<AuthorizeAttribute>() == null);
    }

    [Fact]
    public async Task Painel_DownloadDevolveAnexo()
    {
        var (db, _, _, sut) = Build(nameof(Painel_DownloadDevolveAnexo));
        var (group, admin) = await SeedAsync(db);
        var id = (await Upload(sut, group.Id, admin.Id, "Jogos.xlsx", XlsxBytes, GroupSpreadsheetKind.Matches)).Data!.Id;
        var controller = new AdminSpreadsheetsController(sut)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(Guid.NewGuid(), "Admin") } },
        };

        var file = (await controller.Download(id, default)).Should().BeOfType<FileStreamResult>().Subject;

        file.FileDownloadName.Should().Be("Jogos.xlsx");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        (await controller.Download(Guid.NewGuid(), default)).Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    // ── Regras puras ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("C:\\Users\\x\\Planilha.xlsx", "Planilha.xlsx")]
    [InlineData("../../etc/passwd.csv", "passwd.csv")]
    [InlineData("nome\"com\u0001lixo.csv", "nomecomlixo.csv")]
    [InlineData("   ", "planilha")]
    public void NomeDoArquivo_Sanitizado(string input, string expected)
        => SpreadsheetFileRules.SanitizeFileName(input).Should().Be(expected);

    [Theory]
    [InlineData("group-spreadsheets/abc/def.xlsx", true)]
    [InlineData("group-spreadsheets/../x/def.xlsx", false)]
    [InlineData("avatars/abc/def.png", false)]
    [InlineData("group-spreadsheets/abc\\def.xlsx", false)]
    [InlineData("group-spreadsheets/a/b/c.xlsx", false)]
    public void ChaveDoStorage_SoNoFormatoGerado(string key, bool valid)
        => SpreadsheetObjectKeys.IsValid(key).Should().Be(valid);

    [Fact]
    public async Task ArmazenamentoEmDisco_GravaLeApagaEBloqueiaChaveForaDaPasta()
    {
        var root = Path.Combine(Path.GetTempPath(), "planilhas-teste-" + Guid.NewGuid().ToString("N"));
        try
        {
            var disk = new LocalDiskSpreadsheetStorage(root);
            var key = SpreadsheetObjectKeys.New(Guid.NewGuid(), ".csv");

            await disk.SaveAsync(key, new MemoryStream(CsvBytes), "text/csv", default);
            await using (var read = await disk.OpenReadAsync(key, default))
            {
                using var ms = new MemoryStream();
                await read.CopyToAsync(ms);
                ms.ToArray().Should().Equal(CsvBytes);
            }

            await disk.DeleteAsync(key, default);
            await disk.DeleteAsync(key, default); // apagar de novo não é erro
            await FluentActions.Awaiting(() => disk.OpenReadAsync(key, default)).Should().ThrowAsync<FileNotFoundException>();
            await FluentActions.Awaiting(() => disk.OpenReadAsync("group-spreadsheets/../../x.csv", default))
                .Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (AppDbContext Db, FakeSpreadsheetStorage Storage, Mock<IPushService> Push, GroupSpreadsheetService Sut) Build(string name)
    {
        var db = DbContextFactory.Create("Spreadsheets_" + name);
        var storage = new FakeSpreadsheetStorage();
        var push = new Mock<IPushService>();
        return (db, storage, push, new GroupSpreadsheetService(db, storage, push.Object, NullLogger<GroupSpreadsheetService>.Instance));
    }

    private static Task<Result<GroupSpreadsheetDto>> Upload(
        GroupSpreadsheetService sut, Guid groupId, Guid userId, string name, byte[] bytes,
        GroupSpreadsheetKind kind, string? note = null)
        => sut.UploadAsync(groupId, userId, kind, name, bytes.Length, new MemoryStream(bytes), note, default);

    internal static async Task<UserEntity> SeedUserAsync(AppDbContext db, string email)
    {
        var user = new UserEntity(email.Split('@')[0], "Alexandre", "Pires", email, "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(GroupEntity Group, UserEntity Admin)> SeedAsync(AppDbContext db, string email = "alexandre@test.com")
    {
        var admin = await SeedUserAsync(db, email);
        var group = new GroupEntity("Resenha FC", null, admin.Id);
        group.SetAdmins([admin.Id]);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return (group, admin);
    }

    private static ClaimsPrincipal Principal(Guid userId, string role)
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            "Tests", ClaimTypes.Name, ClaimTypes.Role));

    private static GroupSpreadsheetsController GroupController(GroupSpreadsheetService sut, AppDbContext db, Guid userId, string role)
        => new(sut, db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role) } },
        };

    private static IFormFile FormFile(string name, byte[] bytes)
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);
}

/// <summary>
/// Exclusão de patota e de conta levando planilhas e arquivos junto. Postgres real: a
/// exclusão de conta roda via ExecuteDelete, que o InMemory não traduz.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class GroupSpreadsheetCleanupTests : IClassFixture<CampeonatoPostgresFixture>
{
    private readonly CampeonatoPostgresFixture _fixture;

    public GroupSpreadsheetCleanupTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExcluirPatota_ApagaPlanilhasEArquivos()
    {
        await using var db = await _fixture.CreateDatabaseAsync("planilhas_excluir_patota");
        var storage = new FakeSpreadsheetStorage();
        var admin = await GroupSpreadsheetTests.SeedUserAsync(db, "dono@test.com");
        var group = await SeedGroupAsync(db, admin.Id, [admin.Id]);
        var other = await SeedGroupAsync(db, admin.Id, [admin.Id]);
        var service = new GroupSpreadsheetService(db, storage, Mock.Of<IPushService>(), NullLogger<GroupSpreadsheetService>.Instance);
        await UploadAsync(service, group.Id, admin.Id);
        await UploadAsync(service, group.Id, admin.Id);
        var kept = await UploadAsync(service, other.Id, admin.Id);
        db.ChangeTracker.Clear();

        var groups = NewGroupService(db, storage);
        (await groups.DeleteAsync(group.Id, default)).Success.Should().BeTrue();

        (await db.GroupSpreadsheets.Select(s => s.Id).ToListAsync()).Should().Equal(kept);
        storage.Files.Should().ContainSingle().Which.Key.Should().Contain(other.Id.ToString("N"));
    }

    [Fact]
    public async Task ExcluirConta_ApagaPlanilhasQueEleEnviou_InclusiveDePatotaQueContinua()
    {
        await using var db = await _fixture.CreateDatabaseAsync("planilhas_excluir_conta");
        var storage = new FakeSpreadsheetStorage();
        var leaving = await GroupSpreadsheetTests.SeedUserAsync(db, "sai@test.com");
        var staying = await GroupSpreadsheetTests.SeedUserAsync(db, "fica@test.com");

        // Patota só dele (some junto) e patota com outro admin (continua).
        var solo = await SeedGroupAsync(db, leaving.Id, [leaving.Id]);
        var shared = await SeedGroupAsync(db, leaving.Id, [leaving.Id, staying.Id]);
        db.Players.Add(new PlayerEntity("Fica", staying.Id, shared.Id, 3m, false, false));
        await db.SaveChangesAsync();

        var service = new GroupSpreadsheetService(db, storage, Mock.Of<IPushService>(), NullLogger<GroupSpreadsheetService>.Instance);
        await UploadAsync(service, solo.Id, leaving.Id);
        await UploadAsync(service, shared.Id, leaving.Id);
        var othersUpload = await UploadAsync(service, shared.Id, staying.Id);
        db.ChangeTracker.Clear();

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        repo.Setup(x => x.GetByIdIncludingInactiveAsync(leaving.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(await db.Users.FirstAsync(u => u.Id == leaving.Id));
        var users = new UserService(db, repo.Object, Mock.Of<ILogger<UserService>>(), TestImageStorage.Create(), NewGroupService(db, storage));

        var result = await users.DeleteMyAccountAsync(leaving.Id, default);

        result.Success.Should().BeTrue(result.Error);
        (await db.Groups.AnyAsync(g => g.Id == solo.Id)).Should().BeFalse();
        (await db.GroupSpreadsheets.Select(s => s.Id).ToListAsync()).Should().Equal(othersUpload);
        storage.Files.Should().ContainSingle();
    }

    private static GroupService NewGroupService(AppDbContext db, ISpreadsheetStorage storage)
        => new(db, NullLogger<GroupService>.Instance, new Mock<IRepositoryBase<GroupEntity>>().Object,
            Mock.Of<IPushService>(), TestImageStorage.Create(), storage);

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db, Guid creatorId, Guid[] adminIds)
    {
        var group = new GroupEntity("Patota " + Guid.NewGuid().ToString("N")[..6], null, creatorId);
        group.SetAdmins(adminIds);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<Guid> UploadAsync(GroupSpreadsheetService service, Guid groupId, Guid userId)
    {
        var bytes = "a;b\n1;2"u8.ToArray();
        var result = await service.UploadAsync(groupId, userId, GroupSpreadsheetKind.Payments, "p.csv", bytes.Length, new MemoryStream(bytes), null, default);
        result.Success.Should().BeTrue(result.Error);
        return result.Data!.Id;
    }
}
