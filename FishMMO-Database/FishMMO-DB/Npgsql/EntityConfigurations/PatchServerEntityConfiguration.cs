using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PatchServerEntity with explicit indexes and constraints.
	/// </summary>
	public class PatchServerEntityConfiguration : IEntityTypeConfiguration<PatchServerEntity>
	{
		public void Configure(EntityTypeBuilder<PatchServerEntity> builder)
		{
			builder.ToTable("patch_servers");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.Address)
				.IsRequired()
				.HasMaxLength(255);

			builder.Property(e => e.Port)
				.IsRequired();

			builder.Property(e => e.LastPulse)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Unique constraint on server address/port
			builder.HasIndex(e => new { e.Address, e.Port })
				.IsUnique();

			// Performance index for active server queries
			builder.HasIndex(e => e.LastPulse);

			// Index for server creation time
			builder.HasIndex(e => e.TimeCreated);
		}
	}
}