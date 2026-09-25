using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity configuration for AccountEntity with explicit indexes and constraints.
	/// </summary>
	public class AccountEntityConfiguration : IEntityTypeConfiguration<AccountEntity>
	{
		/// <inheritdoc/>
		public void Configure(EntityTypeBuilder<AccountEntity> builder)
		{
			builder.ToTable("accounts");

			// Primary Key
			builder.HasKey(e => e.Name);

			// Required fields
			builder.Property(e => e.Name)
				.IsRequired()
				.HasMaxLength(50);

			// Case-insensitive lookup column. PostgreSQL stores LOWER(name) and a UNIQUE index
			// on this column prevents two accounts that differ only in case.
			builder.Property(e => e.NameLowercase)
				.HasColumnName("name_lowercase")
				.HasComputedColumnSql("LOWER(name)", stored: true);

			// xmin concurrency token — PostgreSQL system column updated on every row change.
			builder.Property(e => e.Version)
				.HasColumnName("xmin")
				.HasColumnType("xid")
				.ValueGeneratedOnAddOrUpdate()
				.IsConcurrencyToken();

			builder.Property(e => e.Salt)
				.IsRequired()
				.HasMaxLength(256);

			builder.Property(e => e.Verifier)
				.IsRequired()
				.HasMaxLength(512);

			builder.Property(e => e.AccessLevel)
				.IsRequired();

			builder.Property(e => e.Email)
				.HasMaxLength(320);

			builder.Property(e => e.Age)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.TotpEnabled)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.TotpSecret)
				.HasMaxLength(256);

			builder.Property(e => e.PendingTotpSecret)
				.HasMaxLength(256);

			builder.Property(e => e.TotpVerifiedAt);

			builder.Property(e => e.LastTotpWindow)
				.IsRequired()
				.HasDefaultValue(0L);

			builder.Property(e => e.Verified)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.VerifyCode)
				.IsRequired()
				.HasDefaultValue(0);

			// Verification code expiry. Null while no verification is pending; bounded so an
			// abandoned code cannot remain a guess-target indefinitely.
			builder.Property(e => e.VerifyCodeExpiresUtc);

			builder.Property(e => e.VerificationEmailSentAt);

			/* Moderation columns. Written only by operator actions and by the login fetch lifting an
			 * expired temporary ban — never by the account-creation or save paths, which is why none
			 * of those statements name them. The "by" columns carry no foreign key for the same
			 * reason admin_audit_log has none: deleting an operator must not erase who acted. */
			builder.Property(e => e.Muted)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.MutedBy)
				.HasMaxLength(50);

			builder.Property(e => e.MuteReason)
				.HasMaxLength(256);

			builder.Property(e => e.BannedBy)
				.HasMaxLength(50);

			builder.Property(e => e.BanReason)
				.HasMaxLength(256);

			/* Contact and identity. All optional; lengths generous enough for real addresses and names
			 * in any script, and bounded so a registration form cannot store a novel. */
			builder.Property(e => e.Phone)
				.HasMaxLength(16);

			builder.Property(e => e.PhoneVerified)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.PhoneVerifyCode)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.PhoneVerifyCodeExpiresUtc);

			// Existing rows are backfilled from `verified` by the migration, not by this default.
			builder.Property(e => e.EmailVerified)
				.IsRequired()
				.HasDefaultValue(false);

			// Email only: what every account meant before there was a choice.
			builder.Property(e => e.VerificationChannels)
				.IsRequired()
				.HasDefaultValue((byte)1);

			builder.Property(e => e.RealName)
				.HasMaxLength(128);

			builder.Property(e => e.Country)
				.HasMaxLength(64);

			builder.Property(e => e.Address)
				.HasMaxLength(512);

			builder.Property(e => e.ReferralAccount)
				.HasMaxLength(50);

			// Discord verification and the bot's link. See the entity.
			// A username, or a name with "#" and its four-digit discriminator: AccountProfileRules.MaxDiscordTagLength.
			builder.Property(e => e.DiscordUsername)
				.HasMaxLength(37);

			builder.Property(e => e.DiscordUserId);

			builder.Property(e => e.DiscordLinkedAt);

			builder.Property(e => e.DiscordVerified)
				.IsRequired()
				.HasDefaultValue(false);

			builder.Property(e => e.DiscordVerifyCode)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.DiscordDmClaimedAt);

			builder.Property(e => e.DiscordDmSentAt);

			builder.Property(e => e.DiscordDmUserId);

			builder.Property(e => e.DiscordDmAttempts)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.DiscordDmLastError)
				.HasMaxLength(256);

			builder.Property(e => e.VerifyFailedCount)
				.IsRequired()
				.HasDefaultValue(0);

			// Sign-in lockout counters. See the entity.
			builder.Property(e => e.FailedLoginCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.FailedLoginSinceUtc);

			builder.Property(e => e.LoginLockedUntilUtc);

			builder.Property(e => e.FailedTwoFactorCount)
				.IsRequired()
				.HasDefaultValue(0);

			builder.Property(e => e.FailedTwoFactorSinceUtc);

			builder.Property(e => e.TwoFactorLockedUntilUtc);

			builder.Property(e => e.LastLogin)
				.IsRequired()
				.HasDefaultValueSql("timezone('UTC', CURRENT_TIMESTAMP)");

			// Indexes
			builder.HasIndex(e => e.AccessLevel);

			builder.HasIndex(e => e.TimeCreated);

			// Case-insensitive uniqueness for account names via the computed name_lowercase column.
			builder.HasIndex(e => e.NameLowercase)
				.IsUnique();

			// Unique index on email when provided
			builder.HasIndex(e => e.Email)
				.IsUnique()
				.HasFilter("email IS NOT NULL");

			// One Discord account links one game account.
			builder.HasIndex(e => e.DiscordUserId)
				.IsUnique()
				.HasFilter("discord_user_id IS NOT NULL");

			// What the Discord bot reads: DMs owed and not yet sent, and the one owed to a member who just joined.
			builder.HasIndex(e => e.DiscordUsername)
				.HasFilter("discord_verify_code <> 0 AND discord_dm_sent_at IS NULL");
		}
	}
}