using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="WebSessionEntity"/>.
	/// </summary>
	public class WebSessionEntityConfiguration : IEntityTypeConfiguration<WebSessionEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<WebSessionEntity> builder)
		{
			builder.ToTable("web_sessions");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// xmin concurrency token, matching every other versioned entity here.
			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			// SHA-256 hex of the cookie value. Unique, because it is the lookup key.
			builder.Property(e => e.SessionHash)
				.IsRequired()
				.HasMaxLength(64);

			builder.HasIndex(e => e.SessionHash)
				.IsUnique();

			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(100);

			builder.HasIndex(e => e.AccountName);

			builder.Property(e => e.AccessLevelAtIssue)
				.IsRequired();

			builder.Property(e => e.TwoFactorSatisfied)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.LastStepUpUtc);

			builder.Property(e => e.CreatedUtc)
				.IsRequired();

			builder.Property(e => e.LastSeenUtc)
				.IsRequired();

			builder.Property(e => e.ExpiresUtc)
				.IsRequired();

			// Drives the expiry sweep.
			builder.HasIndex(e => e.ExpiresUtc);

			builder.Property(e => e.Revoked)
				.IsRequired()
				.HasDefaultValue(false);

			// Revoking every session for an account is a single indexed delete.
			builder.HasIndex(e => new { e.AccountName, e.Revoked });

			builder.Property(e => e.IpAddress)
				.HasMaxLength(64);

			builder.Property(e => e.UserAgent)
				.HasMaxLength(256);

			// Deleting an account takes its panel sessions with it, exactly as it does its
			// game tokens.
			builder.HasOne<AccountEntity>()
				.WithMany()
				.HasForeignKey(e => e.AccountName)
				.HasPrincipalKey(a => a.Name)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
