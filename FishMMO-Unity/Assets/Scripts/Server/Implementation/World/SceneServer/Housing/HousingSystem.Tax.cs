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
		/// whole grace period, and every sweep in between tries the charge again.
		/// </remarks>
		[Tooltip("Days an overdue plot is kept before it is reclaimed. The owner is charged again on every sweep in between.")]
		[SerializeField]
		private float taxGraceDays = 14f;

		/// <summary>
		/// Seconds between tax sweeps.
		/// </summary>
		[Tooltip("Seconds between tax sweeps. Tax periods are days long, so this does not need to be short.")]
		[SerializeField]
		private float taxSweepIntervalSeconds = 300f;

		/// <summary>
		/// Most plots charged in one sweep.
		/// </summary>
		/// <remarks>
		/// A server that has been down across a billing period comes back to every plot at once.
		/// Bounding the batch means that arrives as several sweeps rather than one that tries to
		/// charge the entire world in a single pass.
		/// </remarks>
		private const int TaxBatchSize = 64;

		/// <summary>
		/// Seconds until the next tax sweep.
		/// </summary>
		private float taxSweepCountdown;

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
		/// Runs the tax sweep on its interval.
		/// </summary>
		private void TickTax(float deltaTime)
		{
			if (!IsTaxEnabled)
			{
				return;
			}

			taxSweepCountdown -= deltaTime;
			if (taxSweepCountdown > 0f)
			{
				return;
			}
			taxSweepCountdown = Mathf.Max(1f, taxSweepIntervalSeconds);

			/* Swept per world this server is hosting scenes for, not globally. A scene server holds
			 * scenes for several worlds, and each world's land is its own. */
			foreach (long worldServerID in CollectHostedWorlds())
			{
				long world = worldServerID;
				if (!TryEnqueueAsyncWork(() => SweepTaxAsync(world)))
				{
					Log.Warning("HousingSystem", $"Could not enqueue the tax sweep for world {world}.");
				}
			}
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
		/// Charges every plot that has come due, and reclaims the ones that have run out of grace.
		/// </summary>
		/// <remarks>
		/// Safe to run from every scene server hosting the world at once. Winning the right to
		/// charge is a pinned update, so a period produces one payment however many servers sweep —
		/// which is why this needs no leader and survives any of them dying.
		/// </remarks>
		private async Task SweepTaxAsync(long worldServerID)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				Log.Error("HousingSystem", $"Tax sweep for world {worldServerID} skipped: IPlotService unavailable.");
				return;
			}

			DateTime now = DateTime.UtcNow;

			DatabaseResult<List<PlotData>> due = await plotService.FetchTaxDueAsync(worldServerID, now, TaxBatchSize);
			if (!due.IsSuccess || due.Data == null)
			{
				Log.Error("HousingSystem", $"Tax sweep for world {worldServerID} failed: [{due.ErrorCode}] {due.ErrorMessage}");
				return;
			}
			if (due.Data.Count < 1)
			{
				return;
			}

			TimeSpan grace = TimeSpan.FromDays(Mathf.Max(0f, taxGraceDays));
			TimeSpan period = TimeSpan.FromDays(Mathf.Max(0.001f, taxPeriodDays));

			foreach (PlotData plot in due.Data)
			{
				if (!plot.TaxDueUtc.HasValue)
				{
					continue;
				}

				await ProcessDuePlotAsync(plotService, plot, plot.TaxDueUtc.Value, now, period, grace);
			}
		}

		/// <summary>
		/// Charges one overdue plot, or reclaims it when its grace has run out.
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
		/// </remarks>
		private async Task ProcessDuePlotAsync(
			IPlotService plotService,
			PlotData plot,
			DateTime dueUtc,
			DateTime now,
			TimeSpan period,
			TimeSpan grace)
		{
			PlotTaxAction action = PlotTaxDecision.Decide(
				plot.OwnerCharacterID,
				plot.OwnerGuildID,
				plot.TaxDelinquentSinceUtc,
				now,
				grace);

			if (action == PlotTaxAction.None)
			{
				return;
			}

			if (action == PlotTaxAction.Reclaim)
			{
				await ReclaimAsync(plotService, plot, plot.TaxDelinquentSinceUtc ?? dueUtc);
				return;
			}

			if (action == PlotTaxAction.Defer)
			{
				DatabaseResult<int> deferred = await plotService.TryAdvanceTaxAsync(plot.ID, dueUtc, dueUtc + period);
				if (!deferred.IsSuccess)
				{
					// Harmless — the plot is simply swept again — but not silent.
					Log.Warning("HousingSystem", $"Could not move guild plot {plot.ID}'s tax date on: [{deferred.ErrorCode}] {deferred.ErrorMessage}");
				}
				return;
			}

			/* Two shapes of charge, and the difference is who is authoritative for the money.
			 *
			 * ONLINE HERE: this server holds the character, so the attribute lives in memory and
			 * the ordinary persistence path writes it. Winning the right to charge comes first —
			 * the plot row's tax advance is a compare-and-set, so of every scene server sweeping
			 * this world exactly one bills the period — and only then is the money taken.
			 *
			 * OFFLINE: nobody holds the character. The money is taken straight from the row, and
			 * the advance and the debit are ONE transaction that also asserts, under the row
			 * lock, that no server has claimed the character. A second server sweeping the same
			 * plot either finds the period already advanced (its transaction rolls back, no
			 * money moves) or finds the character claimed (it skips, no period consumed). The
			 * unchanged-version write this replaced was refused by the upsert's version guard
			 * every single time and reported as paid: free rent for anyone who logged off.
			 *
			 * ONLINE ELSEWHERE: the offline transaction's ownership assertion fails. Nothing is
			 * charged and nothing is advanced; the server that holds the character sweeps this
			 * world too and bills them there. */
			if (!await IsOwnerOnlineHereAsync(plot.OwnerCharacterID))
			{
				await SettleOfflineOutcomeAsync(plotService, plot, dueUtc,
					await TryChargeOfflineOwnerAsync(plotService, plot, dueUtc, dueUtc + period, plot.OwnerCharacterID, taxPerPeriod, advancePeriod: true));
				return;
			}

			/* Online here: the period is won before any money moves, so a lost race takes
			 * nothing from the player. The probe above spent nothing either. */
			if (!await TryWinPeriodAsync(plotService, plot, dueUtc, dueUtc + period))
			{
				return;
			}

			await ChargeWonPeriodAsync(plotService, plot, dueUtc, dueUtc + period);
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
		/// Charges an owner for a period this server has already won.
		/// </summary>
		/// <remarks>
		/// <para>The period is spent the moment it is won — its date has moved on, and no server will
		/// pick it up again — so every way of failing to charge it is a free period. The owner is
		/// charged in memory if they are still here, from their stored row if they have gone.</para>
		///
		/// <para>The stored-row charge refuses while any server holds the character, and a character
		/// who has just logged out is still held by this one until their logout save hands the
		/// session back. That used to be read as "somebody else will bill them" and dropped, when the
		/// period was already won and nobody else ever would. So an outcome that is neither paid nor
		/// unpaid is tried again, a few seconds apart, which is what the logout needs.</para>
		/// </remarks>
		private async Task ChargeWonPeriodAsync(IPlotService plotService, PlotData plot, DateTime dueUtc, DateTime nextDueUtc)
		{
			string lastOutcome = null;

			for (int attempt = 1; attempt <= WonPeriodChargeAttempts; ++attempt)
			{
				if (attempt > 1)
				{
					await Task.Delay(TimeSpan.FromSeconds(WonPeriodRetrySeconds * (attempt - 1)));
				}

				OnlineChargeOutcome online = await TryChargeOnlineOwnerAsync(plot.OwnerCharacterID, taxPerPeriod);
				if (online == OnlineChargeOutcome.Charged)
				{
					RecordLandTax(plot.OwnerCharacterID, taxPerPeriod);
					MarkPlotChanged(plot.ID);
					return;
				}
				if (online == OnlineChargeOutcome.Refused)
				{
					/* Unpaid. The owner keeps the house until their grace runs out, which is the
					 * point of having one. */
					await MarkUnpaidAsync(plotService, plot, dueUtc);
					Log.Debug("HousingSystem", $"CharID={plot.OwnerCharacterID} could not pay {taxPerPeriod} tax on plot {plot.ID}.");
					return;
				}
				if (online == OnlineChargeOutcome.NotOnline)
				{
					/* Logged out between the probe and the charge. The period is already ours, so the
					 * debit runs against the row without advancing it again. */
					OfflineChargeOutcome offline = await TryChargeOfflineOwnerAsync(plotService, plot, dueUtc, nextDueUtc, plot.OwnerCharacterID, taxPerPeriod, advancePeriod: false);
					if (offline == OfflineChargeOutcome.Paid || offline == OfflineChargeOutcome.Unpaid)
					{
						await SettleOfflineOutcomeAsync(plotService, plot, dueUtc, offline);
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
		/// Applies the plot-side consequence of an offline charge: delinquency cleared or marked,
		/// or nothing at all when another server owns the outcome.
		/// </summary>
		private async Task SettleOfflineOutcomeAsync(IPlotService plotService, PlotData plot, DateTime dueUtc, OfflineChargeOutcome outcome)
		{
			switch (outcome)
			{
				case OfflineChargeOutcome.Paid:
					// The missed-payment mark came off inside the payment's own transaction.
					RecordLandTax(plot.OwnerCharacterID, taxPerPeriod);
					MarkPlotChanged(plot.ID);
					return;
				case OfflineChargeOutcome.Unpaid:
					await MarkUnpaidAsync(plotService, plot, dueUtc);
					Log.Debug("HousingSystem", $"CharID={plot.OwnerCharacterID} could not pay {taxPerPeriod} tax on plot {plot.ID}.");
					return;
				case OfflineChargeOutcome.OwnedElsewhere:
					Log.Debug("HousingSystem", $"CharID={plot.OwnerCharacterID} is claimed by another server; leaving plot {plot.ID}'s tax for that server's sweep.");
					return;
				default:
					// PeriodAlreadyBilled, or a fault already logged where it happened: nothing to settle here.
					return;
			}
		}

		/// <summary>
		/// Whether this server holds the owner's character. Main-thread lookup; spends nothing.
		/// </summary>
		private Task<bool> IsOwnerOnlineHereAsync(long characterID)
		{
			TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!TryEnqueueHousingMainThread(() =>
			{
				bool here = Server?.DataContainerRegistry != null &&
					Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out ICharacterMappingData<NetworkConnection> mappingData) &&
					mappingData.CharactersByID != null &&
					mappingData.CharactersByID.ContainsKey(characterID);
				completion.TrySetResult(here);
			}))
			{
				completion.TrySetResult(false);
			}
			return completion.Task;
		}

		/// <summary>How an offline owner's tax charge ended.</summary>
		private enum OfflineChargeOutcome
		{
			/// <summary>The debit, the period advance and the lifted missed-payment mark committed together.</summary>
			Paid = 0,
			/// <summary>The owner could not cover the tax; the period advance committed alone.</summary>
			Unpaid = 1,
			/// <summary>A server holds the character's session — possibly this one, mid-logout; nothing was touched.</summary>
			OwnedElsewhere = 2,
			/// <summary>Another server advanced this period first; the debit rolled back.</summary>
			PeriodAlreadyBilled = 3,
			/// <summary>A database fault; nothing was touched.</summary>
			Faulted = 4,
		}

		/// <summary>
		/// Charges an owner nobody is hosting, straight from the database, atomically with the
		/// plot's period advance.
		/// </summary>
		/// <remarks>
		/// <para>Everything happens inside one unit of work. The ownership assertion takes the
		/// character row's lock and answers "unclaimed" only while no server holds a session for
		/// it — so no scene server can be mid-save on an in-memory copy this write would then
		/// silently overwrite, and none can log the character in until the transaction ends. The
		/// debit is version-gated like every attribute write (<c>version + 1</c>, applied only if
		/// strictly newer), and the advance is the plot service's compare-and-set. If either
		/// refuses, the whole thing rolls back and nothing moved.</para>
		/// <para>A payment also lifts the plot's missed-payment mark, inside the same transaction.
		/// Lifted by a write after the commit, as it used to be, a failure left the mark on a plot
		/// that had been paid for, and the next sweep reclaimed it by that mark alone.</para>
		/// <para>This is what makes the sweep safe to run on every scene server of a cluster at
		/// once: the database, not any server, decides who bills a period.</para>
		/// </remarks>
		/// <param name="advancePeriod">
		/// True to win the period inside this transaction (the ordinary offline case); false when
		/// the caller already advanced it and only the debit is outstanding.
		/// </param>
		private async Task<OfflineChargeOutcome> TryChargeOfflineOwnerAsync(
			IPlotService plotService, PlotData plot, DateTime dueUtc, DateTime nextDueUtc, long characterID, long amount, bool advancePeriod)
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

			// The period, first: whoever advances it owns it, whether or not the owner can pay.
			if (advancePeriod)
			{
				DatabaseResult<int> advanced = await plotService.TryAdvanceTaxAsync(plot.ID, dueUtc, nextDueUtc);
				if (!advanced.IsSuccess)
				{
					await unitOfWork.RollbackAsync();
					Log.Warning("HousingSystem", $"Offline tax for plot {plot.ID}: could not advance its due date: [{advanced.ErrorCode}] {advanced.ErrorMessage}");
					return OfflineChargeOutcome.Faulted;
				}
				if (advanced.Data != 1)
				{
					await unitOfWork.RollbackAsync();
					return OfflineChargeOutcome.PeriodAlreadyBilled;
				}
			}

			if (!canPay)
			{
				DatabaseResult unpaidCommit = await unitOfWork.CommitAsync();
				if (!unpaidCommit.IsSuccess)
				{
					Log.Warning("HousingSystem", $"Offline tax for plot {plot.ID}: could not commit the unpaid period: [{unpaidCommit.ErrorCode}] {unpaidCommit.ErrorMessage}");
					return OfflineChargeOutcome.Faulted;
				}
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
				Log.Warning("HousingSystem", $"Currency ledger: async worker was full; the record for CharID={characterID} ran on the unbounded fallback path.");
			}
		}
	}
}
