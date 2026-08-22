using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for GuildUpdateEntity with explicit indexes and constraints.
	/// </summary>
	public class GuildUpdateEntityConfiguration : IEntityTypeConfiguration<GuildUpdateEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<GuildUpdateEntity> builder)
		{
			builder.ToTable("guild_updates");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.GuildID)
				.IsRequired();

			builder.Property(e => e.LastUpdate)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			// Unique constraint on guild ID (one update record per guild)
			builder.HasIndex(e => e.GuildID)
				.IsUnique();

			/* Composite index for the FetchAsync hot path:
			 *   WHERE last_update >= @lastFetch AND guild_id = ANY(@guildIds)
			 *
			 * guild_id leads. The equality/ANY predicate is the selective one and must come
			 * first so the planner can seek once per id and then range-scan last_update within
			 * it. Ordered (last_update, guild_id) the leading predicate is an open-ended range
			 * matching most of the table, the id filter cannot seek at all, and the planner
			 * falls back to the unique guild_id index — leaving this one dead weight. */
			builder.HasIndex(e => new { e.GuildID, e.LastUpdate });

			/* Foreign key to the guild, cascading.
			 *
			 * There was no relationship here at all, so guild_id was a bare bigint with no
			 * referential integrity: an update row outlived the guild it described, and cleanup
			 * depended on a manual best-effort delete that is only logged on failure. */
			builder.HasOne<GuildEntity>()
				.WithMany()
				.HasForeignKey(e => e.GuildID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}