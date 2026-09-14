using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="BetaCodeEntity"/>.
	/// </summary>
	public class BetaCodeEntityConfiguration : IEntityTypeConfiguration<BetaCodeEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<BetaCodeEntity> builder)
		{
			builder.ToTable("beta_codes");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* For staff edits made through EF. Redemption is guarded by its own conditional
			 * UPDATE and does not need this; see the entity's remarks. */
			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			// XXXX-XXXX-XXXX.
			builder.Property(e => e.Code)
				.IsRequired()
				.HasMaxLength(14);

			/* The redemption lookup, and the arbiter of the mint's ON CONFLICT (code) DO NOTHING:
			 * a freshly generated code that collides is skipped and regenerated rather than
			 * failing the whole batch. Change these columns and minting fails at runtime. */
			builder.HasIndex(e => e.Code)
				.IsUnique();

			builder.Property(e => e.Program)
				.IsRequired()
				.HasMaxLength(32);

			builder.Property(e => e.MaxUses)
				.IsRequired()
				.HasDefaultValue(1);

			builder.Property(e => e.UseCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.ExpiresUtc);

			builder.Property(e => e.RevokedUtc);

			builder.Property(e => e.RevokedBy)
				.HasMaxLength(50);

			builder.Property(e => e.CreatedBy)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.CreatedUtc)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.Property(e => e.Note)
				.HasMaxLength(256);

			// The panel's code list: one program, newest batch first.
			builder.HasIndex(e => new { e.Program, e.CreatedUtc });

			/* No foreign keys: a code references nothing, and links point at it without a
			 * constraint — see AccountBetaCodeEntity. */
		}
	}
}
