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

			// The load path fetches every item a character owns in one query.
			builder.HasIndex(e => e.CharacterID);

			// "Find all characters holding item X".
			builder.HasIndex(e => e.TemplateID);

			builder.HasOne(e => e.Character)
				.WithMany(c => c.Items)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
