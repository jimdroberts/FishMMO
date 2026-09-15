using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterItemCooldownEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterItemCooldownEntityConfiguration : IEntityTypeConfiguration<CharacterItemCooldownEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterItemCooldownEntity> builder)
		{
			builder.ToTable("character_itemcooldowns");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Category)
				.IsRequired();

			builder.Property(e => e.CooldownEnd)
				.IsRequired()
				.HasDefaultValue(0d);

			// Unique constraint: one cooldown category per character
			builder.HasIndex(e => new { e.CharacterID, e.Category })
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

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.ItemCooldowns)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}