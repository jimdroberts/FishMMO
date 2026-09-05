using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PlotVaultEntity with explicit indexes and constraints.
	/// </summary>
	public class PlotVaultEntityConfiguration : IEntityTypeConfiguration<PlotVaultEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PlotVaultEntity> builder)
		{
			builder.ToTable("plot_vault");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.TemplateID)
				.IsRequired();

			builder.Property(e => e.Amount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.OriginalPlotID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.StoredAtUtc)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.Property(e => e.BaseFee)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.FeeRatePerDay)
				.IsRequired()
				.HasDefaultValue(0f);

			/* The only query this table serves: everything one character is owed. Nothing ever asks
			 * for a vault row on its own, or for every vault row across the server. */
			builder.HasIndex(e => e.CharacterID);

			/* Deliberately no foreign key to plots, unlike every other table hanging off one.
			 *
			 * OriginalPlotID is a label saying where the furniture came from, and the plot it names
			 * is being handed to somebody else — that is why these rows exist at all. A cascading
			 * key would empty the previous owner's vault the moment the new owner's house was
			 * cleared, which is exactly the loss the vault was built to prevent.
			 *
			 * The character key is real, because a vault belongs to somebody and characters are
			 * soft-deleted, so the row it points at does not go away. */
			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
