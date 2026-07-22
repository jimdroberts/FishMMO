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
			builder.ToTable("character_mail");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.SenderID)
				.IsRequired();

			builder.Property(e => e.SenderName)
				.IsRequired(false)
				.HasMaxLength(100);

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Subject)
				.IsRequired()
				.HasMaxLength(200);

			builder.Property(e => e.Body)
				.IsRequired()
				.HasMaxLength(4000);

			builder.Property(e => e.Read)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.CurrencyAttachment)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.ItemAttachmentTemplateID)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.ItemAttachmentSeed)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.ItemAttachmentAmount)
				.IsRequired()
				.HasDefaultValue(0);

			// Performance index for character mail queries (hot path)
			builder.HasIndex(e => e.CharacterID);

			// Index for sender lookups
			builder.HasIndex(e => e.SenderID);

			// Index for creation time (sorting/filtering)
			builder.HasIndex(e => e.TimeCreated);

			// Composite index for character + time queries
			builder.HasIndex(e => new { e.CharacterID, e.TimeCreated });

			// Foreign key relationships (prevent orphaned mail rows)
			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction)
				.HasConstraintName("FK_CharacterMail_RecipientCharacterID");

			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.SenderID)
				.OnDelete(DeleteBehavior.NoAction)
				.HasConstraintName("FK_CharacterMail_SenderID");
		}
	}
}