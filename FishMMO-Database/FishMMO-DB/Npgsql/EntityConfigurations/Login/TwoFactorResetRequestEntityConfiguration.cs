using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="TwoFactorResetRequestEntity"/>.
	/// </summary>
	public class TwoFactorResetRequestEntityConfiguration : IEntityTypeConfiguration<TwoFactorResetRequestEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<TwoFactorResetRequestEntity> builder)
		{
			builder.ToTable("two_factor_reset_requests");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(50);

			/* Stored as the enum's integer: 0 Pending, 1 Cancelled, 2 Completed. The partial index
			 * below is filtered on the literal 0, so Pending's number is fixed. */
			builder.Property(e => e.Status)
				.IsRequired()
				.HasConversion<int>()
				.HasDefaultValue(TwoFactorResetStatus.Pending);

			builder.Property(e => e.RequestedUtc)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.Property(e => e.EffectiveUtc)
				.IsRequired();

			builder.Property(e => e.RequestedIp)
				.HasMaxLength(64);

			builder.Property(e => e.ResolvedUtc);

			builder.Property(e => e.ResolvedBy)
				.HasMaxLength(50);

			builder.Property(e => e.ShortenedUtc);

			builder.Property(e => e.ShortenedBy)
				.HasMaxLength(50);

			builder.Property(e => e.StaffReason)
				.HasMaxLength(256);

			/* At most one pending request per account, enforced here rather than by a read in the
			 * service, so two simultaneous requests cannot both insert. It is also the arbiter of
			 * RequestAsync's ON CONFLICT (account_name) WHERE status = 0 DO NOTHING — PostgreSQL
			 * only infers a partial index when the statement's predicate matches this filter, so
			 * the two must change together. Resolved rows are outside the filter and pile up as
			 * history without constraint. */
			builder.HasIndex(e => e.AccountName)
				.IsUnique()
				.HasFilter("status = 0")
				.HasDatabaseName("ix_two_factor_reset_requests_one_pending");

			// The staff queue: pending requests, soonest to take effect first.
			builder.HasIndex(e => new { e.Status, e.EffectiveUtc });

			/* No foreign key to accounts, deliberately — see the entity's remarks. */
		}
	}
}
