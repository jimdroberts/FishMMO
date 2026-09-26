using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Source pins for the tax sweep's shape that no behavioural test can reach without a server
	/// and a database: which writes mark a plot for the other channels, and that both charge paths
	/// bill the one bill the sweep priced.
	/// </summary>
	[TestFixture]
	public class PlotTaxSourcePinsTests
	{
		private const string TaxPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Housing/HousingSystem.Tax.cs";
		private const string PlotServicePath = "../FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Housing/PlotService.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		private static int Count(string source, string needle)
		{
			int count = 0;
			for (int at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>
		/// A plot_updates mark tells every other channel to re-read a plot and its guest list, and
		/// they draw only its owner, state, structures and access. A tax payment changes none of
		/// them; a reclamation changes the owner. So of the tax code, only reclamation marks.
		/// </summary>
		[Test]
		public void OnlyReclamation_MarksAPlotChanged()
		{
			string source = ReadSource(TaxPath);

			LogAssert.AreEqual(1, Count(source, "MarkPlotChanged("),
				"the tax sweep must mark a plot changed in exactly one place");

			string reclaim = MethodBody(source, "private async Task ReclaimAsync(", "private async Task SettleOfflineOutcomeAsync(");
			LogAssert.IsTrue(reclaim.Contains("MarkPlotChanged(plot.ID);"),
				"reclaiming changes the owner, which every channel draws, so it must mark the plot");
		}

		[Test]
		public void TheOfflineBatch_DoesNotMarkPlotUpdates()
		{
			string body = MethodBody(ReadSource(PlotServicePath),
				"public async Task<DatabaseResult<List<PlotTaxChargeResult>>> ChargeTaxOfflineAsync(",
				"public async Task<DatabaseResult<int>> TryAdvanceTaxAsync(");

			LogAssert.IsFalse(body.Contains("GetTableName<PlotUpdateEntity>()"),
				"a tax charge changes nothing another channel draws, so the batch must not write plot_updates");
		}

		/// <summary>
		/// The batched charge debits each owner by their own bill, and only when the balance covers
		/// all of it: the same all-or-nothing question the in-memory charge asks.
		/// </summary>
		[Test]
		public void TheOfflineBatch_DebitsEachBillInFullOrNotAtAll()
		{
			string body = MethodBody(ReadSource(PlotServicePath),
				"public async Task<DatabaseResult<List<PlotTaxChargeResult>>> ChargeTaxOfflineAsync(",
				"public async Task<DatabaseResult<int>> TryAdvanceTaxAsync(");

			LogAssert.IsTrue(body.Contains("SET value = a.value - u.amount"),
				"each owner must be debited by their own bill's amount");
			LogAssert.IsTrue(body.Contains("AND a.value >= u.amount"),
				"a bill the balance cannot cover must debit nothing");
			LogAssert.IsTrue(body.Contains("charge.Amount <= 0"),
				"a charge with no amount must be refused, not recorded as paid");
		}

		/// <summary>
		/// The sweep prices each due plot once and hands the same bill to whichever path carries
		/// it out. Advancing by one period at a time is what left a plot that had fallen behind
		/// due, and re-read by every sweep, until it caught up.
		/// </summary>
		[Test]
		public void BothChargePaths_BillTheOneBillTheSweepPriced()
		{
			string source = ReadSource(TaxPath);

			LogAssert.IsFalse(source.Contains("dueUtc + period"),
				"no path may advance a plot by a single period; every path moves it past every period due");

			string held = MethodBody(source, "private async Task SweepHeldOwnersAsync(", "private async Task SweepWorldAsync(");
			LogAssert.IsTrue(held.Contains("PlotTaxBilling.Bill(dueUtc, now, period, taxPerPeriod)"),
				"the held owners' half must price its plots with PlotTaxBilling");

			string world = MethodBody(source, "private async Task SweepWorldAsync(", "private async Task SettleDuePlotAsync(");
			LogAssert.IsTrue(world.Contains("PlotTaxBill bill = PlotTaxBilling.Bill(dueUtc, now, period, taxPerPeriod);"),
				"the world half must price its plots with PlotTaxBilling");
			LogAssert.IsTrue(world.Contains("new PlotTaxCharge(plot.ID, plot.OwnerCharacterID, dueUtc, bill.NextDueUtc, bill.Amount)"),
				"the batched charge must be handed that bill's date and amount");

			string won = MethodBody(source, "private async Task ChargeWonPeriodAsync(", "private async Task MarkUnpaidAsync(");
			LogAssert.IsTrue(won.Contains("TryChargeOnlineOwnerAsync(plot.OwnerCharacterID, bill.Amount)"),
				"the in-memory charge must take the bill's whole amount");
			LogAssert.IsTrue(won.Contains("TryChargeWonPeriodFromRowAsync(plotService, plot, plot.OwnerCharacterID, bill.Amount)"),
				"the stored-row fallback must take the same amount");
		}
	}
}
