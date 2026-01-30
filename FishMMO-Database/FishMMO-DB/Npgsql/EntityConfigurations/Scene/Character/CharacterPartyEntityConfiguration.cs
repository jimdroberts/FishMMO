using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterPartyEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterPartyEntityConfiguration : IEntityTypeConfiguration<CharacterPartyEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterPartyEntity> builder)
		{
			builder.ToTable("character_party");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.PartyID)
				.IsRequired();

			builder.Property(e => e.Rank)
				.IsRequired()
				.HasDefaultValue((byte)0);

			builder.Property(e => e.HealthPCT)
				.IsRequired()
				.HasDefaultValue(1.0f);

			// Unique constraint: one character can only be in one party
			builder.HasIndex(e => e.CharacterID)
				.IsUnique();

			// Performance indexes for lookups

			builder.HasIndex(e => e.PartyID);

			// Foreign key relationship to Party
			builder.HasOne(e => e.Party)
				.WithMany(p => p.Characters)
				.HasForeignKey(e => e.PartyID)
				.OnDelete(DeleteBehavior.NoAction);

			// Foreign key relationship to Character
			// Deleting a character must remove the character's party membership row.
			builder.HasOne(e => e.Character)
				.WithOne(c => c.Party)
				.HasForeignKey<CharacterPartyEntity>(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}