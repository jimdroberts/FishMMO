using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="ArenaMatchEntity"/>.
	/// </summary>
	public class ArenaMatchEntityConfiguration : IEntityTypeConfiguration<ArenaMatchEntity>
	{
		/// <summary>
		/// "Not finished" as SQL text: every status before <see cref="ArenaMatchStatus.Ended"/>.
		/// The filter of the unfinished-match index, and the condition the queries it serves
		/// write verbatim.
		/// </summary>
		/// <remarks>
		/// A literal in those queries rather than a parameter. The planner may use a partial index
		/// only when it can prove the query's condition implies the index's filter, and it can
		/// prove nothing about a parameter once a plan is cached for reuse.
		/// </remarks>
		public static readonly string UnfinishedFilter = $"status < {(int)ArenaMatchStatus.Ended}";

		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<ArenaMatchEntity> builder)
		{
			builder.ToTable("arena_match");

			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.WorldServerID).IsRequired();
			builder.Property(e => e.InstanceID).IsRequired();
			builder.Property(e => e.SceneName).IsRequired().HasMaxLength(100);
			builder.Property(e => e.TemplateID).IsRequired();
			builder.Property(e => e.Format).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.TeamCount).IsRequired();
			builder.Property(e => e.TeamSize).IsRequired();
			builder.Property(e => e.Status).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.WinnerTeam).IsRequired().HasDefaultValue(-1);
			builder.Property(e => e.Ranked).IsRequired().HasDefaultValue(false);
			builder.Property(e => e.SeasonID).IsRequired().HasDefaultValue(0L);
			builder.Property(e => e.BackfillUntilUtc).IsRequired(false);
			builder.Property(e => e.TimeStarted).IsRequired(false);
			builder.Property(e => e.TimeEnded).IsRequired(false);

			// The hosting scene server resolves a match from the instance it just loaded.
			builder.HasIndex(e => e.InstanceID).IsUnique();

			// Live-match lookups and history queries by world.
			builder.HasIndex(e => new { e.WorldServerID, e.Status });

			// Backfill: live matches of one arena at one format with a window still open.
			builder.HasIndex(e => new { e.WorldServerID, e.SceneName, e.Format, e.Status });

			/* Unfinished matches, oldest first: the abandoned-match sweep every scene server runs
			 * (ArenaMatchService.CancelAbandonedAsync). Nothing deletes finished matches — they are
			 * the history — so without the filter that sweep, which is not scoped to a world, read
			 * a table that only grows. Filtered on the same text the sweep's WHERE uses, so the
			 * planner can prove the index applies; the set it covers is the matches still open. */
			builder.HasIndex(e => e.TimeCreated)
				.HasFilter(UnfinishedFilter)
				.HasDatabaseName("ix_arena_match_unfinished_time_created");
		}
	}
}
