using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel
{
	/// <summary>
	/// Resolves a scoped <see cref="IAccountService"/> per call from the root provider.
	/// </summary>
	/// <remarks>
	/// <see cref="Services.SrpLoginService"/> has to be a singleton, because one SRP exchange
	/// spans two HTTP requests and its in-flight state must outlive the first. A singleton cannot
	/// capture a scoped database service — doing so pins one context open for the life of the
	/// process and defeats the connection pool — so this adapter opens a scope per call instead.
	/// </remarks>
	internal sealed class ScopedAccountService : IAccountService
	{
		private readonly IServiceProvider provider;

		public ScopedAccountService(IServiceProvider provider) => this.provider = provider;

		private async Task<T> InScopeAsync<T>(Func<IAccountService, Task<T>> action)
		{
			using var scope = provider.CreateScope();
			return await action(scope.ServiceProvider.GetRequiredService<IAccountService>());
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<AccountData>> FetchForLoginAsync(string username, bool email = false, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.FetchForLoginAsync(username, email, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.ExistsAsync(key, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<DateTime>> FetchLastLoginAsync(string username, bool email = false, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.FetchLastLoginAsync(username, email, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistAsync(string accountName, string salt, string verifier, string email, int age, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistAsync(accountName, salt, verifier, email, age, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistLastLoginAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistLastLoginAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistSrpCredentialsAsync(string accountName, string salt, string verifier, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistSrpCredentialsAsync(accountName, salt, verifier, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistEmailAsync(string accountName, string email, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistEmailAsync(accountName, email, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistAgeAsync(string accountName, int age, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistAgeAsync(accountName, age, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistTotpSecretAsync(string accountName, string encryptedTotpSecret, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistTotpSecretAsync(accountName, encryptedTotpSecret, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistTotpEnabledAsync(string accountName, bool enabled, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistTotpEnabledAsync(accountName, enabled, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistTotpVerifiedAtAsync(string accountName, long totpWindow, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistTotpVerifiedAtAsync(accountName, totpWindow, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistLastTotpWindowAsync(string accountName, long totpWindow, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistLastTotpWindowAsync(accountName, totpWindow, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> ClearTotpAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.ClearTotpAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistDiscordLinkCodeAsync(string accountName, string linkCode, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistDiscordLinkCodeAsync(accountName, linkCode, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<AccountData?>> FetchByDiscordLinkCodeAsync(string linkCode, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.FetchByDiscordLinkCodeAsync(linkCode, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistVerifiedAsync(string accountName, int verifyCode, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistVerifiedAsync(accountName, verifyCode, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<AccountAdminPage>> SearchAdminAsync(AccountAdminQuery query, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.SearchAdminAsync(query, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<AccountAdminData>> FetchAdminAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.FetchAdminAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> BanAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.BanAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> UnbanAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.UnbanAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> BanAsync(string accountName, DateTime? bannedUntilUtc, string? bannedBy, string? reason, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.BanAsync(accountName, bannedUntilUtc, bannedBy, reason, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistMuteAsync(string accountName, DateTime? mutedUntilUtc, string? mutedBy, string? reason, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistMuteAsync(accountName, mutedUntilUtc, mutedBy, reason, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> ClearMuteAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.ClearMuteAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistAccessLevelAsync(string accountName, byte accessLevel, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistAccessLevelAsync(accountName, accessLevel, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistAutoVerifiedAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistAutoVerifiedAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistVerifyCodeAsync(string accountName, int verifyCode, DateTime expiresUtc, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistVerifyCodeAsync(accountName, verifyCode, expiresUtc, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistVerificationEmailSentAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistVerificationEmailSentAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistProfileAsync(string accountName, AccountProfileData profile, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistProfileAsync(accountName, profile, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistChannelsVerifiedAsync(string accountName, FishMMO.Database.Data.Enums.AccountVerificationChannels channels, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistChannelsVerifiedAsync(accountName, channels, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistPhoneVerifyCodeAsync(string accountName, int verifyCode, DateTime expiresUtc, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistPhoneVerifyCodeAsync(accountName, verifyCode, expiresUtc, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> PersistPhoneVerifiedAsync(string accountName, int verifyCode, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.PersistPhoneVerifiedAsync(accountName, verifyCode, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<DateTime?>> RecordAuthFailureAsync(string accountName, AuthFailureKind kind, int threshold, TimeSpan window, TimeSpan lockout, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.RecordAuthFailureAsync(accountName, kind, threshold, window, lockout, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult> ClearAuthFailuresAsync(string accountName, AuthFailureKind kind, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.ClearAuthFailuresAsync(accountName, kind, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<bool>> ClearAuthLockoutAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.ClearAuthLockoutAsync(accountName, cancellationToken));

		/// <inheritdoc/>
		public Task<DatabaseResult<AuthLockoutState>> FetchAuthLockoutAsync(string accountName, CancellationToken cancellationToken = default)
			=> InScopeAsync(s => s.FetchAuthLockoutAsync(accountName, cancellationToken));
	}
}
