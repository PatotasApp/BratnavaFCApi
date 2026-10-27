using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public sealed class ChampionshipEntityConfiguration : IEntityTypeConfiguration<ChampionshipEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlaceName).IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(ChampionshipStatus.Created)
            .IsRequired();

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Jogos do campeonato: Restrict para nunca apagar partidas (e estatísticas) em cascata por engano.
        builder.HasMany(x => x.Matches)
            .WithOne(x => x.Championship)
            .HasForeignKey(x => x.ChampionshipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.RulesJson).HasColumnType("jsonb").IsRequired();
        builder.Ignore(x => x.Rules);
        builder.Ignore(x => x.RulesLocked);

        builder.HasMany(x => x.Teams)
            .WithOne()
            .HasForeignKey(x => x.ChampionshipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Players)
            .WithOne()
            .HasForeignKey(x => x.ChampionshipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Votes)
            .WithOne()
            .HasForeignKey(x => x.ChampionshipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.GroupId, x.PlayedAt });
    }
}

public sealed class ChampionshipTeamEntityConfiguration : IEntityTypeConfiguration<ChampionshipTeamEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipTeamEntity> builder)
    {
        builder.HasKey(x => x.Id);
        // Id gerado no domínio: sem isso, um filho novo adicionado a um campeonato já
        // rastreado vira UPDATE (o EF supõe que chave preenchida = linha existente).
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();

        builder.HasOne(x => x.Color)
            .WithMany()
            .HasForeignKey(x => x.ColorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class ChampionshipPlayerEntityConfiguration : IEntityTypeConfiguration<ChampionshipPlayerEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipPlayerEntity> builder)
    {
        builder.HasKey(x => x.Id);
        // Id gerado no domínio: sem isso, um filho novo adicionado a um campeonato já
        // rastreado vira UPDATE (o EF supõe que chave preenchida = linha existente).
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.InviteResponse).HasDefaultValue(InviteResponse.None);

        // Cascade: quem tem jogo de campeonato já é protegido pelo histórico em MatchPlayers;
        // aqui sobra só a presença do dia, que pode sair junto com o jogador.
        builder.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ChampionshipId, x.PlayerId }).IsUnique();
    }
}

public sealed class ChampionshipVoteEntityConfiguration : IEntityTypeConfiguration<ChampionshipVoteEntity>
{
    public void Configure(EntityTypeBuilder<ChampionshipVoteEntity> builder)
    {
        builder.HasKey(x => x.Id);
        // Id gerado no domínio: sem isso, um filho novo adicionado a um campeonato já
        // rastreado vira UPDATE (o EF supõe que chave preenchida = linha existente).
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.ChampionshipId, x.GameId, x.VoterPlayerId }).IsUnique();
    }
}
