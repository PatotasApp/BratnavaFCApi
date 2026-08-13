using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserName).IsRequired();
        builder.Property(x => x.FirstName).IsRequired();
        builder.Property(x => x.LastName).IsRequired();
        builder.Property(x => x.Email).IsRequired();
        builder.Property(x => x.ProfilePhotoData).HasColumnType("bytea");
        builder.Property(x => x.ProfilePhotoContentType).HasMaxLength(32);
        builder.Property(x => x.ProfilePhotoUpdatedAt);
        builder.Property(x => x.ProfileVisibility)
            .HasConversion<short>()
            .HasDefaultValue(ProfileVisibility.AuthenticatedUsers)
            .IsRequired();
        builder.Property(x => x.ShowPatotaNamesOnProfile).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.ShowZoeiraAchievementsOnProfile).HasDefaultValue(false).IsRequired();
        // O e-mail é a identidade de autenticação e a chave que liga o token do Firebase à
        // linha aqui, então duas linhas com o mesmo e-mail tornariam essa resolução ambígua.
        // Depende de UserEntity.SetEmail normalizar para minúsculas — é o único caminho de
        // escrita da propriedade, então o índice simples basta e não precisa ser funcional.
        builder.HasIndex(x => x.Email).IsUnique();

        builder.Property(x => x.FirebaseUid).HasMaxLength(128);

        // Índice único parcial: enquanto o usuário não migrou, FirebaseUid é nulo, e o
        // Postgres trataria cada nulo como distinto num índice único comum — mas o filtro
        // deixa a intenção explícita e mantém o índice pequeno durante a transição.
        builder.HasIndex(x => x.FirebaseUid)
            .IsUnique()
            .HasFilter("\"FirebaseUid\" IS NOT NULL");

        // Derivado de FirstName/LastName. Explícito para o snapshot não criar coluna e para
        // deixar claro que projeções EF devem concatenar os dois campos, não ler esta.
        builder.Ignore(x => x.DisplayName);

        builder.HasMany(x => x.Players)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId);

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasDefaultValue(Status.Active)
            .IsRequired();

        builder.Property(x => x.InactivatedAt);
    }
}
