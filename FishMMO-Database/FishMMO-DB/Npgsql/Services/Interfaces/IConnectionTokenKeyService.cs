using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
    /// <summary>
    /// Service interface for managing connection token HMAC keys.
    /// The IpFetchServer registers its signing key via <see cref="UpsertAsync"/>;
    /// game servers (Login/World/Scene) fetch all active keys via <see cref="FetchAllActiveAsync"/>
    /// to verify stateless connection tokens without environment variable coordination.
    /// </summary>
    public interface IConnectionTokenKeyService
    {
        /// <summary>
        /// Inserts or updates the active HMAC key for the given <paramref name="keyId"/>.
        /// On insert, a new row is created with <c>IsActive = true</c>.
        /// On update (existing <paramref name="keyId"/>), the key material is replaced,
        /// the key is marked active, and <c>DeactivatedAt</c> is cleared.
        /// </summary>
        /// <param name="keyId">Logical key identifier (e.g., region code). Must be non-empty and at most 255 characters.</param>
        /// <param name="hmacKey">HMAC-SHA256 key material. Must be at least 32 bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="DatabaseResult{T}"/> containing the upserted key data on success.</returns>
        Task<DatabaseResult<ConnectionTokenKeyData>> UpsertAsync(
            string keyId,
            byte[] hmacKey,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches all active connection token keys.
        /// Called periodically by game servers to refresh their in-memory key map.
        /// Results are ordered by <c>TimeCreated</c> descending (newest first).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="DatabaseResult{T}"/> containing an array of active key data.</returns>
        Task<DatabaseResult<ConnectionTokenKeyData[]>> FetchAllActiveAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches a single connection token key by its logical key identifier.
        /// </summary>
        /// <param name="keyId">The logical key identifier to look up.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="DatabaseResult{T}"/> containing the key data if found; otherwise a failure with ENTITY_NOT_FOUND.</returns>
        Task<DatabaseResult<ConnectionTokenKeyData>> FetchByKeyIdAsync(
            string keyId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a dictionary mapping key ID to raw HMAC key bytes for all active keys.
        /// Each value is the base64-decoded HMAC key material.
        /// Convenience method for game servers that need the key material in byte form.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="DatabaseResult{T}"/> containing the key ID to raw key bytes map.</returns>
        Task<DatabaseResult<Dictionary<string, byte[]>>> GetConnectionTokenKeyMapAsync(
            CancellationToken cancellationToken = default);
    }
}
