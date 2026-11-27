using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterMercenaryEntity"/>.
	/// </summary>
	public class CharacterMercenaryEntityConfiguration : IEntityTypeConfiguration<CharacterMercenaryEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterMercenaryEntity> builder)
		{
			builder.ToTable("character_mercenaries");

			// One mercenary per character: the character IS the key, and the upsert arbitrates on it.
			builder.HasKey(e => e.CharacterID);

			builder.Property(e => e.CharacterID)
				.IsRequired()
				.ValueGeneratedNever();

			builder.Property(e => e.KitID).IsRequired();

			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(32);

			builder.Property(e => e.Resting).IsRequired().HasDefaultValue(false);
			builder.Property(e => e.Stance).IsRequired().HasDefaultValue((byte)1);
			builder.Property(e => e.AppearanceSeed).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.NameSeed).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.NameGender).IsRequired().HasDefaultValue((byte)0);
			builder.Property(e => e.NameBiomeID).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.NameVariant).IsRequired().HasDefaultValue((byte)0);
			builder.Property(e => e.RaceTemplateID).HasColumnName("race_template_id").IsRequired().HasDefaultValue(0);

			builder.Property(e => e.ResourceTemplateIDs).HasColumnName("resource_template_ids").IsRequired().HasColumnType("integer[]").HasDefaultValueSql("'{}'");
			builder.Property(e => e.ResourceValues).IsRequired().HasColumnType("real[]").HasDefaultValueSql("'{}'");
			builder.Property(e => e.BuffTemplateIDs).HasColumnName("buff_template_ids").IsRequired().HasColumnType("integer[]").HasDefaultValueSql("'{}'");
			builder.Property(e => e.BuffRemaining).IsRequired().HasColumnType("real[]").HasDefaultValueSql("'{}'");
			builder.Property(e => e.BuffTickTime).IsRequired().HasColumnType("real[]").HasDefaultValueSql("'{}'");
			builder.Property(e => e.BuffStacks).IsRequired().HasColumnType("integer[]").HasDefaultValueSql("'{}'");
			builder.Property(e => e.BuffTickCount).IsRequired().HasColumnType("integer[]").HasDefaultValueSql("'{}'");

			builder.Property(e => e.TimeHired)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");
			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.HasOne(e => e.Character)
				.WithOne()
				.HasForeignKey<CharacterMercenaryEntity>(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
