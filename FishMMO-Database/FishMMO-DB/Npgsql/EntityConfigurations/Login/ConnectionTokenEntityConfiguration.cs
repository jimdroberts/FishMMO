using FishMMO.Database.Npgsql.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	public class ConnectionTokenEntityConfiguration : IEntityTypeConfiguration<ConnectionTokenEntity>
	{
		public void Configure(EntityTypeBuilder<ConnectionTokenEntity> builder)
		{
			builder.ToTable("connection_tokens");

			builder.HasKey(e => e.ID);

			builder.Property(e => e.TokenHash)
				.IsRequired()
				.HasMaxLength(64);

			builder.Property(e => e.RealIp)
				.IsRequired()
				.HasMaxLength(45);  // max IPv6 length

			builder.Property(e => e.ExpiresAt)
				.IsRequired();

			// Unique index for fast token lookups
			builder.HasIndex(e => e.TokenHash)
				.IsUnique();

			// Index for periodic cleanup of expired tokens
			builder.HasIndex(e => e.ExpiresAt);
		}
	}
}