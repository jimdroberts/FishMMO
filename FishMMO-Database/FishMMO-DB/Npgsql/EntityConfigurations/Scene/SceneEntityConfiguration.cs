using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for SceneEntity with explicit indexes and constraints.
	/// </summary>
	public class SceneEntityConfiguration : IEntityTypeConfiguration<SceneEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<SceneEntity> builder)
		{
			builder.ToTable("scenes");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.SceneServerID)
				.IsRequired();

			builder.Property(e => e.WorldServerID)
				.IsRequired();

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.SceneHandle)
				.IsRequired();

			builder.Property(e => e.SceneStatus)
				.IsRequired();

			builder.Property(e => e.SceneType)
				.IsRequired();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.CharacterCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.PartyID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.Difficulty)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.IsPrivate)
				.IsRequired()
				.HasDefaultValue(false);

			// Performance indexes for common queries
			builder.HasIndex(e => e.SceneServerID);

			builder.HasIndex(e => e.WorldServerID);

			builder.HasIndex(e => e.CharacterID);

			// Covers the party-membership half of instance resolution: a member asks "does my
			// party already hold one" on every dungeon entrance click. Filtered because party 0
			// is "no party" rather than a party, and indexing every ungrouped instance under one
			// value would make the entry a scan of its own.
			builder.HasIndex(e => e.PartyID)
				.HasFilter("party_id <> 0");

			builder.HasIndex(e => new { e.SceneType, e.SceneStatus });

			builder.HasIndex(e => e.TimeCreated);

			// Performance index for scene queue dequeue (hot path: DequeueAsync)
			// Covers WHERE scene_status = 0 ORDER BY time_created, id LIMIT 1
			builder.HasIndex(e => new { e.SceneStatus, e.TimeCreated, e.ID })
				.HasFilter("scene_status = 0");

			// Performance index for scene ready-claim (hot path: SetReadyAsync)
			// Covers WHERE world_server_id = ? AND scene_name = ? AND scene_status = 1 ORDER BY time_created, id LIMIT 1
			builder.HasIndex(e => new { e.WorldServerID, e.SceneName, e.TimeCreated, e.ID })
				.HasFilter("scene_status = 1");

			/* Performance index for the dungeon finder's joinable list (hot path:
			 * FetchJoinableInstancesAsync). Every open of the finder, and every Refresh, asks for
			 * the public non-full instances of one dungeon at one difficulty on one world server.
			 * Unfiltered on status because the list deliberately includes instances that are still
			 * loading — a player who joins one during its load is queued into it rather than
			 * being told it does not exist yet. */
			builder.HasIndex(e => new { e.WorldServerID, e.SceneName, e.Difficulty, e.SceneStatus });
		}
	}
}