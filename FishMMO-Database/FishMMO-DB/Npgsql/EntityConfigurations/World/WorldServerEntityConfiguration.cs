using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for WorldServerEntity with explicit indexes and constraints.
	/// </summary>
	public class WorldServerEntityConfiguration : IEntityTypeConfiguration<WorldServerEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<WorldServerEntity> builder)
		{
			builder.ToTable("world_servers");

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

			builder.Property(e => e.CharacterCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.Locked)
				.IsRequired()
				.HasDefaultValue(false);

			// Unique constraint on server name
			builder.HasIndex(e => e.Name)
				.IsUnique();

			// Performance index for active server queries (hot path)
			builder.HasIndex(e => e.LastPulse);

			// Index for server selection queries
			builder.HasIndex(e => new { e.Locked, e.CharacterCount });
		}
	}
}