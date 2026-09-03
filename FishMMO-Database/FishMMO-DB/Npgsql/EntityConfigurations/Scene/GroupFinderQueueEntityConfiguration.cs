using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="GroupFinderQueueEntity"/> with explicit indexes and constraints.
	/// </summary>
	public class GroupFinderQueueEntityConfiguration : IEntityTypeConfiguration<GroupFinderQueueEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<GroupFinderQueueEntity> builder)
		{
			builder.ToTable("group_finder_queue");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.WorldServerID)
				.IsRequired();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.Difficulty)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.Status)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.PartyID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.InstanceID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.LastPulse)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.Property(e => e.TimeMatched)
				.IsRequired(false);

			// One row per character: the queue is not a wishlist, and the matcher relies on it —
			// a character appearing twice could be placed in two groups at once.
			builder.HasIndex(e => e.CharacterID)
				.IsUnique();

			/* The matcher's hot path: every pump on every scene server with waiters asks, for one
			 * (world, dungeon, difficulty), how many are waiting and then tries to take the oldest
			 * N of them. Status leads the trailing columns so Matched rows fall out of the scan
			 * immediately, and time_created ends it so the ORDER BY is served from the index. */
			builder.HasIndex(e => new { e.WorldServerID, e.SceneName, e.Difficulty, e.Status, e.TimeCreated });

			// The stale-row reaper: rows whose heartbeat stopped.
			builder.HasIndex(e => e.LastPulse);

			/* Cascades with the character. A deleted character cannot be waiting for anything,
			 * and without this a hard delete would be refused by the row it left behind. */
			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
