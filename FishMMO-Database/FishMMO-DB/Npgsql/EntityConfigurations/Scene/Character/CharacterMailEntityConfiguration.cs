using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterMailEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterMailEntityConfiguration : IEntityTypeConfiguration<CharacterMailEntity>
	{
		/// <inheritdoc/>
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

			/* No bare (character_id) index here.
			 *
			 * The composite above already begins with character_id, so it serves a lookup on
			 * character_id alone — a second index adds nothing a query can use and is maintained on
			 * every write. That cost is not theoretical on these tables: the character save path
			 * deletes and reinserts a character's whole collection, so every redundant index is
			 * rewritten wholesale on each save. Measured on the dev database, the bare index had
			 * taken zero scans while its composite had taken thousands.
			 *
			 * Do NOT copy this omission to character_guild, character_party, character_pet,
			 * guild_application or scenes: those have no composite starting with character_id, so
			 * their bare index is the only thing serving the lookup and dropping it means a scan. */

			// Index for sender lookups
			builder.HasIndex(e => e.SenderID);

			// Index for creation time (sorting/filtering)
			builder.HasIndex(e => e.TimeCreated);

			// Composite index for character + time queries
			builder.HasIndex(e => new { e.CharacterID, e.TimeCreated });

			// Foreign key relationships (application-managed referential integrity)
			// NoAction means no CASCADE/SET NULL — the application handles referential
			// integrity at the business-logic layer rather than the database layer.
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