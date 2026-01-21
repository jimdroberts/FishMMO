using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterGuildEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterGuildEntityConfiguration : IEntityTypeConfiguration<CharacterGuildEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterGuildEntity> builder)
		{
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

			// Unique constraint: one character can only be in one guild
			builder.HasIndex(e => e.CharacterID)
				.IsUnique()
				.HasDatabaseName("IX_CharacterGuild_CharacterID_Unique");

			// Performance indexes for lookups

			builder.HasIndex(e => e.GuildID)
				.HasDatabaseName("IX_CharacterGuild_GuildID");

			// Foreign key relationship to Guild
			builder.HasOne(e => e.Guild)
				.WithMany(g => g.Characters)
				.HasForeignKey(e => e.GuildID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}