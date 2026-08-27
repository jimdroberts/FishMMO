using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
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
				return;
			}

			DateTime now = DateTime.UtcNow;

			DatabaseResult<List<PlotData>> due = await plotService.FetchTaxDueAsync(worldServerID, now, TaxBatchSize);
			if (!due.IsSuccess || due.Data == null)
			{
				Log.Error("HousingSystem", $"Tax sweep for world {worldServerID} failed: {due.ErrorMessage}");
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
				await plotService.TryAdvanceTaxAsync(plot.ID, dueUtc, dueUtc + period);
				return;
			}

			/* Winning the right to charge comes first, exactly as claiming a plot comes before
			 * paying for it. A caller that loses this race must not have taken any money, and one
			 * that wins holds a period nobody else can bill for. */
			DatabaseResult<int> advanced = await plotService.TryAdvanceTaxAsync(plot.ID, dueUtc, dueUtc + period);
			if (!advanced.IsSuccess || advanced.Data != 1)
			{
				// Another server billed this period.
				return;
			}

			if (await TryChargeTaxAsync(plot.OwnerCharacterID, taxPerPeriod))
			{
				// Paid, so the grace clock stops wherever it had got to.
				await plotService.ClearTaxDelinquencyAsync(plot.ID);

				RecordLandTax(plot.OwnerCharacterID, taxPerPeriod);
				MarkPlotChanged(plot.ID);
				return;
			}

			/* Unpaid. The mark is only written when there is not one already, so the grace period
			 * keeps running from the first miss rather than restarting every time they fail again.
			 * The owner keeps the house until that runs out, which is the point of having one. */
			await plotService.MarkTaxDelinquentAsync(plot.ID, dueUtc);

			Log.Debug("HousingSystem", $"CharID={plot.OwnerCharacterID} could not pay {taxPerPeriod} tax on plot {plot.ID}.");
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
			DatabaseResult<int> released = await plotService.ReleaseAsync(plot.ID, plot.OwnerCharacterID, plot.OwnerGuildID);
			if (!released.IsSuccess || released.Data != 1)
			{
				return;
			}

			Log.Debug("HousingSystem",
				$"Plot {plot.ID} reclaimed: unpaid since {delinquentSinceUtc:u}, past the grace period.");

			/* Structures go after the release, not before. Released-then-cleared leaves a moment
			 * where land is free but still has a house on it, which the next owner can see and
			 * report. Cleared-then-released leaves a moment where somebody still owns a plot whose
			 * house has silently vanished, which looks like the game destroying their property. */
			ClearStructures(plot.ID);
			MarkPlotChanged(plot.ID);
		}

		/// <summary>
		/// Takes tax from a character's persisted balance.
		/// </summary>
		/// <remarks>
		/// Written straight to the stored attribute rather than through
		/// <see cref="CharacterCurrency"/>, because tax falls due whether or not the owner is
		/// logged in — and an owner who is charged only while online is an owner who never pays.
		///
		/// <para>Refuses rather than going negative. A balance that can go below zero is a debt the
		/// rest of the economy has no concept of; the plot simply stays unpaid and the grace period
		/// decides what happens next.</para>
		/// </remarks>
		private async Task<bool> TryChargeTaxAsync(long characterID, long amount)
		{
			if (characterID <= 0 || amount <= 0 || currencyTemplate == null)
			{
				return false;
			}

			if (!TryGetDbService(out ICharacterAttributeService attributeService))
			{
				return false;
			}

			DatabaseResult<IReadOnlyList<CharacterAttributeData>> attributes = await attributeService.FetchAsync(characterID);
			if (!attributes.IsSuccess || attributes.Data == null)
			{
				return false;
			}

			int templateID = currencyTemplate.ID;
			foreach (CharacterAttributeData attribute in attributes.Data)
			{
				if (attribute.TemplateID != templateID)
				{
					continue;
				}

				if (attribute.Value < amount)
				{
					return false;
				}

				CharacterAttributeData updated = new CharacterAttributeData(
					attribute.ID,
					attribute.Version,
					attribute.CharacterID,
					attribute.TemplateID,
					attribute.Value - (int)amount,
					attribute.CurrentValue);

				DatabaseResult<BulkWriteResult> persisted = await attributeService.PersistAsync(new List<CharacterAttributeData> { updated });
				return persisted.IsSuccess;
			}

			return false;
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

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out ICurrencyEscrowService escrowService))
				{
					return;
				}

				DatabaseResult<long> hold = await escrowService.HoldAsync(characterID, amount, (int)CurrencyEscrowReason.LandTax);
				if (!hold.IsSuccess || hold.Data <= 0)
				{
					return;
				}

				await escrowService.AbsorbAsync(hold.Data);
			}, characterID))
			{
				Log.Warning("HousingSystem", $"Currency ledger: could not record land tax for CharID={characterID}.");
			}
		}
	}
}
