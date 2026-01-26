using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Tracks request-scoped idempotency for write operations.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A request is uniquely identified by <see cref="RequestID"/>. The record stores the finalized response
	/// (success or failure) so retries can return deterministically.
	/// </para>
	/// <para>
	/// Ownership is tracked via <see cref="OwnerID"/> and <see cref="LeaseExpiresAt"/> to safely distinguish
	/// same-call execution-strategy retries from truly concurrent processing by another node.
	/// </para>
	/// </remarks>
	[Table("processed_requests")]
	public sealed class ProcessedRequestEntity
	{
		/// <summary>
		/// Unique idempotency key for this logical request.
		/// </summary>
		public Guid RequestID { get; set; }

		/// <summary>
		/// Request scope identifier.
		/// </summary>
		public long ScopeID { get; set; }

		/// <summary>
		/// Logical operation name (schema-capped at 64 characters).
		/// </summary>
		public string OperationName { get; set; }

		/// <summary>
		/// Current request state (0=in progress, 1=success, 2=failure).
		/// </summary>
		public byte Status { get; set; }

		/// <summary>
		/// Identifier of the current request owner.
		/// </summary>
		public Guid OwnerID { get; set; }

		/// <summary>
		/// Lease expiry timestamp for the current owner.
		/// </summary>
		public DateTime LeaseExpiresAt { get; set; }

		/// <summary>
		/// Cached response payload stored as jsonb.
		/// </summary>
		public string Response { get; set; }

		/// <summary>
		/// Cached failure code, if the request completed as failure.
		/// </summary>
		public string ErrorCode { get; set; }

		/// <summary>
		/// Cached failure message, if the request completed as failure.
		/// </summary>
		public string ErrorMessage { get; set; }

		/// <summary>
		/// Creation timestamp.
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Completion timestamp, set when status becomes success/failure.
		/// </summary>
		public DateTime? CompletedAt { get; set; }
	}
}