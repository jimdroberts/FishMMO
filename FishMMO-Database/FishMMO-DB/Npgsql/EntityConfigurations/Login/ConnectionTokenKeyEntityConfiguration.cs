using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for ConnectionTokenKeyEntity with explicit indexes and constraints.
	/// </summary>
	public class ConnectionTokenKeyEntityConfiguration : IEntityTypeConfiguration<ConnectionTokenKeyEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<ConnectionTokenKeyEntity> builder)
		{
			builder.ToTable("connection_token_keys");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// KeyId — unique logical identifier for region-scoped key lookup
			builder.Property(e => e.KeyId)
				.IsRequired()
				.HasMaxLength(64);

			builder.HasIndex(e => e.KeyId)
				.IsUnique();

			// HmacKeyBase64 — base64-encoded HMAC key material
			builder.Property(e => e.HmacKeyBase64)
				.IsRequired();

			// IsActive — soft lifecycle flag
			builder.Property(e => e.IsActive)
				.IsRequired()
				.HasDefaultValue(true);

			// DeactivatedAt — nullable, set when key is deactivated
			builder.Property(e => e.DeactivatedAt);

			// Composite index for "all active keys" query
			builder.HasIndex(e => e.IsActive);
		}
	}
}
