using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterDialogueChoiceEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterDialogueChoiceEntityConfiguration : IEntityTypeConfiguration<CharacterDialogueChoiceEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterDialogueChoiceEntity> builder)
		{
			builder.ToTable("character_dialogue_choices");

			/* Composite primary key rather than the surrogate bigserial the sibling character
			 * tables use. This row has no identity of its own — (character, template) IS the
			 * identity, it is never referenced by anything else, and the same pair is what the
			 * upsert arbitrates on and what the character-load fetch scans. One b-tree therefore
			 * serves the key, the conflict target and the only read, where a surrogate key would
			 * add a second index and a sequence to a table that is one row per character per
			 * caching dialogue and can reach millions of rows on a mature shard. */
			builder.HasKey(e => new { e.CharacterID, e.TemplateID });

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Choices)
				.IsRequired()
				.HasDefaultValue((short)0);

			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* No separate index on CharacterID: the composite key leads with it, so
			 * "every dialogue mask for this character" — the character-load query, and the only
			 * read this table serves — is already a prefix scan of the primary key. */

			/* NoAction, matching the other per-character tables. Characters are soft-deleted, so a
			 * cascade would never fire; making it CASCADE here would only differ if something ever
			 * hard-deleted a character row, and at that point every sibling table has the same
			 * question and should answer it the same way. */
			builder.HasOne(e => e.Character)
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
