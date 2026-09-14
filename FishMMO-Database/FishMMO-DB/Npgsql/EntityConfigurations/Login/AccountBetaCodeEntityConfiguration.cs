using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="AccountBetaCodeEntity"/>.
	/// </summary>
	public class AccountBetaCodeEntityConfiguration : IEntityTypeConfiguration<AccountBetaCodeEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<AccountBetaCodeEntity> builder)
		{
			builder.ToTable("account_beta_codes");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* No concurrency token: a link is written once and never edited. */

			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.BetaCodeID)
				.IsRequired();

			builder.Property(e => e.Code)
				.IsRequired()
				.HasMaxLength(14);

			builder.Property(e => e.Program)
				.IsRequired()
				.HasMaxLength(32);

			builder.Property(e => e.RedeemedUtc)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* One link per account per code, and the arbiter of the redemption insert's
			 * ON CONFLICT (account_name, beta_code_id) DO NOTHING. It is what makes a double
			 * submit by one account roll back its second use instead of spending two. */
			builder.HasIndex(e => new { e.AccountName, e.BetaCodeID })
				.IsUnique();

			// The access check and the account's own list.
			builder.HasIndex(e => e.AccountName);

			// "Who redeemed this code?"
			builder.HasIndex(e => e.BetaCodeID);

			/* No foreign key to accounts or beta_codes, deliberately — the link is permanent
			 * evidence of admission and must survive anything done to either side. See the
			 * entity's remarks. */
		}
	}
}
