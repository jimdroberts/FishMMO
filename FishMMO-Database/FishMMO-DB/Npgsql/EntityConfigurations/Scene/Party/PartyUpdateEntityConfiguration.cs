using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PartyUpdateEntity with explicit indexes and constraints.
	/// </summary>
	public class PartyUpdateEntityConfiguration : IEntityTypeConfiguration<PartyUpdateEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PartyUpdateEntity> builder)
		{
			builder.ToTable("party_updates");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			// Required fields
			builder.Property(e => e.PartyID)
				.IsRequired();

			builder.Property(e => e.LastUpdate)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			// Unique constraint on party ID (one update record per party)
			builder.HasIndex(e => e.PartyID)
				.IsUnique();

			// Composite index for party update fetch queries (FetchAsync hot path)
			// Optimizes WHERE last_update >= @lastFetch AND party_id IN (@partyIds)
			// Using (last_update, party_id) allows efficient range scan on last_update
			// followed by party_id filtering without additional lookups
			/* party_id leads, for the same reason as guild_updates: the ANY() predicate is the
			 * selective one and a leading open-ended range on last_update cannot seek. */
			builder.HasIndex(e => new { e.PartyID, e.LastUpdate });

			/* Foreign key to the party, cascading — previously absent entirely, so an update
			 * row could outlive its party and PartyService.DeleteAsync's documented reliance on
			 * CASCADE had nothing to cascade through. */
			builder.HasOne<PartyEntity>()
				.WithMany()
				.HasForeignKey(e => e.PartyID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}