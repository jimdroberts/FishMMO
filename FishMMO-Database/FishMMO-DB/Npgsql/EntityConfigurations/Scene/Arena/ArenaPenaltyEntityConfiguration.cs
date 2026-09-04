using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Entity configuration for <see cref="ArenaPenaltyEntity"/>.</summary>
	public class ArenaPenaltyEntityConfiguration : IEntityTypeConfiguration<ArenaPenaltyEntity>
	{
		public void Configure(EntityTypeBuilder<ArenaPenaltyEntity> builder)
		{
			builder.ToTable("arena_penalty");
			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();
			builder.Property(e => e.CharacterID).IsRequired();
			builder.Property(e => e.LockedUntilUtc).IsRequired();
			builder.Property(e => e.Reason).IsRequired().HasMaxLength(128);
			builder.HasIndex(e => e.CharacterID).IsUnique();
			builder.HasOne<CharacterEntity>().WithMany().HasForeignKey(e => e.CharacterID).OnDelete(DeleteBehavior.Cascade);
		}
	}
}
