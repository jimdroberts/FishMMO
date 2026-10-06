using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin time</c>: the world clock — shown, set to the millisecond, held, resumed, raced.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One clock for the whole world: every change is a write to the <c>world_clock_control</c> row,
	/// which every scene server reads every couple of seconds and adopts at once (WorldClockHost), so
	/// the acknowledgement says what was WRITTEN, as server control's do. FishNet's tick keeps its
	/// pace throughout; only the anchor that says what time it is moves.
	/// </para>
	/// <para>
	/// What a hold stops is the world's environment — the sun, the sky, the weather, the seasons, the
	/// sea, the wind — not the game: players, creatures and combat go on. The same text sets the clock
	/// here, in the Control Panel and in the editor's World Sim bed (WorldTimeText).
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		private static readonly string[] TimeActionHelp =
		{
			"time — the world time to the millisecond, its pace, the date and the hour where you stand",
			"time set <yyyy-MM-dd HH:mm:ss.fff> — sets the world time (to the millisecond), keeping its pace",
			"time set <+1h30m | -90s | +2d | +250ms> — moves it on or back by that much world time",
			"time hold — holds the world still (sun, weather, sea); the game itself goes on",
			"time resume — runs it again at the pace it was held from",
			"time pace <rate> — world seconds per real second: 1 real time, 60 a minute a second, 0 holds",
			"Every change reaches every scene server within a few seconds.",
		};

		/// <summary>The clock rows of the <c>/admin</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildAdminTimeCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "time", Aliases = new[] { "clock" }, Category = "World",
					Summary = "Shows the world clock, or sets it to the millisecond, holds, resumes or races it. /admin time help lists the actions.",
					Arguments = "action:Choice=show,set,hold,resume,pace,help?;value:Text?",
					Destructive = true,
					Run = RunAdminTime,
				},
			};
		}

		private void RunAdminTime(IPlayerCharacter character, string arguments)
		{
			string action = OperatorCommandParsing.SplitFirstWord(arguments, out string rest).ToLowerInvariant();
			rest = rest?.Trim() ?? string.Empty;
			switch (action)
			{
				case "":
				case "show":
					ReportClock(character);
					return;
				case "help":
					ReplyLines(character, TimeActionHelp);
					return;
				case "set":
					SetWorldTime(character, rest);
					return;
				case "hold":
				case "pause":
					WriteWorldTime(character, "hold", svc => svc.HoldAsync(character.CharacterName));
					return;
				case "resume":
				case "run":
					WriteWorldTime(character, "resume", svc => svc.ResumeAsync(character.CharacterName));
					return;
				case "pace":
				case "rate":
				case "speed":
					SetWorldPace(character, rest);
					return;
				default:
					Reply(character, $"Unknown time action '{action}'. /admin time help lists them.");
					return;
			}
		}

		private static long WorldEpoch()
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			return system != null ? system.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
		}

		/// <summary>The world time on this server now, in world seconds (for a change typed as +/−).</summary>
		private double WorldSecondsNow()
		{
			var tm = Server.NetworkWrapper.NetworkManager.TimeManager;
			return WorldClock.Shared.WorldSecondsAt(tm.Tick + tm.GetTickPercentAsDouble());
		}

		private void SetWorldTime(IPlayerCharacter character, string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				Reply(character, "Usage: /admin time set <yyyy-MM-dd HH:mm:ss.fff> or /admin time set <+1h | -90s | +2d>.");
				return;
			}
			string actor = character.CharacterName;
			if (value[0] == '+' || value[0] == '-')
			{
				// A change is applied to the row's own time in the database, so it is exact whatever
				// this server's clock reads: two admins stepping at once add both steps.
				if (!WorldTimeText.TryReadDuration(value.Substring(1), out double seconds))
				{
					Reply(character, $"'{value}' is not a change of time (e.g. +1h30m, -90s, +2d, +250ms).");
					return;
				}
				long deltaMs = (long)Math.Round((value[0] == '-' ? -seconds : seconds) * 1000.0);
				WriteWorldTime(character, "shift", svc => svc.ShiftAsync(deltaMs, actor));
				return;
			}
			if (!WorldTimeText.TryRead(value, WorldEpoch(), WorldSecondsNow(), out double worldSeconds, out string error))
			{
				Reply(character, error + ".");
				return;
			}
			if (worldSeconds < 0.0)
			{
				Reply(character, "That is before the calendar's epoch; the world clock starts there.");
				return;
			}
			long worldMs = (long)Math.Round(worldSeconds * 1000.0);
			WriteWorldTime(character, "set", svc => svc.SetAsync(worldMs, actor));
		}

		private void SetWorldPace(IPlayerCharacter character, string value)
		{
			string text = (value ?? string.Empty).Trim().TrimEnd('x', 'X', '×');
			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || double.IsNaN(rate) || double.IsInfinity(rate))
			{
				Reply(character, "Usage: /admin time pace <rate> — 1 is real time, 60 a minute a second, 0.25 a quarter, 0 holds.");
				return;
			}
			if (rate < 0.0)
			{
				Reply(character, "The world clock cannot run backwards. Use 0 (or /admin time hold) to hold it.");
				return;
			}
			if (rate > WorldClockLimits.MaxRate)
			{
				Reply(character, $"That is faster than the clock allows ({WorldClockLimits.MaxRate.ToString(CultureInfo.InvariantCulture)}×).");
				return;
			}
			string actor = character.CharacterName;
			WriteWorldTime(character, "pace", svc => svc.SetRateAsync(rate, actor));
		}

		/// <summary>
		/// Writes the world clock control row, and answers with what was written and what it replaced.
		/// The gate has already audited the command; the write itself is logged as a warning, as server
		/// control is, because it changes the world for everyone.
		/// </summary>
		private void WriteWorldTime(IPlayerCharacter character, string what, Func<IWorldClockControlService, Task<DatabaseResult<WorldClockReading>>> write)
		{
			string adminName = character.CharacterName;
			long epoch = WorldEpoch();
			RunOperatorAction(character, async () =>
			{
				if (!TryGetDbService(out IWorldClockControlService service))
				{
					return "The world clock service is unavailable.";
				}
				DatabaseResult<WorldClockReading> result = await write(service);
				if (!result.IsSuccess || result.Data == null)
				{
					return $"The world clock was not changed: {result.ErrorCode} - {result.ErrorMessage}";
				}
				WorldClockReading reading = result.Data;
				long readingEpoch = reading.EpochUnixSeconds > 0 ? reading.EpochUnixSeconds : epoch;
				string now = WorldTimeText.Write(reading.WorldMsNow / 1000.0, readingEpoch);
				string pace = reading.Rate <= 0.0 ? $"held (resumes at {WorldTimeText.Pace(reading.ResumeRate)})" : WorldTimeText.Pace(reading.Rate);
				string was = reading.ReplacedWorldMs.HasValue
					? $" It was {WorldTimeText.Write(reading.ReplacedWorldMs.Value / 1000.0, readingEpoch)}" + (reading.ReplacedRate.HasValue ? $", {WorldTimeText.Pace(reading.ReplacedRate.Value)}." : ".")
					: string.Empty;
				await Log.Warning("SceneServerSystem", $"Administrator '{adminName}' wrote the world clock ({what}): {now}, {pace}, revision {reading.Revision}.");
				// This server reads it at once rather than at its next poll.
				worldClockHost?.CheckNow();
				return $"World time written: {now}, {pace} (revision {reading.Revision}).{was} Every scene server adopts it within a few seconds.";
			});
		}
	}
}
