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

			// Performance index for character friend list queries
			builder.HasIndex(e => e.CharacterID);

			// Performance index for reverse friend lookup (who has friended this character)
			builder.HasIndex(e => e.FriendCharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Friends)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);

			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.FriendCharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}