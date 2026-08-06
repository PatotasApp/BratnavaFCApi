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
