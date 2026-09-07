using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterWaypointEntity"/>.
	/// </summary>
	public class CharacterWaypointEntityConfiguration : IEntityTypeConfiguration<CharacterWaypointEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterWaypointEntity> builder)
		{
			builder.ToTable("character_waypoints");

			/* Composite key: (character, scene, page) IS the identity, it is what the upsert
			 * arbitrates on, and the character-load fetch is a prefix scan of it. A surrogate
			 * bigserial would add a sequence and a second index to a table that is one row per
			 * character per scene page. Same reasoning as character_dialogue_choices. */
			builder.HasKey(e => new { e.CharacterID, e.SceneName, e.Page });

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(64);

			builder.Property(e => e.Page)
				.IsRequired();

			builder.Property(e => e.Mask)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.HasOne(e => e.Character)
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
