using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for LoginServerSigningKeyEntity with explicit indexes and constraints.
	/// </summary>
	public class LoginServerSigningKeyEntityConfiguration : IEntityTypeConfiguration<LoginServerSigningKeyEntity>
	{
		public void Configure(EntityTypeBuilder<LoginServerSigningKeyEntity> builder)
		{
			builder.ToTable("login_server_signing_keys");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// LoginServerId — multiple historical keys may exist during token overlap windows.
			builder.Property(e => e.LoginServerId)
				.IsRequired();

			builder.HasIndex(e => e.LoginServerId);

			builder.HasIndex(e => new { e.LoginServerId, e.TimeCreated, e.ID });

			// HMAC-SHA256 key (32 bytes)
			builder.Property(e => e.HmacKey)
				.IsRequired()
				.HasMaxLength(64);

			// Rotation lifecycle metadata.
			builder.Property(e => e.IsActive)
				.IsRequired()
				.HasDefaultValue(true);

			builder.Property(e => e.ActivatedAtUtc)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.RotatedAtUtc);

			// Partial index supports the "current active key per login server" hot path.
			builder.HasIndex(e => new { e.LoginServerId, e.IsActive })
				.HasFilter("is_active = true");

			// Used by rotation pruning to find old inactive keys safe to delete.
			builder.HasIndex(e => new { e.LoginServerId, e.RotatedAtUtc });

			// Foreign key to login_servers
			builder.HasOne(e => e.LoginServer)
				.WithMany()
				.HasForeignKey(e => e.LoginServerId)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}