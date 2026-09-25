using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for PartyEntity with explicit indexes and constraints.
	/// </summary>
	public class PartyEntityConfiguration : IEntityTypeConfiguration<PartyEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<PartyEntity> builder)
		{
			builder.ToTable("parties");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.WorldServerID)
				.IsRequired()
				.HasDefaultValue(0L);

			// Parties are looked up by world server when a shard's parties are reconciled.
			builder.HasIndex(e => e.WorldServerID);

			/* One row per request. A write retried after its reply was lost (the connection dropped
			 * after the commit) carries the same key and lands on the row its first attempt made,
			 * instead of writing a second (issue #267). Filtered, so rows written without a key
			 * never collide. */
			builder.HasIndex(e => e.RequestKey)
				.IsUnique()
				.HasFilter("request_key IS NOT NULL");
		}
	}
}