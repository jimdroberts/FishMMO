using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for account operations following ISP principle.
	/// All operations are async and return DatabaseResult for consistent error handling.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// </summary>
	/// <remarks>
	/// This service provides account management operations including:
	/// - Account creation with atomic UPSERT to prevent race conditions
	/// - Login authentication with SRP (Secure Remote Password) protocol support
	/// - Last login timestamp tracking
	/// - Account existence checks
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// All methods follow consistent validation and error handling patterns:
	/// - Username validation (3-32 characters, non-empty)
	/// - Null safety with nullable reference types
	/// - DatabaseResult pattern for safe, typed error handling
	/// - Custom exceptions with sanitized messages for security
	/// </remarks>
	public interface IAccountService : IExistsByKeyAction<string>
	{
		/// <summary>
		/// Gets the last login time for an account.
		/// </summary>
		/// <param name="username">The account name or email to query, depending on <paramref name="email"/>.</param>
		/// <param name="email">When false, <paramref name="username"/> is treated as the account name (3-32 chars). When true, it is treated as an email (max 320 chars).</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the last login timestamp on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Success: Returns the last login timestamp.
		/// Failure cases:
		/// - VALIDATION_ERROR: Username or email validation failed
		/// - DB_NOT_FOUND: Account does not exist
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// </remarks>
		Task<DatabaseResult<DateTime>> FetchLastLoginAsync(
			string username,
			bool email = false,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates a new account with the specified credentials.
		/// </summary>
		/// <param name="accountName">The account name. Must be 3-32 characters.</param>
		/// <param name="salt">The salt for SRP password hashing. Must not be null or whitespace.</param>
		/// <param name="verifier">The verifier for SRP password hashing. Must not be null or whitespace.</param>
		/// <param name="email">The account email. Must not be empty and must not exceed 320 characters.</param>
		/// <param name="age">The account holder age. Must be between 0 and 200.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Success: Account created with Player access level and current timestamp.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid username, salt, verifier, email, or age
		/// - UNIQUE_VIOLATION: Account name already exists (non-transient)
		/// - DATABASE_ERROR: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> PersistAsync(
			string accountName,
			string salt,
			string verifier,
			string email,
			int age,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves account authentication data for login.
		/// </summary>
		/// <param name="username">The account name or email to query, depending on <paramref name="email"/>.</param>
		/// <param name="email">When false, <paramref name="username"/> is treated as the account name (3-32 chars). When true, it is treated as an email (max 320 chars).</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing AccountData with authentication credentials on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Success: Returns AccountData with salt, verifier, access level, and timestamps.
		/// - (Banned, null): Account exists but is banned
		/// Success: Returns AccountData with salt, verifier, access level, and timestamps.
		/// Failure cases:
		/// - VALIDATION_ERROR: Username or email validation failed
		/// - DB_NOT_FOUND: Account does not exist
		/// - ACCOUNT_BANNED: Account exists but is banned
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// 
		/// Security Note: Does not distinguish between non-existent accounts and banned accounts
		/// in error messages to prevent username enumeration attacks.
		/// 
		/// The returned AccountData DTO is a defensive copy and can be safely used
		/// after the database context is disposed.
		/// </remarks>
		Task<DatabaseResult<AccountData>> FetchForLoginAsync(
			string username,
			bool email = false,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the last login timestamp for an account atomically.
		/// Uses execution strategy for automatic retry on transient failures.
		/// </summary>
		/// <param name="accountName">The account name. Must be 3-32 characters.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Uses atomic UPDATE without loading entity to prevent race conditions.
		/// Wrapped in execution strategy for automatic retry on transient failures.
		/// 
		/// Success: Last login timestamp updated to current database server time.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid username (length or format)
		/// - DB_NOT_FOUND: Account does not exist
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> PersistLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the email address for an account.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="email">The new email address, or null to clear.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistEmailAsync(
			string accountName,
			string? email,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the age for an account.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="age">The age value.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistAgeAsync(
			string accountName,
			int age,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Stores the encrypted TOTP secret and enables TOTP for an account.
		/// Called during 2FA enrollment after the server generates the secret.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="encryptedTotpSecret">The Base32-encoded TOTP secret, encrypted at rest by the server.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistTotpSecretAsync(
			string accountName,
			string encryptedTotpSecret,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Enables or disables TOTP two-factor authentication for an account.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="enabled">Whether TOTP should be enabled.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistTotpEnabledAsync(
			string accountName,
			bool enabled,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Records the first successful TOTP verification timestamp, confirming 2FA setup.
		/// Atomically sets totp_verified_at and updates last_totp_window.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="totpWindow">The time-step window of the verified code.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistTotpVerifiedAtAsync(
			string accountName,
			long totpWindow,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the last TOTP time-step window used for an account, preventing replay attacks.
		/// Only updates if the new window is greater than the stored value.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="totpWindow">The time-step window of the verified code.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistLastTotpWindowAsync(
			string accountName,
			long totpWindow,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Clears all TOTP fields when a user disables 2FA.
		/// Atomically resets totp_secret, totp_enabled, totp_verified_at, and last_totp_window.
		/// Recovery codes should be invalidated separately via ITwoFactorRecoveryCodeService.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> ClearTotpAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the temporary Discord link verification code for an account.
		/// The Discord bot generates this code; the user verifies in-game with /verify.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="linkCode">The link code, or null to clear after verification.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistDiscordLinkCodeAsync(
			string accountName,
			string? linkCode,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches an account by its Discord link code for verification.
		/// Used by the Discord bot to confirm in-game verification.
		/// </summary>
		/// <param name="linkCode">The Discord link code to search for.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult containing the AccountData if found, or null.</returns>
		Task<DatabaseResult<AccountData?>> FetchByDiscordLinkCodeAsync(
			string linkCode,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the verified status for an account.
		/// Called when the user provides the correct verify code from the email verification link.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="verifyCode">The verification code the user provided. Must match the stored verify code.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistVerifiedAsync(
			string accountName,
			int verifyCode,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the verification code for an account, along with the UTC expiry timestamp
		/// after which the code is no longer redeemable.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="verifyCode">The randomly generated verification code.</param>
		/// <param name="expiresUtc">UTC instant after which the code is invalid.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistVerifyCodeAsync(
			string accountName,
			int verifyCode,
			DateTime expiresUtc,
			CancellationToken cancellationToken = default);
	}
}