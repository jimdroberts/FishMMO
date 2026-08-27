using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PlotEntity with explicit indexes and constraints.
	/// </summary>
	public class PlotEntityConfiguration : IEntityTypeConfiguration<PlotEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PlotEntity> builder)
		{
			builder.ToTable("plots");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.WorldServerID)
				.IsRequired();

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(100);

			/* 64 characters, matching PlotIdentity.MaxPlotKeyLength. The shared type rejects
			 * anything longer while the designer still has the scene open; this is the backstop for
			 * a writer that did not go through it. */
			builder.Property(e => e.PlotKey)
				.IsRequired()
				.HasMaxLength(64);

			builder.Property(e => e.OwnerCharacterID)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.OwnerGuildID)
				.IsRequired()
				.HasDefaultValue(0L);

			/* The plot's identity, and the constraint that makes claiming safe.
			 *
			 * Registration inserts a row the first time a scene server loads a scene containing an
			 * authored foundation. Every channel of that scene does the same thing, on a different
			 * server, at roughly the same moment — so without uniqueness the same plot quietly
			 * becomes several rows with separate owners, and which one a player sees depends on
			 * which channel they walked into. Unique here turns that race into one winner and a
			 * conflict the loser can read.
			 *
			 * The world server leads. Every query here names one — a scene server only registers or
			 * reads the land of the world whose scene it is hosting — so leading with that equality
			 * predicate lets the planner seek straight to that world's rows. */
			builder.HasIndex(e => new { e.WorldServerID, e.SceneName, e.PlotKey })
				.IsUnique();

			/* "Which plots do I own", asked per character on login and whenever the housing UI
			 * opens. Filtered, because unowned land is the common case on a young server and every
			 * one of those rows carries the same zero: indexing them would be paying to store a
			 * value this query never searches for. */
			builder.HasIndex(e => e.OwnerCharacterID)
				.HasFilter("owner_character_id <> 0");

			// The same question asked for a guild, and the sweep that releases plots when one disbands.
			builder.HasIndex(e => e.OwnerGuildID)
				.HasFilter("owner_guild_id <> 0");

			/* The tax sweep's only query: owned plots whose payment has come due. Filtered to rows
			 * that have a due date at all, because unowned land is not taxed and is the majority of
			 * the table on any server with room left to build on. */
			builder.HasIndex(e => e.TaxDueUtc)
				.HasFilter("tax_due_utc IS NOT NULL");
		}
	}
}
