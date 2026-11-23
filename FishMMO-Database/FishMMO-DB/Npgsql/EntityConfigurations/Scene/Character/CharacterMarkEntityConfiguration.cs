using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterMarkEntity"/>.
	/// </summary>
	public class CharacterMarkEntityConfiguration : IEntityTypeConfiguration<CharacterMarkEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterMarkEntity> builder)
		{
			builder.ToTable("character_marks");

			// One mark per character: the character IS the key, and the upsert arbitrates on it.
			builder.HasKey(e => e.CharacterID);

			builder.Property(e => e.CharacterID)
				.IsRequired()
				.ValueGeneratedNever();

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(64);

			builder.Property(e => e.SceneID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.X).IsRequired();
			builder.Property(e => e.Y).IsRequired();
			builder.Property(e => e.Z).IsRequired();
			builder.Property(e => e.Heading).IsRequired();

			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.HasOne(e => e.Character)
				.WithOne()
				.HasForeignKey<CharacterMarkEntity>(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
