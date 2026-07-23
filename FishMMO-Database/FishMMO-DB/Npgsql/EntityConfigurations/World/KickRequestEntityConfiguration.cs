using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for KickRequestEntity with explicit indexes and constraints.
	/// </summary>
	public class KickRequestEntityConfiguration : IEntityTypeConfiguration<KickRequestEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<KickRequestEntity> builder)
		{
			builder.ToTable("kick_requests");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(100);

			// Performance index for kick request lookups
			builder.HasIndex(e => e.AccountName)
				.IsUnique();

			// Index for creation time (sorting/filtering old requests)
			builder.HasIndex(e => e.TimeCreated);

			// Composite index for kick request pagination (FetchAsync hot path)
			// Covers WHERE time_created >= @lastFetch AND id > @lastPosition ORDER BY time_created, id
			builder.HasIndex(e => new { e.TimeCreated, e.ID });
		}
	}
}