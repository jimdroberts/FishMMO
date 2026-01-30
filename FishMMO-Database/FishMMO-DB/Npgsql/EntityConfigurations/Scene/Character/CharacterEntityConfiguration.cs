using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterEntity"/>.
	/// </summary>
	public class CharacterEntityConfiguration : IEntityTypeConfiguration<CharacterEntity>
	{
		public void Configure(EntityTypeBuilder<CharacterEntity> builder)
		{
			builder.ToTable("characters");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.Name)
				.IsRequired();

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			builder.Property(e => e.Version)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.NameLowercase)
				.HasComputedColumnSql("LOWER(\"name\")", stored: true);

			// Foreign key relationship to Account
			// Account property is the account name (string) which is the PK of AccountEntity
			// Restrict delete behavior prevents orphaned characters
			builder.HasOne<AccountEntity>()
				.WithMany()
				.HasForeignKey(e => e.Account)
				.OnDelete(DeleteBehavior.Restrict)
				.HasConstraintName("FK_Character_Account");

			builder.HasIndex(e => e.NameLowercase)
				.IsUnique();

			// Performance index for account character queries (GetCharactersAsync hot path)
			builder.HasIndex(e => e.Account);

			// Performance index for online status filtering
			builder.HasIndex(e => e.Online)
				.HasFilter("online = true");
		}
	}
}