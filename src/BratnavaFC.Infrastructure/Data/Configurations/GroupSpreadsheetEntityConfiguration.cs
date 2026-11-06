using BratnavaFC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BratnavaFC.Infrastructure.Data.Configurations;

public class GroupSpreadsheetEntityConfiguration : IEntityTypeConfiguration<GroupSpreadsheetEntity>
{
    public void Configure(EntityTypeBuilder<GroupSpreadsheetEntity> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<short>().IsRequired();
        builder.Property(x => x.Status).HasConversion<short>().IsRequired();
        builder.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(GroupSpreadsheetEntity.FileNameMaxLength);
        builder.Property(x => x.StorageKey).IsRequired().HasMaxLength(GroupSpreadsheetEntity.StorageKeyMaxLength);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(GroupSpreadsheetEntity.ContentTypeMaxLength);
        builder.Property(x => x.Note).HasMaxLength(GroupSpreadsheetEntity.NoteMaxLength);
        builder.Property(x => x.AdminNote).HasMaxLength(GroupSpreadsheetEntity.NoteMaxLength);

        // Patota e quem enviou saem em cascata no banco. O código apaga as linhas antes
        // (para levar o arquivo do storage junto); o CASCADE é a rede de segurança.
        builder.HasOne<GroupEntity>()
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.CreateDate });
        builder.HasIndex(x => new { x.Status, x.CreateDate });
        builder.HasIndex(x => x.UploadedByUserId);
    }
}
