using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for LoginServerEntity with explicit indexes and constraints.
	/// </summary>
	public class LoginServerEntityConfiguration : IEntityTypeConfiguration<LoginServerEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<LoginServerEntity> builder)
		{
			builder.ToTable("login_servers");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(100);

			builder.Property(e => e.LastPulse)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			builder.Property(e => e.Address)
				.IsRequired()
				.HasMaxLength(255);

			builder.Property(e => e.Port)
				.IsRequired();

			// Index on server name
			builder.HasIndex(e => e.Name)
				.IsUnique();

			// Performance index for active server queries
			builder.HasIndex(e => e.LastPulse);

			// Index for server creation time
			builder.HasIndex(e => e.TimeCreated);
		}
	}
}