using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for LoginServerSigningKeyEntity with explicit indexes and constraints.
	/// </summary>
	public class LoginServerSigningKeyEntityConfiguration : IEntityTypeConfiguration<LoginServerSigningKeyEntity>
	{
		/// <inheritdoc/>
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

			// xmin concurrency token — PostgreSQL system column updated on every row change.
			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			builder.HasIndex(e => e.LoginServerId);

			builder.HasIndex(e => new { e.LoginServerId, e.TimeCreated, e.ID });

			// Stored signing key. Either a raw 32-byte HMAC-SHA256 key (legacy) or an
			// AEAD-wrapped sealed blob (see FishMMO.Auth.Implementation.KeyEnvelope) when
			// the deployment KEK is configured. Length is generous to accommodate the
			// envelope header + nonce + ciphertext + tag without future migrations.
			builder.Property(e => e.HmacKey)
				.IsRequired()
				.HasMaxLength(256);

			// Rotation lifecycle metadata.
			builder.Property(e => e.IsActive)
				.IsRequired()
				.HasDefaultValue(true);

			builder.Property(e => e.ActivatedAtUtc)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.RotatedAtUtc);

			// Partial UNIQUE index supports the "current active key per login server" hot path.
			// Uniqueness on (login_server_id) WHERE is_active=true forces concurrent rotations
			// to serialise at the DB level — a second active-row insert from a racing rotation
			// is rejected by PostgreSQL, which the rotation transaction then handles cleanly.
			builder.HasIndex(e => new { e.LoginServerId, e.IsActive })
				.IsUnique()
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