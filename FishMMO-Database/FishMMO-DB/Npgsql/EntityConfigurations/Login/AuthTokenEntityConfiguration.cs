using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for AuthTokenEntity with explicit indexes and constraints.
	/// </summary>
	public class AuthTokenEntityConfiguration : IEntityTypeConfiguration<AuthTokenEntity>
	{
		public void Configure(EntityTypeBuilder<AuthTokenEntity> builder)
		{
			builder.ToTable("auth_tokens");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// TokenHash — SHA-256 hex of the signed token blob, unique for lookups
			builder.Property(e => e.TokenHash)
				.IsRequired()
				.HasMaxLength(64);

			builder.HasIndex(e => e.TokenHash)
				.IsUnique();

			// AccountName — FK to accounts (string PK)
			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(100);

			builder.HasIndex(e => e.AccountName);

			// LoginServerId — FK to login_servers
			builder.Property(e => e.LoginServerId)
				.IsRequired();

			// ExpiresUtc — used for cleanup queries
			builder.Property(e => e.ExpiresUtc)
				.IsRequired();

			builder.HasIndex(e => e.ExpiresUtc);

			// Revoked — soft revocation flag
			builder.Property(e => e.Revoked)
				.IsRequired()
				.HasDefaultValue(false);

			// Composite index for account + revoked queries (e.g., revoke all for account)
			builder.HasIndex(e => new { e.AccountName, e.Revoked });

			// Foreign key to accounts
			builder.HasOne(e => e.Account)
				.WithMany()
				.HasForeignKey(e => e.AccountName)
				.HasPrincipalKey(a => a.Name)
				.OnDelete(DeleteBehavior.Cascade);

			// Foreign key to login_servers
			builder.HasOne(e => e.LoginServer)
				.WithMany()
				.HasForeignKey(e => e.LoginServerId)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}