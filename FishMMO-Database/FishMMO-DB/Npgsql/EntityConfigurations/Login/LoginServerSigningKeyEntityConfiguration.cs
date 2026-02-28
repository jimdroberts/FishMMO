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

			// LoginServerId — one signing key per LoginServer
			builder.Property(e => e.LoginServerId)
				.IsRequired();

			builder.HasIndex(e => e.LoginServerId)
				.IsUnique();

			// HMAC-SHA256 key (32 bytes)
			builder.Property(e => e.HmacKey)
				.IsRequired()
				.HasMaxLength(64);

			// Foreign key to login_servers
			builder.HasOne(e => e.LoginServer)
				.WithMany()
				.HasForeignKey(e => e.LoginServerId)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}