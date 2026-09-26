using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The recurring charge for keeping land, and taking it back when it goes unpaid.
	/// </summary>
	public partial class HousingSystem
	{
		/// <summary>
		/// What keeping a plot costs each period.
		/// </summary>
		[Header("Tax")]
		[Tooltip("Charged to the owner every tax period. Zero disables tax entirely.")]
		[SerializeField]
		private long taxPerPeriod;

		/// <summary>
		/// How long a tax period lasts, in days.
		/// </summary>
		/// <remarks>
		/// Issue #121 asks for weekly or monthly, which is what the default reflects. It is a
		/// number rather than a choice between the two because a server that wants a fortnight
		/// should not have to pick the wrong one.
		/// </remarks>
		[Tooltip("Days between tax charges. 7 is weekly, 30 roughly monthly.")]
		[SerializeField]
		private float taxPeriodDays = 7f;

		/// <summary>
		/// How long an owner has to settle an overdue plot before it is taken back, in days.
		/// </summary>
		/// <remarks>
		/// Land is destroyed at the end of this, so it is measured in days rather than minutes: a
		/// player on holiday should not lose a house they paid for, and a player who has genuinely
		/// left should not hold land forever. Nothing is taken until it has been overdue for the
		/// whole grace period, and every period that falls due in between is billed again — a
		/// payment in full then lifts the missed-payment mark.
		/// </remarks>
		[Tooltip("Days an overdue plot is kept before it is reclaimed. Each period that falls due in between is billed again; paying it clears the arrears mark.")]
		[SerializeField]
		private float taxGraceDays = 14f;

		/// <summary>
		/// Seconds between tax sweeps.
		/// </summary>
		[Tooltip("Seconds between tax sweeps. Tax periods are days long, so this does not need to be short.")]
		[SerializeField]
		private float taxSweepIntervalSeconds = 300f;

		/// <summary>
		/// Most plots read per page of one world's sweep.
		/// </summary>
		/// <remarks>
		/// A page's offline owners are charged together in one transaction (see
		/// <see cref="IPlotService.ChargeTaxOfflineAsync"/>), so a page costs a handful of round trips
		/// rather than several per plot, and can be far larger than the 64 plots a sweep used to
		/// bill one at a time — a 10,000-plot backlog took about 13 hours to clear at that rate.
		/// </remarks>
		private const int TaxBatchSize = 256;

		/// <summary>
		/// Most pages one sweep reads from one world.
		/// </summary>
		/// <remarks>
		/// A server that has been down across a billing period comes back to every plot at once.
		/// Bounding the pages means that arrives over a few sweeps rather than as one that holds a
		/// worker for as long as the whole world takes — 4,096 plots a world per sweep.
		/// </remarks>
		private const int MaxTaxPagesPerSweep = 16;

		/// <summary>
		/// How long the world sweep leaves a plot alone once it has found the owner held by
		/// another server.
		/// </summary>
		/// <remarks>
		/// The server holding the owner bills them from its own sweep (see
		/// <see cref="SweepHeldOwnersAsync"/>), within one sweep interval. This only stops every
		/// other server re-reading the plot meanwhile — the head of the page used to fill with such
		/// plots, and every sweep on every server read the same ones and billed nobody behind them.
		/// Long enough that re-checking is rare, short enough that a holder which never bills
		/// (tax switched off there, or it went down holding the claim until the lease ran out) costs
		/// minutes, not the period.
		/// </remarks>
		private static readonly TimeSpan TaxOwnedElsewhereRetry = TimeSpan.FromMinutes(15);

		/// <summary>
		/// Seconds until the next tax sweep.
		/// </summary>
		/// <remarks>
		/// The first sweep runs shortly after startup (<see cref="RandomiseSweepPhases"/>), at a
		/// random point in a short window rather than at zero: at zero every scene server swept on
		/// its first frame and in lockstep, all reading the same page in the same order.
		/// </remarks>
		private float taxSweepCountdown;

		/// <summary>
		/// Earliest and latest seconds after startup at which the first tax sweep runs.
		/// </summary>
		/// <remarks>
		/// A world is billed only while some server hosts its housing scene, so a world that was
		/// unhosted, or a server that was down across a billing period, has bills waiting. Running
		/// the first sweep within a minute of startup collects them straight away instead of up to
		/// a whole interval later. Ten seconds gives the plot scenes time to resolve; the spread
		/// keeps servers started together from sweeping in step.
		/// </remarks>
		private const float TaxStartupSweepMinSeconds = 10f;
		private const float TaxStartupSweepMaxSeconds = 40f;

		/// <summary>
		/// Seconds between retries of the startup sweep while nothing is ready to sweep yet.
		/// </summary>
		private const float TaxStartupSweepRetrySeconds = 15f;

		/// <summary>
		/// True until the first tax sweep has been handed to the worker.
		/// </summary>
		/// <remarks>
		/// While set, a sweep that finds no hosted world and nobody held (the plot scenes have not
		/// resolved yet) retries in <see cref="TaxStartupSweepRetrySeconds"/> rather than waiting a
		/// full interval. It clears once a sweep is enqueued, or once an interval has passed since
		/// startup, so a server that hosts no housing at all stops retrying.
		/// </remarks>
		private bool taxStartupSweepPending;

		/// <summary>
		/// Seconds since startup, counted only while the startup sweep is pending.
		/// </summary>
		private float taxStartupElapsed;

		/// <summary>
		/// 1 while a sweep is running on the worker, 0 otherwise. Set on the main thread, cleared by
		/// the sweep itself whichever way it ends.
		/// </summary>
		/// <remarks>
		/// A sweep that takes longer than the interval — a backlog, or a slow database — used to be
		/// joined by the next one, and the two read and billed the same plots.
		/// </remarks>
		private int taxSweepInFlight;

		/// <summary>
		/// Picks where in their intervals this server's periodic sweeps start. Main thread only.
		/// </summary>
		private void RandomiseSweepPhases()
		{
			System.Random random = new System.Random();
			taxSweepCountdown = TaxStartupSweepMinSeconds +
				(float)(random.NextDouble() * (TaxStartupSweepMaxSeconds - TaxStartupSweepMinSeconds));
			taxStartupSweepPending = true;
			taxStartupElapsed = 0f;
			plotSyncCountdown = (float)(random.NextDouble() * Mathf.Max(1f, plotSyncIntervalSeconds));
		}

		/// <summary>
		/// True when this server charges tax at all.
		/// </summary>
		private bool IsTaxEnabled => IsHousingEnabled && taxPerPeriod > 0 && taxPeriodDays > 0f;

		/// <summary>
		/// When a plot claimed now would first fall due, or null when tax is off.
		/// </summary>
		/// <remarks>
		/// A new owner gets a full period before their first bill rather than being charged at the
		/// till — they have just paid the purchase price, and billing them again in the same breath
		/// reads as being charged twice.
		/// </remarks>
		private DateTime? NextTaxDueUtc()
		{
			if (!IsTaxEnabled)
			{
				return null;
			}

			return DateTime.UtcNow + TimeSpan.FromDays(Mathf.Max(0.001f, taxPeriodDays));
		}

		/// <summary>
		/// Runs the tax sweep on its interval, one at a time.
		/// </summary>
		private void TickTax(float deltaTime)
		{
			if (!IsTaxEnabled)
			{
				return;
			}

			if (taxStartupSweepPending)
			{
				taxStartupElapsed += deltaTime;
				if (taxStartupElapsed >= Mathf.Max(1f, taxSweepIntervalSeconds))
				{
					taxStartupSweepPending = false;
				}
			}

			taxSweepCountdown -= deltaTime;
			if (taxSweepCountdown > 0f)
			{
				return;
			}
			taxSweepCountdown = Mathf.Max(1f, taxSweepIntervalSeconds);

			/* A misconfigured currency is refused here rather than per plot. With no currency nobody
			 * can pay, so every owner held here would be marked unpaid on every sweep, and after the
			 * grace period every one of their houses would be taken — for a mistake in an asset. */
			if (currencyTemplate == null)
			{
				Log.Error("HousingSystem", "Tax is enabled but currencyTemplate is not assigned; no tax is charged and nothing is reclaimed until it is.");
				return;
			}

			if (System.Threading.Interlocked.CompareExchange(ref taxSweepInFlight, 1, 0) != 0)
			{
				Log.Debug("HousingSystem", "The previous tax sweep is still running; this interval's is skipped.");
				return;
			}

			/* Both gathered here, on the main thread: the scene mapping and the character map are
			 * main-thread state, and the sweep runs on the worker. The held set is a snapshot. An
			 * owner who arrives after it is treated as held elsewhere — the offline charge's
			 * assertion finds this server's claim — and is billed by the next sweep's held pass; one
			 * who leaves after it is charged in memory, finds nobody, and falls back to the row. */
			List<long> worlds = CollectHostedWorlds();
			long[] heldHere = CollectHeldCharacterIDs();
			if (worlds.Count < 1 && heldHere.Length < 1)
			{
				System.Threading.Interlocked.Exchange(ref taxSweepInFlight, 0);
				if (taxStartupSweepPending)
				{
					// The plot scenes have not resolved yet; try the startup sweep again shortly.
					taxSweepCountdown = TaxStartupSweepRetrySeconds;
				}
				return;
			}

			if (!TryEnqueueAsyncWork(() => SweepTaxAsync(worlds, heldHere)))
			{
				System.Threading.Interlocked.Exchange(ref taxSweepInFlight, 0);
				if (taxStartupSweepPending)
				{
					taxSweepCountdown = TaxStartupSweepRetrySeconds;
				}
				Log.Warning("HousingSystem", taxStartupSweepPending
					? "Could not enqueue the startup tax sweep; retrying shortly."
					: "Could not enqueue the tax sweep; it runs again next interval.");
				return;
			}
			taxStartupSweepPending = false;
		}

		/// <summary>
		/// The worlds whose plots this server has resolved.
		/// </summary>
		private List<long> CollectHostedWorlds()
		{
			List<long> worlds = new List<long>();

			foreach (int sceneHandle in PlotFoundation.Registry.Scenes)
			{
				if (TryResolveWorld(sceneHandle, out long worldServerID, out _) &&
					!worlds.Contains(worldServerID))
				{
					worlds.Add(worldServerID);
				}
			}

			return worlds;
		}

		/// <summary>
		/// The characters this server holds. Main thread only.
		/// </summary>
		private long[] CollectHeldCharacterIDs()
		{
			if (Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out ICharacterMappingData<NetworkConnection> mappingData) ||
				mappingData.CharactersByID == null ||
				mappingData.CharactersByID.Count < 1)
			{
				return Array.Empty<long>();
			}

			long[] ids = new long[mappingData.CharactersByID.Count];
			mappingData.CharactersByID.Keys.CopyTo(ids, 0);
			return ids;
		}

		/// <summary>
		/// Bills this server's own characters wherever their land is, then sweeps each hosted world.
		/// </summary>
		/// <remarks>
		/// <para>Two halves, because two different things decide who may bill a plot. An owner who is
		/// logged in may only be charged by the server holding them — their balance is in its memory
		/// — and that server may be hosting nothing of the world their land is in: a dungeon, a
		/// different region. The world sweep used to assume the holder swept that world too, found
		/// the owner held elsewhere, and left the plot for a server that never came. So the holder
		/// bills its own characters first, by owner rather than by world.</para>
		///
		/// <para>The world half then bills everybody nobody is holding, in batches, and steps past
		/// the rest. Safe to run from every scene server at once: winning a period is a locked
		/// compare on its due date, so a period produces one charge however many servers sweep it,
		/// which is why this needs no leader and survives any of them dying.</para>
		/// </remarks>
		private async Task SweepTaxAsync(List<long> worlds, long[] heldHere)
		{
			try
			{
				if (!TryGetDbService(out IPlotService plotService))
				{
					Log.Error("HousingSystem", "Tax sweep skipped: IPlotService unavailable.");
					return;
				}

				DateTime now = DateTime.UtcNow;
				TimeSpan grace = TimeSpan.FromDays(Mathf.Max(0f, taxGraceDays));
				TimeSpan period = TimeSpan.FromDays(Mathf.Max(0.001f, taxPeriodDays));

				// Plots this sweep has already acted on, so the world half does not act on them twice.
				HashSet<long> settled = new HashSet<long>();

				if (heldHere.Length > 0)
				{
					await SweepHeldOwnersAsync(plotService, heldHere, now, period, grace, settled);
				}

				HashSet<long> heldSet = new HashSet<long>(heldHere);
				foreach (long worldServerID in worlds)
				{
					await SweepWorldAsync(plotService, worldServerID, heldSet, now, period, grace, settled);
				}
			}
			catch (Exception ex)
			{
				Log.Error("HousingSystem", $"Tax sweep failed; the next interval runs it again: {ex}");
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref taxSweepInFlight, 0);
			}
		}

		/// <summary>
		/// Bills every due plot owned by a character this server holds, in any world.
		/// </summary>
		private async Task SweepHeldOwnersAsync(IPlotService plotService, long[] heldHere, DateTime now, TimeSpan period, TimeSpan grace, HashSet<long> settled)
		{
			DatabaseResult<List<PlotData>> due = await plotService.FetchTaxDueForOwnersAsync(heldHere, now);
			if (!due.IsSuccess || due.Data == null)
			{
				Log.Error("HousingSystem", $"Tax sweep could not read the plots this server's characters owe on: [{due.ErrorCode}] {due.ErrorMessage}");
				return;
			}

			foreach (PlotData plot in due.Data)
			{
				if (!plot.TaxDueUtc.HasValue || !settled.Add(plot.ID))
				{
					continue;
				}

				DateTime dueUtc = plot.TaxDueUtc.Value;
				PlotTaxRoute route = PlotTaxRouting.Route(
					PlotTaxDecision.Decide(plot.OwnerCharacterID, plot.OwnerGuildID, plot.TaxDelinquentSinceUtc, now, grace),
					ownerHeldHere: true);

				await SettleDuePlotAsync(plotService, plot, route, dueUtc, PlotTaxBilling.Bill(dueUtc, now, period, taxPerPeriod));
			}
		}

		/// <summary>
		/// Sweeps one world's due plots page by page, charging the offline owners of each page in
		/// one batch.
		/// </summary>
		private async Task SweepWorldAsync(IPlotService plotService, long worldServerID, HashSet<long> heldSet, DateTime now, TimeSpan period, TimeSpan grace, HashSet<long> settled)
		{
			DateTime afterDueUtc = DateTime.MinValue;
			long afterPlotID = 0;

			for (int page = 0; page < MaxTaxPagesPerSweep; ++page)
			{
				DatabaseResult<List<PlotData>> due = await plotService.FetchTaxDueAsync(worldServerID, now, afterDueUtc, afterPlotID, TaxBatchSize);
				if (!due.IsSuccess || due.Data == null)
				{
					Log.Error("HousingSystem", $"Tax sweep for world {worldServerID} failed: [{due.ErrorCode}] {due.ErrorMessage}");
					return;
				}
				if (due.Data.Count < 1)
				{
					return;
				}

				List<PlotTaxCharge> offline = new List<PlotTaxCharge>();
				foreach (PlotData plot in due.Data)
				{
					if (!plot.TaxDueUtc.HasValue || !settled.Add(plot.ID))
					{
						continue;
					}

					DateTime dueUtc = plot.TaxDueUtc.Value;
					PlotTaxRoute route = PlotTaxRouting.Route(
						PlotTaxDecision.Decide(plot.OwnerCharacterID, plot.OwnerGuildID, plot.TaxDelinquentSinceUtc, now, grace),
						heldSet.Contains(plot.OwnerCharacterID));

					/* Priced once, here, for whichever path carries it out: the in-memory charge and
					 * the batched one bill the same periods, the same amount, and move the date to the
					 * same place. */
					PlotTaxBill bill = PlotTaxBilling.Bill(dueUtc, now, period, taxPerPeriod);

					if (route == PlotTaxRoute.ChargeOffline)
					{
						if (bill.IsDue)
						{
							offline.Add(new PlotTaxCharge(plot.ID, plot.OwnerCharacterID, dueUtc, bill.NextDueUtc, bill.Amount));
						}
						continue;
					}

					await SettleDuePlotAsync(plotService, plot, route, dueUtc, bill);
				}

				if (offline.Count > 0)
				{
					await ChargeOfflineOwnersAsync(plotService, worldServerID, offline, now);
				}

				/* The cursor moves past this page whatever became of it. Plots that could not be
				 * settled keep their date and would otherwise head every page read after them. */
				PlotData last = due.Data[due.Data.Count - 1];
				afterDueUtc = last.TaxDueUtc ?? afterDueUtc;
				afterPlotID = last.ID;

				if (due.Data.Count < TaxBatchSize)
				{
					return;
				}
			}

			Log.Debug("HousingSystem", $"World {worldServerID} still has plots due after {MaxTaxPagesPerSweep} pages; the next sweep continues.");
		}

		/// <summary>
		/// Carries out the per-plot routes: reclamation, guild deferral, and charging an owner held here.
		/// </summary>
		/// <remarks>
		/// Grace runs from the <em>first</em> missed payment, not from the current due date. The due
		/// date has to move on every billing attempt — that is the pin which stops two servers
		/// charging the same period — so it advances whether or not any money was collected, and a
		/// plot that never pays would otherwise never look more than one period overdue.
		///
		/// <para>The missed-payment mark is cleared in the same commit as the payment it answers,
		/// never in a write after it. The sweep decides to reclaim before it decides to charge, from
		/// that mark alone, so a mark that outlived a payment — one failed write — took the house off
		/// an owner who had paid every period since.</para>
		///
		/// <para><paramref name="bill"/> covers every period that has fallen due
		/// (<see cref="PlotTaxBilling"/>): a plot several periods behind is charged for all of them
		/// at once, or marked unpaid for all of them, and its date moves past them all.</para>
		/// </remarks>
		private async Task SettleDuePlotAsync(IPlotService plotService, PlotData plot, PlotTaxRoute route, DateTime dueUtc, PlotTaxBill bill)
		{
			if (route != PlotTaxRoute.Reclaim && !bill.IsDue)
			{
				// Nothing to bill (a date the arithmetic cannot move on); the cursor steps past it.
				return;
			}

			switch (route)
			{
				case PlotTaxRoute.Reclaim:
					await ReclaimAsync(plotService, plot, plot.TaxDelinquentSinceUtc ?? dueUtc);
					return;

				case PlotTaxRoute.Defer:
					/* Past every period due, like a charge, not one period per sweep: stepped one at a
					 * time, a guild plot that had fallen behind stayed due and was read again by every
					 * sweep until it caught up. */
					DatabaseResult<int> deferred = await plotService.TryAdvanceTaxAsync(plot.ID, dueUtc, bill.NextDueUtc);
					if (!deferred.IsSuccess)
					{
						// Harmless — the plot is simply swept again — but not silent.
						Log.Warning("HousingSystem", $"Could not move guild plot {plot.ID}'s tax date on: [{deferred.ErrorCode}] {deferred.ErrorMessage}");
					}
					return;

				case PlotTaxRoute.ChargeHere:
					/* This server holds the character, so the balance lives in memory and the ordinary
					 * persistence path writes it. Winning the bill comes first — the advance is a
					 * compare-and-set on the date, so of every scene server sweeping this plot exactly
					 * one bills it — and only then is the money taken, so a lost race takes nothing. */
					if (await TryWinPeriodAsync(plotService, plot, dueUtc, bill.NextDueUtc))
					{
						await ChargeWonPeriodAsync(plotService, plot, dueUtc, bill);
					}
					return;

				default:
					/* None, or ChargeOffline, which only the world half batches. A plot the rule says
					 * to leave alone keeps its date; the cursor steps past it. */
					return;
			}
		}

		/// <summary>
		/// Charges one page's offline owners in a single transaction and reports what happened.
		/// </summary>
		/// <remarks>
		/// <para>Everything happens inside <see cref="IPlotService.ChargeTaxOfflineAsync"/>: the
		/// owners are locked and asserted unclaimed, the bills are won by locking the plots that
		/// still hold the date read, the money is taken — each bill in full or not at all — and the
		/// missed-payment marks and the ledger rows land in the same commit. An owner some server holds is not charged —
		/// deducting from the row underneath a server that holds them would be overwritten by its
		/// next save — and their plot steps aside until <see cref="TaxOwnedElsewhereRetry"/> has
		/// passed; the server holding them bills them.</para>
		/// <para>The unchanged-version write an earlier version of this path relied on was refused
		/// by the upsert's version guard every single time and reported as paid: free rent for
		/// anyone who logged off. The batch debits in place, under the row lock, at version + 1.</para>
		/// </remarks>
		private async Task ChargeOfflineOwnersAsync(IPlotService plotService, long worldServerID, List<PlotTaxCharge> charges, DateTime now)
		{
			DatabaseResult<List<PlotTaxChargeResult>> charged = await plotService.ChargeTaxOfflineAsync(
				charges,
				currencyTemplate.ID,
				now + TaxOwnedElsewhereRetry,
				(int)CurrencyMovementReason.LandTax,
				(int)CurrencyMovementState.Absorbed);

			if (!charged.IsSuccess || charged.Data == null)
			{
				Log.Warning("HousingSystem",
					$"Offline tax for {charges.Count} plot(s) in world {worldServerID} could not be charged; nothing was taken and the next sweep tries again: [{charged.ErrorCode}] {charged.ErrorMessage}");
				return;
			}

			int paid = 0, unpaid = 0, gone = 0, elsewhere = 0, skipped = 0;
			foreach (PlotTaxChargeResult result in charged.Data)
			{
				switch (result.Outcome)
				{
					case PlotTaxChargeOutcome.Paid: ++paid; break;
					case PlotTaxChargeOutcome.Unpaid: ++unpaid; break;
					case PlotTaxChargeOutcome.OwnerGone:
						++gone;
						Log.Debug("HousingSystem", $"Plot {result.PlotID}'s owner CharID={result.OwnerCharacterID} is deleted; the period is marked unpaid and the plot runs out its grace.");
						break;
					case PlotTaxChargeOutcome.OwnedElsewhere: ++elsewhere; break;
					default: ++skipped; break;
				}
			}

			Log.Debug("HousingSystem",
				$"Offline tax in world {worldServerID}: {paid} paid, {unpaid} unpaid, {gone} owner gone, {elsewhere} held elsewhere (deferred), {skipped} skipped.");
		}

		/// <summary>
		/// Wins the right to bill a plot's period for an owner who is online here, and lifts any
		/// earlier missed-payment mark, in one commit.
		/// </summary>
		/// <returns>True when this call won the period; false when another server did, or on a fault.</returns>
		/// <remarks>
		/// The mark comes off before the charge rather than after it, because after it is too late to
		/// be safe: the charge is taken in memory, and a separate write to clear the mark that then
		/// failed left a paying owner on the road to losing their house. Lifted first and put back if
		/// they then cannot pay (<see cref="MarkUnpaidAsync"/>, with the date of their first miss),
		/// the write that can fail is the one whose failure only lets a non-payer keep their house a
		/// little longer.
		/// </remarks>
		private async Task<bool> TryWinPeriodAsync(IPlotService plotService, PlotData plot, DateTime dueUtc, DateTime nextDueUtc)
		{
			if (!TryGetDbService(out IUnitOfWorkService unitOfWorkService))
			{
				Log.Error("HousingSystem", $"Tax for plot {plot.ID} skipped: IUnitOfWorkService unavailable.");
				return false;
			}

			DatabaseResult<IUnitOfWork> begin = await unitOfWorkService.BeginAsync();
			if (!begin.IsSuccess || begin.Data == null)
			{
				Log.Warning("HousingSystem", $"Tax for plot {plot.ID}: could not begin a unit of work: [{begin.ErrorCode}] {begin.ErrorMessage}");
				return false;
			}

			await using IUnitOfWork unitOfWork = begin.Data;

			DatabaseResult<int> advanced = await plotService.TryAdvanceTaxAsync(plot.ID, dueUtc, nextDueUtc);
			if (!advanced.IsSuccess)
			{
				await unitOfWork.RollbackAsync();
				Log.Warning("HousingSystem", $"Tax for plot {plot.ID}: could not advance its due date; the next sweep tries again: [{advanced.ErrorCode}] {advanced.ErrorMessage}");
				return false;
			}
			if (advanced.Data != 1)
			{
				// Another server billed this period.
				await unitOfWork.RollbackAsync();
				return false;
			}

			if (plot.TaxDelinquentSinceUtc.HasValue)
			{
				DatabaseResult<int> cleared = await plotService.ClearTaxDelinquencyAsync(plot.ID);
				if (!cleared.IsSuccess)
				{
					await unitOfWork.RollbackAsync();
					Log.Warning("HousingSystem", $"Tax for plot {plot.ID}: could not lift its missed-payment mark; the next sweep tries again: [{cleared.ErrorCode}] {cleared.ErrorMessage}");
					return false;
				}
			}

			DatabaseResult commit = await unitOfWork.CommitAsync();
			if (!commit.IsSuccess)
			{
				Log.Warning("HousingSystem", $"Tax for plot {plot.ID}: could not commit the period advance; the next sweep tries again: [{commit.ErrorCode}] {commit.ErrorMessage}");
				return false;
			}

			return true;
		}

		/// <summary>
		/// How many times a won period's charge is attempted before it is given up on.
		/// </summary>
		private const int WonPeriodChargeAttempts = 3;

		/// <summary>
		/// Seconds between attempts at a won period's charge, multiplied by the attempt number.
		/// </summary>
		private const float WonPeriodRetrySeconds = 2f;

		/// <summary>
		/// Charges an owner for a bill this server has already won: every period it covers, in full
		/// or not at all.
		/// </summary>
		/// <remarks>
		/// <para>The bill is spent the moment it is won — its date has moved on, and no server will
		/// pick it up again — so every way of failing to charge it is free rent. The owner is
		/// charged in memory if they are still here, from their stored row if they have gone.</para>
		///
		/// <para>The stored-row charge refuses while any server holds the character, and a character
		/// who has just logged out is still held by this one until their logout save hands the
		/// session back. That used to be read as "somebody else will bill them" and dropped, when the
		/// period was already won and nobody else ever would. So an outcome that is neither paid nor
		/// unpaid is tried again, a few seconds apart, which is what the logout needs.</para>
		/// </remarks>
		private async Task ChargeWonPeriodAsync(IPlotService plotService, PlotData plot, DateTime dueUtc, PlotTaxBill bill)
		{
			string lastOutcome = null;
			DateTime nextDueUtc = bill.NextDueUtc;

			for (int attempt = 1; attempt <= WonPeriodChargeAttempts; ++attempt)
			{
				if (attempt > 1)
				{
					await Task.Delay(TimeSpan.FromSeconds(WonPeriodRetrySeconds * (attempt - 1)));
				}

				OnlineChargeOutcome online = await TryChargeOnlineOwnerAsync(plot.OwnerCharacterID, bill.Amount);
				if (online == OnlineChargeOutcome.Charged)
				{
					/* No plot_updates mark: other channels draw a plot's owner, state, structures and
					 * guest list, and a payment changes none of them. */
					RecordLandTax(plot.OwnerCharacterID, bill.Amount);
					return;
				}
				if (online == OnlineChargeOutcome.Refused)
				{
					/* Unpaid — for the whole bill, as the batched charge treats one it cannot cover.
					 * The owner keeps the house until their grace runs out, which is the point of
					 * having one. */
					await MarkUnpaidAsync(plotService, plot, dueUtc);
					Log.Debug("HousingSystem", $"CharID={plot.OwnerCharacterID} could not pay {bill.Amount} tax ({bill.Periods} period(s)) on plot {plot.ID}.");
					return;
				}
				if (online == OnlineChargeOutcome.NotOnline)
				{
					/* Logged out between the sweep's snapshot and the charge. The bill is already
					 * ours, so the debit runs against the row without advancing it again. */
					OfflineChargeOutcome offline = await TryChargeWonPeriodFromRowAsync(plotService, plot, plot.OwnerCharacterID, bill.Amount);
					if (offline == OfflineChargeOutcome.Paid || offline == OfflineChargeOutcome.Unpaid)
					{
						await SettleOfflineOutcomeAsync(plotService, plot, dueUtc, bill, offline);
						return;
					}
					lastOutcome = offline.ToString();
				}
				else
				{
					lastOutcome = online.ToString();
				}
			}

			/* Given up. The period is handed back — the due date returns to dueUtc, pinned to the
			 * nextDueUtc this server set, so it cannot undo a later period somebody else has won —
			 * and the next sweep bills it again. It used to go unbilled: the date only moved
			 * forward (issue #267, IPlotService.TryRestoreTaxDueAsync).
			 *
			 * The owner's earlier miss, lifted when the period was won, is put back as well, so a
			 * charge this server could not make does not also forgive a debt they already had. A
			 * plot with no earlier miss is not marked: the failure was ours. */
			DatabaseResult<int> restored = await plotService.TryRestoreTaxDueAsync(plot.ID, nextDueUtc, dueUtc);
			if (plot.TaxDelinquentSinceUtc.HasValue)
			{
				await MarkUnpaidAsync(plotService, plot, dueUtc);
			}

			if (restored.IsSuccess && restored.Data == 1)
			{
				Log.Warning("HousingSystem",
					$"Plot {plot.ID}'s tax due {dueUtc:u} could not be charged to CharID={plot.OwnerCharacterID} in {WonPeriodChargeAttempts} attempts (last: {lastOutcome}); the period was handed back and is billed again on the next sweep.");
			}
			else
			{
				string why = restored.IsSuccess
					? "the due date had already moved on"
					: $"handing it back failed: [{restored.ErrorCode}] {restored.ErrorMessage}";
				Log.Warning("HousingSystem",
					$"Plot {plot.ID}'s tax due {dueUtc:u} went unbilled: CharID={plot.OwnerCharacterID} could not be charged in {WonPeriodChargeAttempts} attempts (last: {lastOutcome}), and {why}.");
			}
		}

		/// <summary>
		/// Records a missed payment, keeping the date of the owner's first miss.
		/// </summary>
		/// <remarks>
		/// The mark is only written when there is not one already, so passing the first miss — not
		/// this due date — is what keeps the grace clock running from it even after the mark was
		/// lifted for a charge that then did not go through.
		/// </remarks>
		private async Task MarkUnpaidAsync(IPlotService plotService, PlotData plot, DateTime dueUtc)
		{
			DatabaseResult<int> marked = await plotService.MarkTaxDelinquentAsync(plot.ID, plot.TaxDelinquentSinceUtc ?? dueUtc);
			if (!marked.IsSuccess)
			{
				Log.Warning("HousingSystem",
					$"Could not record CharID={plot.OwnerCharacterID}'s missed tax on plot {plot.ID}; their grace starts from a later miss: [{marked.ErrorCode}] {marked.ErrorMessage}");
			}
		}

		/// <summary>
		/// Takes an unpaid plot back and clears what was built on it.
		/// </summary>
		/// <remarks>
		/// The release is pinned to the owner the sweep read, so a plot sold or given up between the
		/// read and this write is not confiscated from whoever holds it now.
		/// </remarks>
		private async Task ReclaimAsync(IPlotService plotService, PlotData plot, DateTime delinquentSinceUtc)
		{
			/* The release, the move into the vault and the end of the guest list are one
			 * transaction; TryReleaseIntoVaultAsync says why they can no longer land apart. */
			if (!await TryReleaseIntoVaultAsync(plotService, plot))
			{
				return;
			}

			Log.Debug("HousingSystem",
				$"Plot {plot.ID} reclaimed: unpaid since {delinquentSinceUtc:u}, past the grace period.");

			/* This server's copies go with the rows. Left behind, the placement cache would keep
			 * reporting the vaulted house as occupying the ground — the next owner would find their
			 * own plot full of furniture nobody can see — and the foundations would keep the old
			 * keys until the sync caught up. Both are main-thread state, and this runs on the
			 * worker. */
			if (!TryEnqueueHousingMainThread(() => ForgetPlotContents(plot.ID)))
			{
				Log.Warning("HousingSystem", $"Could not clear this server's copy of reclaimed plot {plot.ID}; the plot sync clears its guest list, its placement cache the next time the scene resolves.");
			}

			MarkPlotChanged(plot.ID);
		}

		/// <summary>
		/// Applies the plot-side consequence of a won bill charged from the stored row:
		/// delinquency cleared or marked.
		/// </summary>
		private async Task SettleOfflineOutcomeAsync(IPlotService plotService, PlotData plot, DateTime dueUtc, PlotTaxBill bill, OfflineChargeOutcome outcome)
		{
			switch (outcome)
			{
				case OfflineChargeOutcome.Paid:
					// The missed-payment mark came off inside the payment's own transaction.
					RecordLandTax(plot.OwnerCharacterID, bill.Amount);
					return;
				case OfflineChargeOutcome.Unpaid:
					await MarkUnpaidAsync(plotService, plot, dueUtc);
					Log.Debug("HousingSystem", $"CharID={plot.OwnerCharacterID} could not pay {bill.Amount} tax ({bill.Periods} period(s)) on plot {plot.ID}.");
					return;
				default:
					/* Still held (mid-logout), or a fault already logged where it happened: nothing to
					 * settle here. ChargeWonPeriodAsync tries again, and hands the period back if it
					 * never can. */
					return;
			}
		}

		/// <summary>How an offline owner's tax charge ended.</summary>
		private enum OfflineChargeOutcome
		{
			/// <summary>The debit and the lifted missed-payment mark committed together.</summary>
			Paid = 0,
			/// <summary>The owner could not cover the tax; nothing was written.</summary>
			Unpaid = 1,
			/// <summary>A server holds the character's session — this one, mid-logout; nothing was touched.</summary>
			OwnedElsewhere = 2,
			/// <summary>A database fault; nothing was touched.</summary>
			Faulted = 4,
		}

		/// <summary>
		/// Charges a period this server has already won to an owner who logged out of it before the
		/// in-memory charge could be made, straight from their stored row.
		/// </summary>
		/// <remarks>
		/// <para>The one per-plot offline charge left: an ordinary offline owner is charged by the
		/// batch (<see cref="ChargeOfflineOwnersAsync"/>), which wins the period and takes the money
		/// in one transaction. This period is already won — its date moved when
		/// <see cref="TryWinPeriodAsync"/> advanced it — so the batch's pin would never match it.</para>
		/// <para>Everything happens inside one unit of work. The ownership assertion takes the
		/// character row's lock and answers "unclaimed" only while no server holds a session for
		/// it — so no scene server can be mid-save on an in-memory copy this write would then
		/// silently overwrite, and none can log the character in until the transaction ends. The
		/// debit is version-gated like every attribute write (<c>version + 1</c>, applied only if
		/// strictly newer). If it refuses, the whole thing rolls back and nothing moved.</para>
		/// <para>A payment also lifts the plot's missed-payment mark, inside the same transaction.
		/// Lifted by a write after the commit, as it used to be, a failure left the mark on a plot
		/// that had been paid for, and the next sweep reclaimed it by that mark alone.</para>
		/// </remarks>
		private async Task<OfflineChargeOutcome> TryChargeWonPeriodFromRowAsync(
			IPlotService plotService, PlotData plot, long characterID, long amount)
		{
			if (characterID <= 0 || amount <= 0 || currencyTemplate == null)
			{
				return OfflineChargeOutcome.Faulted;
			}

			if (!TryGetDbService(out ICharacterAttributeService attributeService) ||
				!TryGetDbService(out IUnitOfWorkService unitOfWorkService) ||
				!TryGetDbService(out ICharacterSessionOwnershipService ownershipService))
			{
				Log.Error("HousingSystem", $"Offline tax for CharID={characterID} skipped: a database service is unavailable.");
				return OfflineChargeOutcome.Faulted;
			}

			DatabaseResult<IUnitOfWork> begin = await unitOfWorkService.BeginAsync();
			if (!begin.IsSuccess || begin.Data == null)
			{
				Log.Warning("HousingSystem", $"Offline tax for CharID={characterID}: could not begin a unit of work: [{begin.ErrorCode}] {begin.ErrorMessage}");
				return OfflineChargeOutcome.Faulted;
			}

			await using IUnitOfWork unitOfWork = begin.Data;

			// Unclaimed, under the row lock, for the whole transaction.
			DatabaseResult ownership = await ownershipService.AssertOwnershipAsync(characterID, default, allowUnclaimed: true);
			if (!ownership.IsSuccess)
			{
				await unitOfWork.RollbackAsync();

				/* Forbidden is the answer that means what the caller acts on: a server holds the
				 * character. Anything else is a fault, and used to be reported as the same thing. */
				if (ownership.ErrorCode == DatabaseErrorCodes.Forbidden)
				{
					return OfflineChargeOutcome.OwnedElsewhere;
				}
				Log.Warning("HousingSystem", $"Offline tax for CharID={characterID}: the ownership check failed: [{ownership.ErrorCode}] {ownership.ErrorMessage}");
				return OfflineChargeOutcome.Faulted;
			}

			DatabaseResult<IReadOnlyList<CharacterAttributeData>> attributes = await attributeService.FetchAsync(characterID);
			if (!attributes.IsSuccess || attributes.Data == null)
			{
				await unitOfWork.RollbackAsync();
				Log.Warning("HousingSystem", $"Offline tax for CharID={characterID}: could not read their attributes: [{attributes.ErrorCode}] {attributes.ErrorMessage}");
				return OfflineChargeOutcome.Faulted;
			}

			int templateID = currencyTemplate.ID;
			CharacterAttributeData? currency = null;
			foreach (CharacterAttributeData attribute in attributes.Data)
			{
				if (attribute.TemplateID == templateID)
				{
					currency = attribute;
					break;
				}
			}

			bool canPay = currency.HasValue && currency.Value.Value >= amount;
			if (!canPay)
			{
				// Nothing to write: the period was won before this, and the caller marks the miss.
				await unitOfWork.RollbackAsync();
				return OfflineChargeOutcome.Unpaid;
			}

			CharacterAttributeData row = currency.Value;
			CharacterAttributeData updated = new CharacterAttributeData(
				row.ID,
				row.Version + 1,
				row.CharacterID,
				row.TemplateID,
				row.Value - (int)amount,
				row.CurrentValue);
			/* The ungated write, and deliberately so: every other per-character write quotes the
			 * session claim it was made under (CharacterWriteGate), but this one is made for a
			 * character NO server holds, and the assertion above — under the row lock, for the whole
			 * transaction — is what proves it. There is no claim to quote, and a gated write would
			 * refuse the very owner it exists to bill. */
			DatabaseResult<BulkWriteResult> persisted = await attributeService.PersistAsync(new List<CharacterAttributeData> { updated });
			if (!persisted.IsSuccess || persisted.Data.Applied != 1)
			{
				// Refused or superseded: the row moved under us. Nothing is billed this sweep.
				await unitOfWork.RollbackAsync();
				Log.Warning("HousingSystem", $"Offline tax for CharID={characterID}: the currency row could not be debited ({(persisted.IsSuccess ? "superseded" : $"[{persisted.ErrorCode}] {persisted.ErrorMessage}")}); rolled back.");
				return OfflineChargeOutcome.Faulted;
			}

			// Paid, so any earlier miss is settled — in this commit, not after it.
			DatabaseResult<int> cleared = await plotService.ClearTaxDelinquencyAsync(plot.ID);
			if (!cleared.IsSuccess)
			{
				await unitOfWork.RollbackAsync();
				Log.Warning("HousingSystem", $"Offline tax for plot {plot.ID}: could not lift its missed-payment mark; rolled back: [{cleared.ErrorCode}] {cleared.ErrorMessage}");
				return OfflineChargeOutcome.Faulted;
			}

			DatabaseResult commit = await unitOfWork.CommitAsync();
			if (!commit.IsSuccess)
			{
				Log.Warning("HousingSystem", $"Offline tax for CharID={characterID}: could not commit the payment: [{commit.ErrorCode}] {commit.ErrorMessage}");
				return OfflineChargeOutcome.Faulted;
			}
			return OfflineChargeOutcome.Paid;
		}

		private enum OnlineChargeOutcome
		{
			/// <summary>Nobody is playing this character on this server.</summary>
			NotOnline = 0,

			/// <summary>They were here and the money was taken.</summary>
			Charged = 1,

			/// <summary>They were here and could not pay.</summary>
			Refused = 2,

			/// <summary>
			/// The charge could not be attempted — the hop to the main thread was refused, the charge
			/// threw, or its save could not be queued. Nothing was taken, and it says nothing about
			/// whether they could pay.
			/// </summary>
			Unreachable = 3,
		}

		/// <summary>
		/// Charges an owner who is logged in here, through the same path any purchase uses.
		/// </summary>
		/// <remarks>
		/// Hops to the main thread and waits for the answer, because both the character map and the
		/// attribute controller are main-thread state. The wait cannot deadlock: the main thread
		/// drains this queue every frame and never blocks on the worker, and shutdown drains it in
		/// full rather than dropping it.
		/// </remarks>
		private Task<OnlineChargeOutcome> TryChargeOnlineOwnerAsync(long characterID, long amount)
		{
			TaskCompletionSource<OnlineChargeOutcome> completion =
				new TaskCompletionSource<OnlineChargeOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

			if (!TryEnqueueHousingMainThread(() =>
			{
				try
				{
					completion.TrySetResult(ChargeOnlineOwner(characterID, amount));
				}
				catch (System.Exception exception)
				{
					/* Not a refusal: a refusal marks the owner delinquent and starts the clock that
					 * eventually takes their house, and a bug on this server is not a reason to do that
					 * to them. Not "not online" either, which it used to be reported as: the
					 * stored-row charge refuses a character this server still holds, and the period
					 * — already won — went unbilled with nobody left to bill it. */
					Log.Error("HousingSystem", $"Charging online CharID={characterID} for tax threw: {exception.Message}");
					completion.TrySetResult(OnlineChargeOutcome.Unreachable);
				}
			}))
			{
				completion.TrySetResult(OnlineChargeOutcome.Unreachable);
			}

			return completion.Task;
		}

		/// <summary>
		/// Charges a logged-in owner. Main thread only.
		/// </summary>
		private OnlineChargeOutcome ChargeOnlineOwner(long characterID, long amount)
		{
			if (Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out ICharacterMappingData<NetworkConnection> mappingData) ||
				mappingData.CharactersByID == null ||
				!mappingData.CharactersByID.TryGetValue(characterID, out IPlayerCharacter player) ||
				player == null)
			{
				return OnlineChargeOutcome.NotOnline;
			}

			/* Affordability is asked first and on its own, because TrySpend's false covers two
			 * things: a balance that is too low, and a save that could not be queued (it refunds and
			 * reports the same false). Only the first is the owner's missed payment. */
			if (!CharacterCurrency.CanAfford(player, currencyTemplate, amount))
			{
				return OnlineChargeOutcome.Refused;
			}

			return CharacterCurrency.TrySpend(player, currencyTemplate, amount, () => TryPersistCurrency(player))
				? OnlineChargeOutcome.Charged
				: OnlineChargeOutcome.Unreachable;
		}

		/// <summary>
		/// Records a paid tax charge in the currency ledger.
		/// </summary>
		private void RecordLandTax(long characterID, long amount)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}

			if (!EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out ICurrencyLedgerService ledgerService))
				{
					Log.Warning("HousingSystem", $"Currency ledger: could not record {amount} (land tax) for CharID={characterID}: ICurrencyLedgerService unavailable.");
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(
					characterID,
					amount,
					(int)CurrencyMovementReason.LandTax,
					(int)CurrencyMovementState.Absorbed);

				if (!record.IsSuccess)
				{
					Log.Warning("HousingSystem",
						$"Currency ledger: could not record {amount} (land tax) for CharID={characterID}: [{record.ErrorCode}] {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Currency ledger: the persistence queue is saturated; the record for CharID={characterID} is still written, but late (behind the backlog, or through the teardown fallback).");
			}
		}
	}
}
