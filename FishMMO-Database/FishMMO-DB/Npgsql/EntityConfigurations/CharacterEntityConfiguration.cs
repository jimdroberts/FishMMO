using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterEntityConfiguration : IEntityTypeConfiguration<CharacterEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterEntity> builder)
		{
			builder.Property(e => e.Name)
				.IsRequired();

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.NameLowercase)
				.HasComputedColumnSql("LOWER(\"name\")", stored: true);

			builder.HasIndex(e => e.NameLowercase)
				.IsUnique()
				.HasDatabaseName("IX_CharacterEntity_NameLowercase");

			// Performance index for account character queries (GetCharactersAsync hot path)
			builder.HasIndex(e => new { e.Account, e.Deleted })
				.HasDatabaseName("IX_Character_Account_Deleted");

			// Performance index for online status filtering
			builder.HasIndex(e => e.Online)
				.HasDatabaseName("IX_Character_Online")
				.HasFilter("online = true AND NOT deleted");
		}
	}
}