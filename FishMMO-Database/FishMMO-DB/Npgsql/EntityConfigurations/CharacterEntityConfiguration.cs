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

			builder.Property(e => e.NameLowercase)
				.HasComputedColumnSql("LOWER(\"name\")", stored: true);

			builder.HasIndex(e => e.NameLowercase)
				.IsUnique()
				.HasDatabaseName("IX_CharacterEntity_NameLowercase");

			/*builder.HasIndex($"LOWER(\"{nameof(CharacterEntity.name)}\")")
                .IsUnique()
                .HasName("idx_character_name_case_insensitive");
            
            builder.HasAnnotation("Relational:SqlCreateIndexStatement", 
                "CREATE UNIQUE INDEX idx_character_name_case_insensitive ON \"MyEntities\" (LOWER(\"name\")) COLLATE \"en_US.utf8\"");*/
		}
	}
}