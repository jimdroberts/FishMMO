using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for GuildEntity with explicit indexes and constraints.
	/// </summary>
	public class GuildEntityConfiguration : IEntityTypeConfiguration<GuildEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<GuildEntity> builder)
		{
			builder.ToTable("guilds");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.Name)
				.IsRequired();

			builder.Property(e => e.NameLowercase)
				.HasComputedColumnSql("LOWER(name)", stored: true);

			builder.Property(e => e.Notice)
				.HasMaxLength(500);

			builder.Property(e => e.MessageOfTheDay)
				.HasMaxLength(500);

			builder.Property(e => e.Blurb)
				.HasMaxLength(500)
				.HasDefaultValue(string.Empty);

			builder.Property(e => e.Tags)
				.HasMaxLength(200)
				.HasDefaultValue(string.Empty);

			builder.Property(e => e.IsRecruiting)
				.IsRequired()
				.HasDefaultValue(false);

			/* The directory's only query is "recruiting guilds, newest listing first, optionally
			 * matched on text". Partial index: the overwhelming majority of rows are not
			 * recruiting at any given moment, and indexing them would be paying for the guilds the
			 * query never returns. */
			builder.HasIndex(e => e.IsRecruiting)
				.HasFilter("is_recruiting = TRUE");

			// Case-insensitive uniqueness via normalized computed column.
			builder.HasIndex(e => e.NameLowercase)
				.IsUnique();

		}
	}
}