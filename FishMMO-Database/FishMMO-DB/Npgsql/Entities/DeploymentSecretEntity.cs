using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Stores deployment-global secrets that are loaded by servers at startup,
	/// eliminating the need for env files to distribute secrets across machines.
	/// Each row is a key-value pair with creation and update timestamps.
	/// </summary>
	public class DeploymentSecretEntity
	{
		/// <summary>
		/// Primary key — the logical identifier for this secret (e.g., "client_gate_secret").
		/// </summary>
		public string Key { get; set; }

		/// <summary>
		/// The secret value (e.g., HMAC key material, AES KEK).
		/// </summary>
		public string Value { get; set; }

		/// <summary>
		/// UTC timestamp when this secret was first persisted.
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// UTC timestamp when this secret was last updated.
		/// </summary>
		public DateTime TimeUpdated { get; set; }
	}
}
