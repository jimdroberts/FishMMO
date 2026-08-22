using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for GuildRankEntity with explicit indexes and constraints.
	/// </summary>
	public class GuildRankEntityConfiguration : IEntityTypeConfiguration<GuildRankEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<GuildRankEntity> builder)
		{
			builder.ToTable("guild_rank");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.GuildID)
				.IsRequired();

			builder.Property(e => e.RankOrder)
				.IsRequired();

			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(24);

			builder.Property(e => e.Permissions)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.Version)
				.IsRequired()
				.HasDefaultValue(1L);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* Unique, not merely indexed. The ladder is a set of DISTINCT positions, and two rows
			 * sharing an order would make "at or above your rank" ambiguous for the one comparison
			 * the whole permission model rests on. It is also the conflict target the idempotent
			 * seed relies on. */
			builder.HasIndex(e => new { e.GuildID, e.RankOrder })
				.IsUnique();

			/* CASCADE. A rank row describes a guild and means nothing without it; the membership
			 * relationship next door is NoAction because a membership row is a fact about a
			 * character too. */
			builder.HasOne(e => e.Guild)
				.WithMany(g => g.Ranks)
				.HasForeignKey(e => e.GuildID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
