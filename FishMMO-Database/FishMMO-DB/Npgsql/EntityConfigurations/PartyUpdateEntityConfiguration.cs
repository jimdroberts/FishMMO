using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PartyUpdateEntity with explicit indexes and constraints.
	/// </summary>
	public class PartyUpdateEntityConfiguration : IEntityTypeConfiguration<PartyUpdateEntity>
	{
		public void Configure(EntityTypeBuilder<PartyUpdateEntity> builder)
		{
			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.PartyID)
				.IsRequired();

			builder.Property(e => e.LastUpdate)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Unique constraint on party ID (one update record per party)
			builder.HasIndex(e => e.PartyID)
				.IsUnique()
				.HasDatabaseName("IX_PartyUpdate_PartyID_Unique");

			// Performance index for party update fetch queries (FetchAsync hot path)
			// Covers WHERE last_update >= @lastFetch AND party_id IN (@partyIds)
			builder.HasIndex(e => e.LastUpdate)
				.HasDatabaseName("IX_PartyUpdate_LastUpdate");
		}
	}
}
