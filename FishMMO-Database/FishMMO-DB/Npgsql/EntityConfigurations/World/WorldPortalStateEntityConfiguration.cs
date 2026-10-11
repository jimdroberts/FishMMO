using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="WorldPortalStateEntity"/>.
	/// </summary>
	public class WorldPortalStateEntityConfiguration : IEntityTypeConfiguration<WorldPortalStateEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<WorldPortalStateEntity> builder)
		{
			builder.ToTable("world_portal_state");

			/* (scene, index) is the identity and the scene load reads by scene, a prefix of the key. */
			builder.HasKey(e => new { e.SceneName, e.PortalIndex });

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(64);

			builder.Property(e => e.PortalIndex)
				.IsRequired();

			builder.Property(e => e.Permanent)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.ActiveUntil)
				.IsRequired()
				.HasDefaultValueSql("'1970-01-01 00:00:00'::timestamp without time zone");

			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.HasCheckConstraint("ck_world_portal_state_portal_index", "portal_index >= 0");
		}
	}
}
