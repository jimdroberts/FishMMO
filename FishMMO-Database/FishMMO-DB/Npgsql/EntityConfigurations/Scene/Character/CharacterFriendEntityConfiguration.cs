using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterFriendEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterFriendEntityConfiguration : IEntityTypeConfiguration<CharacterFriendEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterFriendEntity> builder)
		{
			builder.ToTable("character_friends");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.FriendCharacterID)
				.IsRequired();

			builder.Property(e => e.IsBlocked)
				.IsRequired()
				.HasDefaultValue(false);

			// Unique constraint: one friendship relationship per character pair
			builder.HasIndex(e => new { e.CharacterID, e.FriendCharacterID })
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

			// Performance index for reverse friend lookup (who has friended this character)
			builder.HasIndex(e => e.FriendCharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Friends)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);

			// FK to CharacterEntity for the friend character.
			// NOTE: No navigation property exists on CharacterEntity for
			// this relationship (no "FriendsOf" collection). If reverse
			// lookups ("who has this character as a friend?") become a
			// performance concern, add a navigation property and migration.
			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.FriendCharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}