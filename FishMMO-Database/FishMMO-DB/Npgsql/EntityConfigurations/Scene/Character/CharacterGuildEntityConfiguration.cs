using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterGuildEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterGuildEntityConfiguration : IEntityTypeConfiguration<CharacterGuildEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterGuildEntity> builder)
		{
			builder.ToTable("character_guild");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.GuildID)
				.IsRequired();

			builder.Property(e => e.Rank)
				.IsRequired()
				.HasDefaultValue((byte)0);

			builder.Property(e => e.Location)
				.HasMaxLength(200);

			builder.Property(e => e.PublicNote)
				.HasMaxLength(128)
				.HasDefaultValue(string.Empty);

			builder.Property(e => e.OfficerNote)
				.HasMaxLength(128)
				.HasDefaultValue(string.Empty);

			// Unique constraint: one character can only be in one guild
			builder.HasIndex(e => e.CharacterID)
				.IsUnique();

			// Performance indexes for lookups

			builder.HasIndex(e => e.GuildID);

			// Foreign key relationship to Guild
			builder.HasOne(e => e.Guild)
				.WithMany(g => g.Characters)
				.HasForeignKey(e => e.GuildID)
				.OnDelete(DeleteBehavior.NoAction);

			// Foreign key relationship to Character
			// Deleting a character must remove the character's guild membership row.
			builder.HasOne(e => e.Character)
				.WithOne(c => c.Guild)
				.HasForeignKey<CharacterGuildEntity>(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}