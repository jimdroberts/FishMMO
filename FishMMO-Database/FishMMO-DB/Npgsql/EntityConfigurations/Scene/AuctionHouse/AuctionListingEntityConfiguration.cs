using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="AuctionListingEntity"/>.
	/// </summary>
	public class AuctionListingEntityConfiguration : IEntityTypeConfiguration<AuctionListingEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<AuctionListingEntity> builder)
		{
			builder.ToTable("auction_listings");

			builder.HasKey(e => e.ID);
			builder.Property(e => e.ID).ValueGeneratedOnAdd();

			builder.Property(e => e.Version).IsRequired().HasDefaultValue(1L);
			builder.Property(e => e.SellerID).IsRequired();
			builder.Property(e => e.SellerName).IsRequired().HasMaxLength(64);
			builder.Property(e => e.ItemTemplateID).IsRequired();
			builder.Property(e => e.ItemSeed).IsRequired().HasDefaultValue(0);
			builder.Property(e => e.ItemName).IsRequired().HasMaxLength(128);
			builder.Property(e => e.Category).IsRequired().HasDefaultValue((short)0);
			builder.Property(e => e.Quantity).IsRequired();
			builder.Property(e => e.UnitPrice).IsRequired();
			builder.Property(e => e.Deposit).IsRequired().HasDefaultValue(0L);
			builder.Property(e => e.CreatedUtc).IsRequired().HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");
			builder.Property(e => e.ExpiresUtc).IsRequired();

			// The browse list: one category, cheapest first.
			builder.HasIndex(e => new { e.Category, e.UnitPrice });
			// The price hint and "more of the same": one template, cheapest first.
			builder.HasIndex(e => new { e.ItemTemplateID, e.UnitPrice });
			// "My listings" and the per-seller cap.
			builder.HasIndex(e => e.SellerID);
			// The expiry sweep.
			builder.HasIndex(e => e.ExpiresUtc);

			builder.HasIndex(e => e.RequestKey)
				.IsUnique()
				.HasFilter("request_key IS NOT NULL");

			builder.HasCheckConstraint("ck_auction_listings_quantity_positive", "quantity > 0");
			builder.HasCheckConstraint("ck_auction_listings_unit_price_positive", "unit_price > 0");

			/* Characters are soft-deleted, so a seller's row stays and their listings keep selling (the proceeds wait in
			 * a mailbox nobody reads). A hard delete takes the listings with it. */
			builder.HasOne<CharacterEntity>()
				.WithMany()
				.HasForeignKey(e => e.SellerID)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
