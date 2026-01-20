using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

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
	public interface IAccountService
	{
		/// <summary>
		/// Gets the last login time for an account.
		/// </summary>
		/// <param name="accountName">The account name to query. Must be 3-32 characters.</param>
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
		/// - VALIDATION_ERROR: Username validation failed (invalid length or format)
		/// - DB_NOT_FOUND: Account does not exist
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// </remarks>
		Task<DatabaseResult<DateTime>> GetLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates a new account with the specified credentials.
		/// Uses atomic UPSERT to prevent race conditions.
		/// </summary>
		/// <param name="accountName">The account name. Must be 3-32 characters.</param>
		/// <param name="salt">The salt for SRP password hashing. Must not be null or whitespace.</param>
		/// <param name="verifier">The verifier for SRP password hashing. Must not be null or whitespace.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// This method uses PostgreSQL UPSERT (INSERT ... ON CONFLICT DO NOTHING) for atomic
		/// account creation. Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Success: Account created with Player access level and current timestamp.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid username, salt, or verifier
		/// - DB_CONSTRAINT_UNIQUE: Account name already exists
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> CreateAccountAsync(
			string accountName,
			string salt,
			string verifier,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves account authentication data for login.
		/// </summary>
		/// <param name="accountName">The account name. Must be 3-32 characters.</param>
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
		/// - VALIDATION_ERROR: Username validation failed
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
		Task<DatabaseResult<AccountData>> GetAccountForLoginAsync(
			string accountName,
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
		Task<DatabaseResult> UpdateLastLoginAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Checks if an account exists in the database.
		/// </summary>
		/// <param name="accountName">The account name to check. Must be 3-32 characters.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing true if account exists, false if not found.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Success: Returns true if account exists, false otherwise.
		/// Failure cases:
		/// - VALIDATION_ERROR: Username validation failed (treated as not exists)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// 
		/// The query uses AsNoTracking() for optimal performance since entity
		/// tracking is not required for existence checks.
		/// 
		/// Note: Returns false (not failure) for validation errors to maintain backwards
		/// compatibility and prevent enumeration attacks.
		/// </remarks>
		Task<DatabaseResult<bool>> AccountExistsAsync(
			string accountName,
			CancellationToken cancellationToken = default);
	}
}