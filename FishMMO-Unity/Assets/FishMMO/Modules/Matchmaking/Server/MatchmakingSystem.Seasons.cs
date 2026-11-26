using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// PvP seasons for staff: what season it is, the ones before it, and moving on to the next (<c>/admin season</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A season is a database row (<c>arena_season</c>), numbered, and never deleted: ending one closes it and opens the
	/// next in one transaction, so the count and every past season's ratings survive restarts, deploys and crashes. Every
	/// scene server reads the active season from the database when it forms a ranked match or opens a board, so the
	/// change reaches all of them without a message.
	/// </para>
	/// <para>
	/// What a season resets is the ranked arena rating only: everyone starts the new one at the starting rating, with
	/// placement games again. The lifetime PvP Rank and the duel ratings carry on. A ranked match already under way
	/// finishes in the season it was formed in.
	/// </para>
	/// </remarks>
	public partial class MatchmakingSystem
	{
		/// <summary>Past seasons listed by <c>/admin season</c>.</summary>
		private const int StaffSeasonHistoryRows = 5;

		/// <summary>The active season (opening the first when there has never been one) and the ones before it.</summary>
		internal async Task<IReadOnlyList<string>> DescribeSeasonsForStaffAsync()
		{
			if (!TryGetDbService(out IArenaRatingService ratings))
			{
				return new[] { "The arena rating service is unavailable." };
			}
			DatabaseResult<ArenaSeasonData> active = await ratings.GetOrCreateActiveSeasonAsync();
			if (!active.IsSuccess)
			{
				return new[] { $"The active season could not be read: {active.ErrorCode} - {active.ErrorMessage}" };
			}
			var lines = new List<string>
			{
				$"Active: {DescribeSeason(active.Data)}. End it with /admin season next {active.Data.Number.ToString(CultureInfo.InvariantCulture)}.",
			};
			DatabaseResult<IReadOnlyList<ArenaSeasonData>> history = await ratings.FetchSeasonsAsync(StaffSeasonHistoryRows + 1);
			if (history.IsSuccess && history.Data != null)
			{
				int shown = 0;
				foreach (ArenaSeasonData season in history.Data)
				{
					if (season.ID == active.Data.ID || shown >= StaffSeasonHistoryRows)
					{
						continue;
					}
					lines.Add($"Before: {DescribeSeason(season)}.");
					++shown;
				}
			}
			return lines;
		}

		/// <summary>
		/// Ends season <paramref name="endingNumber"/> and opens the next, when it is still the active one. The number is
		/// the guard: a command sent twice, or by two administrators at once, moves the count on once.
		/// </summary>
		internal async Task<IReadOnlyList<string>> AdvanceSeasonForStaffAsync(int endingNumber, string name, string actorName, string actorAccount)
		{
			if (!TryGetDbService(out IArenaRatingService ratings))
			{
				return new[] { "The arena rating service is unavailable." };
			}
			DatabaseResult<ArenaSeasonData> active = await ratings.GetOrCreateActiveSeasonAsync();
			if (!active.IsSuccess)
			{
				return new[] { $"The active season could not be read: {active.ErrorCode} - {active.ErrorMessage}" };
			}
			if (active.Data.Number != endingNumber)
			{
				return new[] { $"Season {endingNumber.ToString(CultureInfo.InvariantCulture)} is not the active season; {DescribeSeason(active.Data)} is. Nothing changed." };
			}

			DatabaseResult<ArenaSeasonAdvanceData> moved = await ratings.StartNextSeasonAsync(active.Data.ID, name);
			if (!moved.IsSuccess)
			{
				return new[] { $"The season was not changed: {moved.ErrorCode} - {moved.ErrorMessage}" };
			}
			if (!moved.Data.Advanced)
			{
				return new[] { $"Someone moved the season on first; {DescribeSeason(moved.Data.Active)} is active. Nothing changed." };
			}

			ArenaSeasonData ended = moved.Data.Ended;
			ArenaSeasonData started = moved.Data.Active;
			await Log.Warning("MatchmakingSystem",
				$"Administrator '{actorName}' ({actorAccount}) ended PvP season {ended.Number} ('{ended.Name}', id {ended.ID}) and began season {started.Number} ('{started.Name}', id {started.ID}).");
			return new[]
			{
				$"{DescribeSeason(ended)} ended; {DescribeSeason(started)} began.",
				"Ranked arena ratings start over at 1500 with placement games; PvP Rank and duel ratings carry on.",
				$"Ranked matches already under way count toward season {ended.Number.ToString(CultureInfo.InvariantCulture)}. Boards show the new season within a minute.",
			};
		}

		/// <summary>"Season 3 'Winter' (since 2026-01-01 00:00 UTC)", with its end when it has one.</summary>
		private static string DescribeSeason(ArenaSeasonData season)
		{
			string number = season.Number.ToString(CultureInfo.InvariantCulture);
			string title = string.IsNullOrWhiteSpace(season.Name) || season.Name == "Season " + number
				? $"Season {number}"
				: $"Season {number} '{season.Name}'";
			string since = season.StartsUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
			return season.EndsUtc.HasValue && !season.Active
				? $"{title} ({since} to {season.EndsUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC)"
				: $"{title} (since {since} UTC)";
		}
	}
}
