using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="AccountMapFogEntity"/>.
	/// </summary>
	public class AccountMapFogEntityConfiguration : IEntityTypeConfiguration<AccountMapFogEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<AccountMapFogEntity> builder)
		{
			builder.ToTable("account_map_fog");

			// (account, scene) IS the identity: the merge arbitrates on it, and the scene list is a prefix scan of it.
			builder.HasKey(e => new { e.AccountName, e.SceneName });

			builder.Property(e => e.AccountName)
				.IsRequired()
				.HasMaxLength(50);

			builder.Property(e => e.SceneName)
				.IsRequired()
				.HasMaxLength(64);

			builder.Property(e => e.Data)
				.IsRequired();

			builder.Property(e => e.ExploredCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.TimeUpdated)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			// An account's maps go with the account.
			builder.HasOne(e => e.Account)
				.WithMany()
				.HasForeignKey(e => e.AccountName)
				.OnDelete(DeleteBehavior.Cascade);
		}
	}
}
