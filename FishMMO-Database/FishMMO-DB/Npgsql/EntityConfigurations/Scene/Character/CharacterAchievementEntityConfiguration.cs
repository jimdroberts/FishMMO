using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterAchievementEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterAchievementEntityConfiguration : IEntityTypeConfiguration<CharacterAchievementEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterAchievementEntity> builder)
		{
			builder.ToTable("character_achievements");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Tier)
				.IsRequired()
				.HasDefaultValue((byte)0);

			builder.Property(e => e.Value)
				.IsRequired()
				.HasDefaultValue(0u);

			// Unique constraint: one achievement template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
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

			/* Leaderboards: one achievement template's values, best first (ILeaderboardService). The
			 * board orders by value DESC within a template and a standing counts the rows above
			 * one value, so both are a range scan of this index rather than a read of every
			 * character's every achievement. The save only writes rows whose value moved, and
			 * those are exactly the rows whose place on a board moved. */
			builder.HasIndex(e => new { e.TemplateID, e.Value });

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Achievements)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}