using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PlotUpdateEntity with explicit indexes and constraints.
	/// </summary>
	public class PlotUpdateEntityConfiguration : IEntityTypeConfiguration<PlotUpdateEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PlotUpdateEntity> builder)
		{
			builder.ToTable("plot_updates");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.PlotID)
				.IsRequired();

			builder.Property(e => e.LastUpdate)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* One update row per plot. The UPSERT that records a change names this constraint in
			 * its ON CONFLICT clause, so it is load-bearing rather than merely tidy. */
			builder.HasIndex(e => e.PlotID)
				.IsUnique();

			/* Composite index for the polling read:
			 *   WHERE last_update >= @lastFetch AND plot_id = ANY(@plotIds)
			 *
			 * plot_id leads, for the same reason it does on guild_updates: the ANY predicate is the
			 * selective one, so the planner seeks once per id and range-scans last_update inside it.
			 * Ordered the other way the leading predicate matches most of the table and the index
			 * goes unused. */
			builder.HasIndex(e => new { e.PlotID, e.LastUpdate });

			/* Cascading, unlike the owner columns on the plot itself. This one can carry a key —
			 * plot_id is always a real plot, never a zero sentinel — and an update row that outlived
			 * its plot would be polled forever and never resolve to anything. */
			builder.HasOne<PlotEntity>()
				.WithMany()
				.HasForeignKey(e => e.PlotID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
