using System;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Starting a maintenance window is safe to retry after a reply lost past the commit.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was wrong.</b> <c>MaintenanceService.StartAsync</c> plans the window inside
	/// <c>ExecuteTransactionAsync</c>, which retries a lost connection, and a connection can drop
	/// after the commit but before its reply. The retry planned again, found the first attempt's
	/// targets live, and refused itself with "already in maintenance window N": the operator was told
	/// the window had failed while the advance pass went on to lock and stop every server in it.
	/// </para>
	/// <para>
	/// Now the plan carries a <c>request_key</c> taken once per call, outside the retried delegate,
	/// and an attempt that finds its own key returns that window. A unique index over the non-null
	/// keys stands behind it, as on the other nine tables with one. Verified against a throwaway
	/// PostgreSQL through a proxy that drops the reply to the plan's <c>COMMIT</c>: the previous
	/// build refused itself (the control fails), this one returns the window its first attempt
	/// planned, and a different request naming the same server is still refused.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MaintenanceStartRetryTests
	{
		private const string MaintenanceServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Maintenance/MaintenanceService.cs";
		private const string ConfigurationPath = "../FishMMO-Database/FishMMO-DB/Npgsql/EntityConfigurations/Maintenance/MaintenanceEntityConfigurations.cs";
		private const string EntityPath = "../FishMMO-Database/FishMMO-DB/Npgsql/Entities/Maintenance/MaintenanceOperationEntity.cs";

		[Test]
		public void TheStart_TakesItsKeyOnce_AndARetryFindsItsOwnWindowFirst()
		{
			string code = SourceScanPins.ReadCode(MaintenanceServicePath);
			Func<string, string> check = source =>
			{
				string body = SourceScanPins.Body(source, "public async Task<DatabaseResult<MaintenanceOperationData>> StartAsync(");
				if (body == null)
				{
					return "StartAsync was not found";
				}

				/* The key outside the retried delegate; inside it, the probe before the clock read
				 * and before the clash check, which is the check the retry used to fail; and the key
				 * written with the plan. */
				string order = SourceScanPins.InOrder(body,
					"Guid requestKey = Guid.NewGuid();",
					"var created = await ExecuteTransactionAsync(",
					".Where(o => o.RequestKey == requestKey)",
					"return new PlanResult { OperationID = planned };",
					"DateTime now = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken)",
					"var liveTargets =",
					"RequestKey = requestKey,");
				if (order != null)
				{
					return order;
				}
				string retried = body.Substring(body.IndexOf("var created = await ExecuteTransactionAsync(", StringComparison.Ordinal));
				return retried.Contains("Guid.NewGuid()")
					? "the request key is taken inside the retried delegate, so a retry carries a different one"
					: null;
			};

			SourceScanPins.HoldsAndFires("StartAsync probe", code, check,
				SourceScanPins.Replace(".Where(o => o.RequestKey == requestKey)", ".Where(o => false)"),
				"the retry no longer looks for its own window");
			SourceScanPins.HoldsAndFires("StartAsync key once", code, check,
				s => SourceScanPins.InsertBefore("long planned = await", "Guid requestKey = Guid.NewGuid();\n\t\t\t\t")(
					s.Replace("Guid requestKey = Guid.NewGuid();\n", string.Empty)),
				"the key taken per attempt");
			SourceScanPins.HoldsAndFires("StartAsync key written", code, check,
				SourceScanPins.Replace("RequestKey = requestKey,", string.Empty),
				"the plan written without its key");
		}

		[Test]
		public void TheWindowTable_HasANullableRequestKey_UniqueOverTheNonNullValues()
		{
			string entity = SourceScanPins.ReadCode(EntityPath);
			LogAssert.IsTrue(entity.Contains("public Guid? RequestKey { get; set; }"),
				"maintenance_operations.request_key is a nullable uuid, like the other tables that carry one");

			string configuration = SourceScanPins.ReadCode(ConfigurationPath);
			SourceScanPins.HoldsAndFires("MaintenanceOperationEntityConfiguration", configuration,
				source =>
				{
					string body = SourceScanPins.Body(source, "public void Configure(EntityTypeBuilder<MaintenanceOperationEntity> builder)");
					if (body == null)
					{
						return "the window's configuration was not found";
					}
					return body.Contains("builder.HasIndex(e => e.RequestKey)\n\t\t\t\t.IsUnique()\n\t\t\t\t.HasFilter(\"request_key IS NOT NULL\");")
						? null
						: "request_key has no unique index filtered to the non-null values";
				},
				SourceScanPins.Replace("\t\t\t\t.IsUnique()\n\t\t\t\t.HasFilter(\"request_key IS NOT NULL\");", "\t\t\t\t.HasFilter(\"request_key IS NOT NULL\");"),
				"the index no longer unique, so a retry's duplicate is not refused by the database");
		}
	}
}
