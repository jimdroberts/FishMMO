using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PartyEntity with explicit indexes and constraints.
	/// </summary>
	public class PartyEntityConfiguration : IEntityTypeConfiguration<PartyEntity>
	{
		public void Configure(EntityTypeBuilder<PartyEntity> builder)
		{
			builder.ToTable("parties");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			// Members are tracked via CharacterPartyEntity join table
		}
	}
}