using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="ArenaMatchMemberEntity"/>.
	/// </summary>
	public class ArenaMatchMemberEntityConfiguration : IEntityTypeConfiguration<ArenaMatchMemberEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<ArenaMatchMemberEntity> builder)
		{
			builder.ToTable("arena_match_member");

			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.MatchID).IsRequired();
			builder.Property(e => e.CharacterID).IsRequired();
			builder.Property(e => e.Team).IsRequired();
			builder.Property(e => e.Kills).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.Deaths).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.Score).IsRequired().HasDefaultValue(0);

			// One seat per character per match.
			builder.HasIndex(e => new { e.MatchID, e.CharacterID }).IsUnique();

			// The "is this character in a live match" guard joins from here to the match.
			builder.HasIndex(e => e.CharacterID);

			builder.HasOne(e => e.Match)
				.WithMany(m => m.Members)
				.HasForeignKey(e => e.MatchID)
				.OnDelete(DeleteBehavior.Cascade);

			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
