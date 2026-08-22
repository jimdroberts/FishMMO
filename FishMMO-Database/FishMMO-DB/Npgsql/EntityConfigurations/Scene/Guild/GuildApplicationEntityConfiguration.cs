using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for GuildApplicationEntity with explicit indexes and constraints.
	/// </summary>
	public class GuildApplicationEntityConfiguration : IEntityTypeConfiguration<GuildApplicationEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<GuildApplicationEntity> builder)
		{
			builder.ToTable("guild_application");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.GuildID)
				.IsRequired();

			builder.Property(e => e.CharacterID)
				.IsRequired();

			builder.Property(e => e.Message)
				.HasMaxLength(300);

			builder.Property(e => e.TimeCreated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			/* One pending application per player per guild, enforced by the storage layer rather
			 * than by a check-then-insert in application code — two scene servers can run that
			 * check concurrently and both pass. */
			builder.HasIndex(e => new { e.GuildID, e.CharacterID })
				.IsUnique();

			/* The applicant's own list of outstanding applications, and the per-player rate limit,
			 * both read by character. */
			builder.HasIndex(e => e.CharacterID);

			builder.HasOne(e => e.Guild)
				.WithMany(g => g.Applications)
				.HasForeignKey(e => e.GuildID)
				.OnDelete(DeleteBehavior.Cascade);

			/* NoAction, matching the membership relationship: character deletion is a soft delete
			 * in this schema, and a hard CASCADE here would be the one place a character row's
			 * removal silently reached into guild data. Stale applications are swept by the
			 * accept path, which re-verifies the applicant before admitting them. */
			builder.HasOne(e => e.Character)
				.WithMany()
				.HasForeignKey(e => e.CharacterID)
				.OnDelete(DeleteBehavior.NoAction);
		}
	}
}
