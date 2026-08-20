using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for <see cref="CharacterEntity"/>.
	/// </summary>
	public class CharacterEntityConfiguration : IEntityTypeConfiguration<CharacterEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<CharacterEntity> builder)
		{
			builder.ToTable("characters");

			// Primary Key
			builder.HasKey(e => e.ID);

			builder.Property(e => e.ID)
				.ValueGeneratedOnAdd();

			builder.Property(e => e.Name)
				.IsRequired();

			builder.Property(e => e.SessionState)
				.IsRequired()
				.HasConversion<short>()
				.HasColumnType("smallint")
				.HasDefaultValue(CharacterSessionState.Offline);

			builder.Property(e => e.SessionOwnerServerId)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.SessionOwnerToken)
				.IsRequired()
				.HasDefaultValue(Guid.Empty);

			builder.Property(e => e.SessionLeaseExpiresUtc)
				.IsRequired()
				.HasDefaultValue(DateTime.UnixEpoch);

			// Epoch rather than DateTime.MinValue so an existing character is immediately
			// eligible for its first channel switch, and so the column matches the
			// "timestamp without time zone" mapping the other UTC columns already use.
			builder.Property(e => e.LastChannelSwitchUtc)
				.IsRequired()
				.HasDefaultValue(DateTime.UnixEpoch);

			builder.Property(e => e.NameLowercase)
				.HasComputedColumnSql("LOWER(name)", stored: true);

			// Foreign key relationship to Account
			// Account property is the account name (string) which is the PK of AccountEntity
			// Restrict delete behavior prevents orphaned characters
			builder.HasOne<AccountEntity>()
				.WithMany()
				.HasForeignKey(e => e.Account)
				.HasPrincipalKey(a => a.Name)
				.OnDelete(DeleteBehavior.Restrict)
				.HasConstraintName("FK_Character_Account");

			builder.HasIndex(e => e.NameLowercase)
				.IsUnique();

			// Performance index for account character queries (FetchManyAsync hot path)
			builder.HasIndex(e => e.Account);

			// Performance index for session state filtering (online queries).
			builder.HasIndex(e => e.SessionState)
				.HasFilter("session_state <> 0");
		}
	}
}
