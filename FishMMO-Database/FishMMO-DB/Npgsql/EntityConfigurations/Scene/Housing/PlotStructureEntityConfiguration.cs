using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PlotStructureEntity with explicit indexes and constraints.
	/// </summary>
	public class PlotStructureEntityConfiguration : IEntityTypeConfiguration<PlotStructureEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PlotStructureEntity> builder)
		{
			builder.ToTable("plot_structures");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.PlotID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			/* The only query this table serves: everything standing on one plot, fetched when the
			 * plot is resolved on scene load. Nothing ever asks for a structure on its own. */
			builder.HasIndex(e => e.PlotID);

			/* Cascading. A structure cannot outlive the plot it stands on — there would be nothing
			 * to position it relative to, and it would be spawned by nobody and owned by nobody. */
			builder.HasOne<PlotEntity>()
				.WithMany()
				.HasForeignKey(e => e.PlotID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
