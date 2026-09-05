using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PlotAccessEntity with explicit indexes and constraints.
	/// </summary>
	public class PlotAccessEntityConfiguration : IEntityTypeConfiguration<PlotAccessEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PlotAccessEntity> builder)
		{
			builder.ToTable("plot_access");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.PlotID)
				.IsRequired();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Permissions)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.GrantedByCharacterID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.TimeGranted)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* One grant per person per plot, and the constraint the grant UPSERT names in its
			 * ON CONFLICT clause — so it is load-bearing rather than merely tidy.
			 *
			 * Without it, re-granting would append instead of replacing, and a revoke that deleted
			 * one row would leave the others standing. The friend would keep exactly the permissions
			 * the owner had just taken away, and the owner would have watched it succeed. */
			builder.HasIndex(e => new { e.PlotID, e.CharacterID })
				.IsUnique();

			/* "Which houses have I been let into", asked when the housing UI opens and by the sweep
			 * that evicts a character whose access went away while they were standing in one. */
			builder.HasIndex(e => e.CharacterID);

			/* Cascading. A grant on a plot that no longer exists can never be evaluated and can
			 * never be revoked; leaving it would be leaving a key to a demolished house. */
			builder.HasOne<PlotEntity>()
				.WithMany()
				.HasForeignKey(e => e.PlotID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
