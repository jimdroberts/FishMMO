using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for GuildLogEntity with explicit indexes and constraints.
	/// </summary>
	public class GuildLogEntityConfiguration : IEntityTypeConfiguration<GuildLogEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<GuildLogEntity> builder)
		{
			builder.ToTable("guild_log");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.GuildID)
				.IsRequired();

			builder.Property(e => e.EventType)
				.IsRequired()
				.HasDefaultValue((byte)0);

			builder.Property(e => e.Detail)
				.HasMaxLength(64);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* The only read this table serves is "the most recent N rows for one guild", so the
			 * index is built for exactly that: guild first to narrow, then time. Left ascending —
			 * PostgreSQL walks a b-tree in either direction at the same cost, so the descending
			 * ORDER BY the read uses is served by this index as it stands. */
			builder.HasIndex(e => new { e.GuildID, e.TimeCreated });

			/* CASCADE, unlike the membership relationship beside it. A disbanded guild's log has
			 * nothing left to describe, and leaving the rows behind would accumulate history for
			 * guilds that no longer exist with nothing able to read or delete it. */
			builder.HasOne(e => e.Guild)
				.WithMany()
				.HasForeignKey(e => e.GuildID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
