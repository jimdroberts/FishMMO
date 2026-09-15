using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterItemEntity"/>.
	/// </summary>
	public class CharacterItemEntityConfiguration : IEntityTypeConfiguration<CharacterItemEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterItemEntity> builder)
		{
			builder.ToTable("character_item");

			// The item's durable identity. Database-generated on first insert; the caller writes
			// the returned value back onto the runtime item and quotes it on every later write.
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Container)
				.IsRequired();

			builder.Property(e => e.Slot)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Seed)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.Amount)
				.IsRequired()
				.HasDefaultValue(1u);

			/* One item per slot per container per character.
			 *
			 * A UNIQUE INDEX rather than a UNIQUE CONSTRAINT, and it is NOT the upsert's conflict
			 * target — the conflict target is the primary key, because the row belongs to the item.
			 * This index exists to stop two items claiming one slot, which is a corruption the
			 * in-memory container cannot represent.
			 *
			 * Because it is checked per row rather than per statement, a straight SWAP of two items
			 * between two slots would trip it halfway through. CharacterItemService never emits one:
			 * SaveSnapshotAsync vacates the character's rows for the container it is writing before
			 * it re-inserts them, so no intermediate state has two rows on one slot. Any future
			 * statement that moves several items at once must do the same, or make this deferrable.
			 */
			builder.HasIndex(e => new { e.CharacterID, e.Container, e.Slot })
				.IsUnique();

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

			// "Find all characters holding item X".
			builder.HasIndex(e => e.TemplateID);

			builder.HasOne(e => e.Character)
				.WithMany(c => c.Items)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
