using System;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// The outcome of comparing the database schema against the entity model.
	/// </summary>
	/// <remarks>
	/// Produced by <see cref="NpgsqlDbContextFactory.ValidateSchemaAsync"/>. Deliberately a
	/// report rather than a thrown exception: whether a schema mismatch should stop a server or
	/// merely warn depends on the server, and that call belongs to the caller.
	/// </remarks>
	public sealed class SchemaValidationResult
	{
		/// <summary>Migrations that exist but have not been applied to this database.</summary>
		public string[] PendingMigrations { get; }

		/// <summary>
		/// True when the entity model has changed since the newest migration was created, so no
		/// migration covering the change exists yet.
		/// </summary>
		public bool ModelChangedSinceLastMigration { get; }

		/// <summary>
		/// True when drift could not be evaluated. The pending-migration list is still valid;
		/// only the model comparison was skipped.
		/// </summary>
		public bool DriftCheckFailed { get; }

		/// <summary>
		/// Why the check could not run at all, or null when it ran. When set, every other
		/// property is meaningless — nothing was determined.
		/// </summary>
		public string UnavailableReason { get; }

		/// <summary>True when the check ran and found nothing wrong.</summary>
		public bool IsUpToDate =>
			UnavailableReason == null &&
			PendingMigrations.Length == 0 &&
			!ModelChangedSinceLastMigration;

		/// <summary>
		/// Creates a result.
		/// </summary>
		public SchemaValidationResult(string[] pendingMigrations, bool modelChangedSinceLastMigration, bool driftCheckFailed, string unavailableReason)
		{
			PendingMigrations = pendingMigrations ?? Array.Empty<string>();
			ModelChangedSinceLastMigration = modelChangedSinceLastMigration;
			DriftCheckFailed = driftCheckFailed;
			UnavailableReason = unavailableReason;
		}

		/// <summary>
		/// Creates a result for a check that could not be performed.
		/// </summary>
		/// <param name="reason">Why nothing could be determined.</param>
		public static SchemaValidationResult Unavailable(string reason) =>
			new SchemaValidationResult(Array.Empty<string>(), false, false, reason ?? "unknown");

		/// <summary>
		/// Builds an operator-facing description of what is wrong and the command that fixes it,
		/// or null when there is nothing to report.
		/// </summary>
		/// <remarks>
		/// The remedy is included deliberately. The failure this guards against presents as
		/// missing player data, and someone reading it for the first time has no reason to
		/// connect that to a migration they were never told to run.
		/// </remarks>
		public string DescribeProblem()
		{
			if (UnavailableReason != null)
			{
				return $"Database schema check could not run ({UnavailableReason}). " +
					"Proceeding, but a schema mismatch would not be detected.";
			}

			if (IsUpToDate)
			{
				return null;
			}

			string detail = ModelChangedSinceLastMigration
				? "The entity model has changed since the last migration was created, so no migration covers it yet. " +
				  "Run: dotnet ef migrations add <name> -p FishMMO-Database/FishMMO-DB -s FishMMO-Database/FishMMO-DB-Migrator -o ../../Migrations"
				: $"This database has not applied {PendingMigrations.Length} migration(s): {string.Join(", ", PendingMigrations)}.";

			string update = "Then apply with: dotnet ef database update -p FishMMO-Database/FishMMO-DB -s FishMMO-Database/FishMMO-DB-Migrator";

			return "Database schema does not match the entity model. " + detail + " " + update +
				" Until this is done, queries touching the changed tables will fail at runtime — " +
				"which typically surfaces as missing data rather than as a schema error.";
		}
	}
}
