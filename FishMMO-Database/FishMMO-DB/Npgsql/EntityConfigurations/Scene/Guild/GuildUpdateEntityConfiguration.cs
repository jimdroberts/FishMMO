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

			// Composite index for guild update fetch queries (FetchAsync hot path)
			// Optimizes WHERE last_update >= @lastFetch AND guild_id IN (@guildIds)
			// Using (last_update, guild_id) allows efficient range scan on last_update
			// followed by guild_id filtering without additional lookups
			builder.HasIndex(e => new { e.LastUpdate, e.GuildID });
		}
	}
}