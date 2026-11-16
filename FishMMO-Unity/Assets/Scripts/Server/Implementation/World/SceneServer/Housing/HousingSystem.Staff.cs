using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The staff side of housing: what the <c>/gm plot</c> and <c>/admin plot</c> commands call.
	/// </summary>
	/// <remarks>
	/// <para>Every change here goes through the same writes the game's own paths use — the release into
	/// the vault is the reclamation's transaction, the due date moves only with the pinned writes the
	/// rent sweep uses — so a staff action cannot leave a plot in a state the game could not have
	/// produced. Each one marks the plot changed, so every channel hears of it.</para>
	///
	/// <para>The commands are audited by the command sets themselves (one audit hook per set), so nothing
	/// here writes to the audit log.</para>
	/// </remarks>
	public partial class HousingSystem
	{
		/// <summary>How far from a deed post a staff command with no target still finds it, metres.</summary>
		private const float StaffReachMetres = 30f;

		/// <inheritdoc />
		public bool TryFindPlotForStaff(IPlayerCharacter actor, string target, out long plotID, out string error)
		{
			plotID = 0;
			error = null;
			if (!IsHousingEnabled)
			{
				error = "Housing is off on this server.";
				return false;
			}
			target = target?.Trim() ?? string.Empty;
			if (long.TryParse(target, out long id))
			{
				if (id <= 0)
				{
					error = "Plot IDs start at 1.";
					return false;
				}
				plotID = id;
				return true;
			}
			if (actor?.Transform == null)
			{
				error = "You are not in a scene.";
				return false;
			}

			IReadOnlyList<PlotFoundation> plots = PlotFoundation.Registry.ForScene(actor.Transform.gameObject.scene.handle);
			if (target.Length > 0)
			{
				string key = PlotIdentity.Normalize(target);
				foreach (PlotFoundation plot in plots)
				{
					if (plot != null && plot.PlotKey == key)
					{
						return Resolved(plot, out plotID, out error);
					}
				}
				error = $"No plot '{key}' in this scene.";
				return false;
			}

			Vector3 at = actor.Transform.position;
			PlotFoundation nearest = null;
			float best = StaffReachMetres * StaffReachMetres;
			foreach (PlotFoundation plot in plots)
			{
				if (plot == null)
				{
					continue;
				}
				if (plot.Contains(at))
				{
					return Resolved(plot, out plotID, out error);
				}
				float d = (plot.transform.position - at).sqrMagnitude;
				if (d < best)
				{
					best = d;
					nearest = plot;
				}
			}
			if (nearest != null)
			{
				return Resolved(nearest, out plotID, out error);
			}
			error = $"You are not on a plot, and no deed post is within {StaffReachMetres:0} m. Name one: a plot key or ID.";
			return false;
		}

		private static bool Resolved(PlotFoundation plot, out long plotID, out string error)
		{
			plotID = plot.PlotID;
			error = plotID > 0 ? null : $"Plot '{plot.PlotKey}' has not been read from the database yet.";
			return plotID > 0;
		}

		/// <inheritdoc />
		public async Task<IReadOnlyList<string>> DescribePlotForStaffAsync(long plotID)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				return new[] { "The plot service is unavailable." };
			}
			DatabaseResult<List<PlotData>> read = await plotService.FetchByIdsAsync(new[] { plotID });
			if (!read.IsSuccess || read.Data == null || read.Data.Count != 1)
			{
				return new[] { read.IsSuccess ? $"No plot {plotID}." : $"Could not read plot {plotID}: {read.ErrorMessage}" };
			}
			PlotData plot = read.Data[0];
			DateTime now = DateTime.UtcNow;
			var lines = new List<string>
			{
				$"Plot {plot.ID} '{plot.PlotKey}' in {plot.SceneName} (world {plot.WorldServerID}): {PlotStateExtensions.FromStored(plot.State)}.",
			};

			if (plot.OwnerCharacterID > 0)
			{
				string name = $"#{plot.OwnerCharacterID}";
				if (TryGetDbService(out ICharacterService characters))
				{
					DatabaseResult<IReadOnlyList<CharacterNameData>> names = await characters.FetchNamesAsync(new[] { plot.OwnerCharacterID });
					if (names.IsSuccess && names.Data != null && names.Data.Count > 0)
					{
						name = $"{names.Data[0].Name} (#{plot.OwnerCharacterID})";
					}
				}
				lines.Add($"Owner: {name}, since {(plot.TimeClaimed.HasValue ? plot.TimeClaimed.Value.ToString("u") : "?")}.");
			}
			else if (plot.OwnerGuildID > 0)
			{
				lines.Add($"Owner: guild #{plot.OwnerGuildID}.");
			}
			else
			{
				lines.Add("Owner: nobody.");
			}

			if (plot.TaxDueUtc.HasValue)
			{
				TimeSpan until = plot.TaxDueUtc.Value - now;
				lines.Add($"Rent due {plot.TaxDueUtc.Value:u} ({(until.TotalSeconds >= 0 ? "in " + Span(until) : Span(-until) + " ago")}).");
			}
			else
			{
				lines.Add("No rent date (rent off, or unowned).");
			}
			if (plot.TaxDelinquentSinceUtc.HasValue)
			{
				TimeSpan grace = TimeSpan.FromDays(Mathf.Max(0f, taxGraceDays)) - (now - plot.TaxDelinquentSinceUtc.Value);
				lines.Add($"Unpaid since {plot.TaxDelinquentSinceUtc.Value:u}; {(grace.TotalSeconds > 0 ? "taken back in " + Span(grace) : "past its grace, taken back on the next sweep")}.");
			}

			if (TryGetDbService(out IPlotAccessService access))
			{
				DatabaseResult<List<PlotAccessData>> grants = await access.FetchByPlotAsync(plotID);
				lines.Add(grants.IsSuccess && grants.Data != null ? $"Guests: {grants.Data.Count}." : "Guests: could not be read.");
			}
			if (TryGetDbService(out IPlotStructureService structures))
			{
				DatabaseResult<List<PlotStructureData>> standing = await structures.FetchByPlotAsync(plotID);
				lines.Add(standing.IsSuccess && standing.Data != null ? $"Structures: {standing.Data.Count}." : "Structures: could not be read.");
			}
			return lines;
		}

		/// <inheritdoc />
		public async Task<string> ReleasePlotForStaffAsync(long plotID, bool keepContents)
		{
			if (!TryGetDbService(out IPlotService plotService) || !TryGetDbService(out IPlotStructureService structureService))
			{
				return "A housing service is unavailable.";
			}
			DatabaseResult<List<PlotData>> read = await plotService.FetchByIdsAsync(new[] { plotID });
			if (!read.IsSuccess || read.Data == null || read.Data.Count != 1)
			{
				return read.IsSuccess ? $"No plot {plotID}." : $"Could not read plot {plotID}: {read.ErrorMessage}";
			}
			PlotData plot = read.Data[0];
			if (plot.OwnerCharacterID <= 0 && plot.OwnerGuildID <= 0)
			{
				return $"Plot {plotID} is not owned; nothing to release.";
			}

			PlotState state;
			if (keepContents)
			{
				DatabaseResult<List<PlotStructureData>> standing = await structureService.FetchByPlotAsync(plotID);
				if (!standing.IsSuccess)
				{
					return $"Could not read what stands on plot {plotID}: {standing.ErrorMessage}";
				}
				state = standing.Data != null && standing.Data.Count > 0 ? PlotState.Abandoned : PlotState.Empty;
				if (!await TryReleaseIntoVaultAsync(plotService, plot, state))
				{
					return $"Plot {plotID} could not be released (it changed hands, or the database refused); see the server log.";
				}
			}
			else
			{
				state = PlotState.Empty;
				string failed = await ResetPlotAsync(plotService, structureService, plot);
				if (failed != null)
				{
					return failed;
				}
			}

			MarkPlotChanged(plotID);
			if (!TryEnqueueHousingMainThread(() => ApplyReleasedLocally(plotID, state)))
			{
				Log.Warning("HousingSystem", $"Could not apply the staff release of plot {plotID} locally; the plot sync applies it.");
			}
			Log.Info("HousingSystem", $"Staff {(keepContents ? "released" : "reset")} plot {plotID} (owner #{plot.OwnerCharacterID}/guild #{plot.OwnerGuildID}); now {state}.");
			return keepContents
				? $"Plot {plotID} released: it is {state}, and whatever stood on it is in the former owner's vault."
				: $"Plot {plotID} reset: it is an empty lot, everything on it demolished and its guest list cleared.";
		}

		/// <summary>
		/// Releases a plot, demolishes what stands on it and clears its guests, in one transaction.
		/// </summary>
		/// <returns>Null on success, else what went wrong.</returns>
		private async Task<string> ResetPlotAsync(IPlotService plotService, IPlotStructureService structureService, PlotData plot)
		{
			if (!TryGetDbService(out IUnitOfWorkService unitOfWorkService) || !TryGetDbService(out IPlotAccessService accessService))
			{
				return "A housing service is unavailable.";
			}
			DatabaseResult<IUnitOfWork> begin = await unitOfWorkService.BeginAsync();
			if (!begin.IsSuccess || begin.Data == null)
			{
				return $"Could not begin a transaction: {begin.ErrorMessage}";
			}
			await using IUnitOfWork unitOfWork = begin.Data;

			DatabaseResult<int> released = await plotService.ReleaseAsync(plot.ID, plot.OwnerCharacterID, plot.OwnerGuildID, (int)PlotState.Empty);
			if (!released.IsSuccess || released.Data != 1)
			{
				await unitOfWork.RollbackAsync();
				return released.IsSuccess ? $"Plot {plot.ID} changed hands since it was read; nothing was done." : $"Could not release plot {plot.ID}: {released.ErrorMessage}";
			}
			DatabaseResult<int> demolished = await structureService.DemolishAllAsync(plot.ID);
			DatabaseResult<int> revoked = demolished.IsSuccess ? await accessService.RevokeAllAsync(plot.ID) : default;
			if (!demolished.IsSuccess || !revoked.IsSuccess)
			{
				await unitOfWork.RollbackAsync();
				return $"Could not clear plot {plot.ID}; nothing was done.";
			}
			DatabaseResult commit = await unitOfWork.CommitAsync();
			return commit.IsSuccess ? null : $"Could not commit the reset of plot {plot.ID}: {commit.ErrorMessage}";
		}

		/// <summary>
		/// This server's copies of a plot that lost its owner: no owner, the new state, no guests, no
		/// contents, no build session. Main thread.
		/// </summary>
		private void ApplyReleasedLocally(long plotID, PlotState state)
		{
			if (buildSessions.TryGetValue(plotID, out BuildSession session))
			{
				CloseSession(plotID, session);
			}
			ForgetPlotContents(plotID);
			foreach (PlotFoundation copy in PlotFoundation.Registry.ForPlot(plotID))
			{
				copy.ApplyOwner(PlotOwner.None);
				copy.ApplyState(state);
				copy.ApplyAccessGrants(new Dictionary<long, PlotPermission>());
			}
		}

		/// <inheritdoc />
		public async Task<string> SetRentDueForStaffAsync(long plotID, string when)
		{
			if (!TryGetDbService(out IPlotService plotService))
			{
				return "The plot service is unavailable.";
			}
			DatabaseResult<List<PlotData>> read = await plotService.FetchByIdsAsync(new[] { plotID });
			if (!read.IsSuccess || read.Data == null || read.Data.Count != 1)
			{
				return read.IsSuccess ? $"No plot {plotID}." : $"Could not read plot {plotID}: {read.ErrorMessage}";
			}
			PlotData plot = read.Data[0];
			if (!plot.TaxDueUtc.HasValue)
			{
				return $"Plot {plotID} has no rent date (rent is off, or nobody owns it).";
			}

			DateTime due = plot.TaxDueUtc.Value;
			DateTime target;
			when = when?.Trim() ?? string.Empty;
			if (when.Equals("now", StringComparison.OrdinalIgnoreCase))
			{
				target = DateTime.UtcNow;
			}
			else if (when.Length > 1 && (when[0] == '+' || when[0] == '-') && WorldTimeText.TryReadDuration(when.Substring(1), out double seconds))
			{
				target = due.AddSeconds(when[0] == '-' ? -seconds : seconds);
			}
			else
			{
				return "Usage: /admin plot setdue <plot> now | +1h | -2d (a change to the current due date).";
			}

			DatabaseResult<int> moved = target > due
				? await plotService.TryAdvanceTaxAsync(plotID, due, target)
				: target < due
					? await plotService.TryRestoreTaxDueAsync(plotID, due, target)
					: DatabaseResult<int>.Success(1);
			if (!moved.IsSuccess)
			{
				return $"Could not move plot {plotID}'s due date: {moved.ErrorMessage}";
			}
			if (moved.Data != 1)
			{
				return $"Plot {plotID} was billed a moment ago; its due date moved under you. Try again.";
			}
			return $"Plot {plotID}'s rent is now due {target:u} (was {due:u}). /admin plot sweep bills it now.";
		}

		/// <inheritdoc />
		public Func<Task<IReadOnlyList<string>>> PrepareOrphanListForStaff(IPlayerCharacter actor)
		{
			if (actor?.Transform == null || !TryResolveWorld(actor.Transform.gameObject.scene.handle, out long worldServerID, out string sceneName))
			{
				return () => Task.FromResult<IReadOnlyList<string>>(new[] { "This scene has not been matched to its world yet." });
			}
			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (PlotFoundation plot in PlotFoundation.Registry.ForScene(actor.Transform.gameObject.scene.handle))
			{
				if (plot != null && !string.IsNullOrEmpty(plot.PlotKey))
				{
					keys.Add(plot.PlotKey);
				}
			}

			return async () =>
			{
				if (!TryGetDbService(out IPlotService plotService))
				{
					return new[] { "The plot service is unavailable." };
				}
				DatabaseResult<List<PlotData>> rows = await plotService.FetchBySceneAsync(worldServerID, sceneName);
				if (!rows.IsSuccess || rows.Data == null)
				{
					return new[] { $"Could not read {sceneName}'s plots: {rows.ErrorMessage}" };
				}
				var lines = new List<string>();
				foreach (PlotData row in rows.Data)
				{
					bool owned = row.OwnerCharacterID > 0 || row.OwnerGuildID > 0;
					if (!keys.Contains(row.PlotKey) && owned)
					{
						lines.Add($"Plot {row.ID} '{row.PlotKey}': owned by {(row.OwnerCharacterID > 0 ? "#" + row.OwnerCharacterID : "guild #" + row.OwnerGuildID)}, {PlotStateExtensions.FromStored(row.State)} — no foundation carries this key. /admin plot retire {row.ID}");
					}
				}
				if (lines.Count == 0)
				{
					lines.Add($"Every owned plot in {sceneName} has its foundation ({rows.Data.Count} row(s), {keys.Count} foundation(s)).");
				}
				return lines;
			};
		}

		/// <inheritdoc />
		public void SweepRentNow()
		{
			taxSweepCountdown = 0f;
		}

		private static string Span(TimeSpan span)
		{
			if (span.TotalDays >= 1.0)
			{
				return $"{(int)span.TotalDays}d {span.Hours}h";
			}
			if (span.TotalHours >= 1.0)
			{
				return $"{(int)span.TotalHours}h {span.Minutes}m";
			}
			return span.TotalMinutes >= 1.0 ? $"{(int)span.TotalMinutes}m" : $"{Math.Max(0, (int)span.TotalSeconds)}s";
		}
	}
}
