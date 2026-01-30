using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterMailEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterMailEntityConfiguration : IEntityTypeConfiguration<CharacterMailEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterMailEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.SenderCharacterID)
				.IsRequired();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Subject)
				.IsRequired()
				.HasMaxLength(200);

			builder.Property(e => e.Message)
				.IsRequired()
				.HasMaxLength(4000);

			builder.Property(e => e.ItemAttachmentTemplateID)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.ItemAttachmentSeed)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.ItemAttachmentAmount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Performance index for character mail queries (hot path)
			builder.HasIndex(e => e.CharacterID)
				.HasDatabaseName("IX_CharacterMail_CharacterID");

			// Index for sender lookups
			builder.HasIndex(e => e.SenderCharacterID)
				.HasDatabaseName("IX_CharacterMail_SenderCharacterID");

			// Index for creation time (sorting/filtering)
			builder.HasIndex(e => e.TimeCreated)
				.HasDatabaseName("IX_CharacterMail_TimeCreated");

			// Composite index for character + time queries
			builder.HasIndex(e => new { e.CharacterID, e.TimeCreated })
				.HasDatabaseName("IX_CharacterMail_CharacterID_TimeCreated");

			// Foreign key relationships (prevent orphaned mail rows)
			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction)
				.HasConstraintName("FK_CharacterMail_RecipientCharacterID");

			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.SenderCharacterID)
				.OnDelete(DeleteBehavior.NoAction)
				.HasConstraintName("FK_CharacterMail_SenderCharacterID");
		}
	}
}