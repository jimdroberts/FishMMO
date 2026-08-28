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
	/// <para>
	/// This reports <b>pending migrations only</b>. It does not detect model drift — an entity
	/// changed with no migration generated for it. That check existed here and never worked: EF
	/// builds <c>ModelSnapshot.Model</c> with an empty convention set, so the relational model the
	/// differ needs is never attached and the comparison threw on every single startup. EF Core 5
	/// exposes no supported way to rebuild it (<c>SnapshotModelProcessor</c> is design-time only;
	/// <c>IModelRuntimeInitializer</c> and <c>HasPendingModelChanges</c> both postdate it), so the
	/// check was removed rather than left reporting a failure forever. See issue #162. Drift is
	/// caught by scaffolding a migration and asserting it is empty, which belongs in CI where the
	/// design-time package is available.
	/// </para>
	/// </remarks>
	public sealed class SchemaValidationResult
	{
		/// <summary>Migrations that exist but have not been applied to this database.</summary>
		public string[] PendingMigrations { get; }

		/// <summary>
		/// Why the check could not run at all, or null when it ran. When set, every other
		/// property is meaningless — nothing was determined.
		/// </summary>
		public string? UnavailableReason { get; }

		/// <summary>
		/// True when the check ran and found no pending migrations.
		/// </summary>
		/// <remarks>
		/// This is narrower than "the schema matches the entity model", and deliberately so. An
		/// entity changed without a migration generated leaves nothing pending and still reports
		/// up to date here — see the remarks on the class for why that gap is not closed at
		/// runtime.
		/// </remarks>
		public bool IsUpToDate =>
			UnavailableReason == null &&
			PendingMigrations.Length == 0;

		/// <summary>
		/// Creates a result.
		/// </summary>
		public SchemaValidationResult(string[] pendingMigrations, string? unavailableReason)
		{
			PendingMigrations = pendingMigrations ?? Array.Empty<string>();
			UnavailableReason = unavailableReason;
		}

		/// <summary>
		/// Creates a result for a check that could not be performed.
		/// </summary>
		/// <param name="reason">Why nothing could be determined.</param>
		public static SchemaValidationResult Unavailable(string? reason) =>
			new SchemaValidationResult(Array.Empty<string>(), reason ?? "unknown");

		/// <summary>
		/// Builds an operator-facing description of what is wrong and the command that fixes it,
		/// or null when there is nothing to report.
		/// </summary>
		/// <remarks>
		/// The remedy is included deliberately. The failure this guards against presents as
		/// missing player data, and someone reading it for the first time has no reason to
		/// connect that to a migration they were never told to run.
		/// </remarks>
		public string? DescribeProblem()
		{
			if (UnavailableReason != null)
			{
				return $"Database schema check could not run ({UnavailableReason}). " +
					"Proceeding, but an unapplied migration would not be detected.";
			}

			if (IsUpToDate)
			{
				return null;
			}

			return "Database schema does not match the entity model. " +
				$"This database has not applied {PendingMigrations.Length} migration(s): {string.Join(", ", PendingMigrations)}. " +
				"Apply with: dotnet ef database update -p FishMMO-Database/FishMMO-DB -s FishMMO-Database/FishMMO-DB-Migrator" +
				" Until this is done, queries touching the changed tables will fail at runtime — " +
				"which typically surfaces as missing data rather than as a schema error.";
		}
	}
}
