using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterPortalEntity"/>.
	/// </summary>
	public class CharacterPortalEntityConfiguration : IEntityTypeConfiguration<CharacterPortalEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterPortalEntity> builder)
		{
			builder.ToTable("character_portals");

			/* Composite key, same reasoning as character_waypoints: (character, scene, page) IS
			 * the identity, the upsert arbitrates on it, and the load fetch is a prefix scan. */
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

			/* Cascade rather than NoAction: the character delete path names every sub-entity
			 * service it clears, and a table it does not know about would otherwise block a hard
			 * delete with a foreign key violation. A portal activation is meaningless without its
			 * character, so the database removes it with the row. */
			builder.HasOne(e => e.Character)
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
