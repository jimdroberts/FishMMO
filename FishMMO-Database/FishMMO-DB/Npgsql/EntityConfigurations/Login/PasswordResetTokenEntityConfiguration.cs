using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="PasswordResetTokenEntity"/>.
	/// </summary>
	public class PasswordResetTokenEntityConfiguration : IEntityTypeConfiguration<PasswordResetTokenEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PasswordResetTokenEntity> builder)
		{
			builder.ToTable("password_reset_tokens");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			/* Lowercase hex SHA-256 of the emailed token, matching auth_tokens.token_hash and
			 * web_sessions.session_hash. Stored as text rather than bytea purely for consistency
			 * with those two: every hashed secret in this schema is a 64-character hex string,
			 * and one table shaped differently is how a byte[]/string mix-up gets written. */
			builder.Property(e => e.TokenHash)
				.IsRequired()
				.HasMaxLength(64);

			// The lookup key, and unique so a hash collision in the issuer cannot silently
			// produce two rows one redemption would have to choose between.
			builder.HasIndex(e => e.TokenHash)
				.IsUnique();

			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.CreatedUtc)
				.IsRequired();

			builder.Property(e => e.ExpiresUtc)
				.IsRequired();

			builder.Property(e => e.UsedUtc);

			builder.Property(e => e.RequestedIp)
				.HasMaxLength(64);

			/* Covers both account-scoped queries: the resend cooldown (newest row for an account)
			 * and the invalidate-the-rest write that follows a redemption. */
			builder.HasIndex(e => new { e.AccountName, e.UsedUtc });

			// Drives the expiry sweep.
			builder.HasIndex(e => e.ExpiresUtc);

			/* No foreign key to accounts, deliberately — see the remarks on the entity. */
		}
	}
}
