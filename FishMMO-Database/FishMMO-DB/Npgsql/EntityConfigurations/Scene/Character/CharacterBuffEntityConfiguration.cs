using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for CharacterBuffEntity with explicit indexes and constraints.
	/// </summary>
	public class CharacterBuffEntityConfiguration : IEntityTypeConfiguration<CharacterBuffEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterBuffEntity> builder)
		{
			builder.ToTable("character_buffs");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.RemainingTime)
				.IsRequired()
				.HasDefaultValue(0f);

			builder.Property(e => e.TickTime)
				.IsRequired()
				.HasDefaultValue(0f);

			builder.Property(e => e.Stacks)
				.IsRequired()
				.HasDefaultValue(1);

			builder.Property(e => e.TickCount)
				.IsRequired()
				.HasDefaultValue(0);

			// Unique constraint: one buff template per character
			builder.HasIndex(e => new { e.CharacterID, e.TemplateID })
				.IsUnique();

			// Performance index for character buff queries (hot path for character load/update)
			builder.HasIndex(e => e.CharacterID);

			// Foreign key relationship
			builder.HasOne(e => e.Character)
				.WithMany(c => c.Buffs)
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}