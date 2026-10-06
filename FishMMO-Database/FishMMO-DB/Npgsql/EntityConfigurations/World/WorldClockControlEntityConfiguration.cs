using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="WorldClockControlEntity"/>: the single-row
	/// <c>world_clock_control</c> table.
	/// </summary>
	/// <remarks>
	/// The check constraints are the backstop for writes that do not come through
	/// <c>WorldClockControlService</c> (psql, a future tool): the service validates the same bounds
	/// first and says why, so in practice these only ever refuse a hand-written statement.
	/// </remarks>
	public class WorldClockControlEntityConfiguration : IEntityTypeConfiguration<WorldClockControlEntity>
	{
		/// <summary>Highest pace a write may set. Mirrors <c>WorldClockControlService.MaxRate</c>.</summary>
		private const string MaxRateSql = "100000";

		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<WorldClockControlEntity> builder)
		{
			builder.ToTable("world_clock_control");

			builder.HasKey(e => e.ID);

			// Never generated: the row is always id 1, written by INSERT … ON CONFLICT (id).
			builder.Property(e => e.ID)
				.ValueGeneratedNever();

			builder.Property(e => e.BaseWorldMs).IsRequired();
			builder.Property(e => e.BaseReferenceMs).IsRequired();
			builder.Property(e => e.Rate).IsRequired();
			builder.Property(e => e.ResumeRate)
				.IsRequired()
				.HasDefaultValue(1.0);
			builder.Property(e => e.EpochUnixSeconds).IsRequired();
			builder.Property(e => e.Revision).IsRequired();
			builder.Property(e => e.UpdatedBy);
			builder.Property(e => e.UpdatedAt);
			builder.Property(e => e.RequestKey);

			builder.HasCheckConstraint("ck_world_clock_control_single_row", "id = 1");
			builder.HasCheckConstraint("ck_world_clock_control_base_world_ms", "base_world_ms >= 0");
			builder.HasCheckConstraint("ck_world_clock_control_rate", $"rate >= 0 AND rate <= {MaxRateSql}");
			builder.HasCheckConstraint("ck_world_clock_control_resume_rate", $"resume_rate > 0 AND resume_rate <= {MaxRateSql}");
		}
	}
}
