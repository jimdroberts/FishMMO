using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="ArenaMatchEntity"/>.
	/// </summary>
	public class ArenaMatchEntityConfiguration : IEntityTypeConfiguration<ArenaMatchEntity>
	{
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
		}
	}
}
