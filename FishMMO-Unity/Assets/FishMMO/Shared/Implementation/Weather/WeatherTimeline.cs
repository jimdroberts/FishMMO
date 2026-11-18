using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A scene's weather as data: what changes and when, never the per-frame state. Owned by the
	/// server, mirrored on each client, and evaluated the same way on both.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every change the server makes takes effect <see cref="LeadSeconds"/> of real time after it is
	/// sent (<see cref="LeadWorldSeconds"/>), so a client has it before it matters and predicted
	/// weather effects match the server's.
	/// </para>
	/// <para>
	/// <b>Kept in world time.</b> Its storms, the air's moves and the cover's stamp are world seconds,
	/// not server ticks: the weather holds when an admin holds the world and races when it is raced,
	/// with the sky over it. Callers holding a tick go through <see cref="WorldSecondsAt(uint)"/>,
	/// which reads the world clock (pace and all) — the one place a tick becomes a time.
	/// </para>
	/// </remarks>
	public sealed class WeatherTimeline
	{
		/// <summary>How far ahead of "now" an edit is scheduled: 1.5 s at 30 Hz.</summary>
		public const float LeadSeconds = 1.5f;

		public string SceneName = string.Empty;
		public uint Revision;
		public uint Seed;
		public WeatherSceneMode SceneMode = WeatherSceneMode.Own;
		public double TickDelta = 1.0 / 30.0;
		public readonly List<StormCell> Cells = new List<StormCell>();
		/// <summary>
		/// What has been added to this scene's air at runtime — by an admin, a script, the test bed —
		/// moving from one value to another. On top of what the scene is authored with, never instead.
		/// </summary>
		public AirOffsetEntry Air;
		/// <summary>
		/// The entries <see cref="Air"/> replaced, oldest first, as far back as anything reads the air
		/// (<see cref="AirHistorySeconds"/>): what <see cref="AirAtSeconds"/> answers with before <see cref="Air"/> began.
		/// </summary>
		/// <remarks>
		/// An entry alone knows only the value it moved from, so without these every change rewrote all of the past to
		/// that value: putting the air back after a cold spell read the whole past as the cold spell, and the ground's
		/// six hours of memory snowed over again the moment the cold was taken away. Both sides append to it by the same
		/// rule (<see cref="ReplaceAir"/>) and a full timeline carries it, so a player who joins reads the same past.
		/// </remarks>
		public readonly List<AirOffsetEntry> AirHistory = new List<AirOffsetEntry>();
		/// <summary>
		/// How far back the air is ever read: the ground's memory (<see cref="GroundCover.MemorySeconds"/>) and a
		/// generation of margin for the forcing it is rebuilt from.
		/// </summary>
		public const double AirHistorySeconds = GroundCover.MemorySeconds + GroundCover.GenerationSeconds;
		/// <summary>The most replaced entries kept, whatever their age: a burst of edits cannot grow the timeline without end.</summary>
		public const int AirHistoryLimit = 32;
		/// <summary>
		/// The scene's snow, wetness, ash and sand now, averaged over its cover points: worked out by each side from
		/// the world time (<see cref="SceneCover"/>), never sent.
		/// </summary>
		public WeatherCover Cover;
		/// <summary>
		/// Counts the times this timeline was replaced whole — a join, a resync, a change of scene.
		/// Not the revision, which moves on every delta.
		/// </summary>
		public uint Generation;
		/// <summary>Whether the scene makes storms of its own (<see cref="StormSchedule"/>): the director's switch, sent so every side agrees.</summary>
		public bool Director;
		/// <summary>The scene's world X/Z rectangle, as the server measured it: where its own storms are born and its cover is read.</summary>
		public Rect Area;

		/// <summary>
		/// Where and when this scene is, for the weather driver.
		/// </summary>
		/// <remarks>
		/// The driver is a pure function of the world clock and the place, so it needs both — and
		/// both have to be the same number on the server and on every client or they compute
		/// different weather. They live here because the timeline is the one piece of weather state
		/// both sides already hold and keep in step.
		/// </remarks>
		/// <summary>
		/// Whether the drifting weather field runs in this scene.
		/// </summary>
		/// <remarks>
		/// On for a living world, which is every scene with weather. Off only where a caller wants the
		/// air with no drifting field under it at all — a probe measuring what one air does, which
		/// would otherwise be measuring the weather of whatever moment it ran at.
		/// </remarks>
		public bool Driver = true;

		public double WorldSecondsAtTick;
		/// <summary>The tick <see cref="WorldSecondsAtTick"/> was taken at.</summary>
		public uint WorldSecondsTick;
		/// <summary>The scene's latitude: it decides which way the weather comes from, and how hard.</summary>
		public float LatitudeDegrees;
		/// <summary>The scene's longitude, for working out its local hour from the world clock.</summary>
		public float LongitudeDegrees;

		/// <summary>
		/// The body the weather is worked out for, when it is not the scene's own. Null in the game,
		/// where a scene stands on the body the world atlas says it does.
		/// </summary>
		/// <remarks>
		/// For the test bed, which stands one scene on any body in the system. The sky followed the
		/// choice and the weather did not, so on a moon the sun kept the moon's season while the
		/// weather kept the home world's. Local only: it is never sent, and a server never sets it.
		/// </remarks>
		[System.NonSerialized] public FishMMO.Shared.Celestial.WorldBody BodyOverride;

		/// <summary>World time at a tick, carried forward from the last anchor the server sent.</summary>
		public double WorldSecondsAt(uint tick) => WorldSecondsAt((double)tick);

		/// <summary>World time at a fractional tick (a presenter drawing between ticks).</summary>
		public double WorldSecondsAt(double tick) =>
			// The world clock when it has an anchor: it carries the pace an admin set (held, raced), which
			// this timeline's own anchor does not, and the weather must keep the sky's time. The timeline's
			// anchor for a timeline read on its own (tests, tools).
			WorldClock.Shared.HasAnchor
				? WorldClock.Shared.WorldSecondsAt(tick)
				: WorldSecondsAtTick + (tick - WorldSecondsTick) * TickDelta;

		/// <summary>
		/// How far ahead of now, in world seconds, an edit is scheduled: <see cref="LeadSeconds"/> of
		/// real time at the world's pace now (none while the world is held, when every client reads the
		/// same moment however late the edit arrives).
		/// </summary>
		public double LeadWorldSeconds => LeadSeconds * (WorldClock.Shared.HasAnchor ? WorldClock.Shared.Rate : 1.0);

		// ── Evaluation ────────────────────────────────────────────────

		/// <summary>What has been added to the scene's air at runtime, at a tick.</summary>
		public AirOffsets AirAt(uint tick) => AirAtSeconds(WorldSecondsAt(tick));

		/// <summary>
		/// What had been added to the scene's air at runtime at a moment of world time: <see cref="Air"/> from its start,
		/// and before that the entry that was in force then (<see cref="AirHistory"/>).
		/// </summary>
		public AirOffsets AirAtSeconds(double worldSeconds)
		{
			if (worldSeconds >= Air.StartSeconds || AirHistory.Count == 0)
			{
				return Air.AtSeconds(worldSeconds);
			}
			for (int i = AirHistory.Count - 1; i >= 0; i--)
			{
				if (worldSeconds >= AirHistory[i].StartSeconds)
				{
					return AirHistory[i].AtSeconds(worldSeconds);
				}
			}
			return AirHistory[0].From;
		}

		/// <summary>
		/// Moves every runtime entry, the one in force and those it replaced, by <paramref name="delta"/>: what moving part
		/// of the runtime air into the scene's authored air takes away here, so the sum of the two is what it was at every
		/// moment, past and future.
		/// </summary>
		public void ShiftAir(in AirOffsets delta)
		{
			Air.From += delta;
			Air.To += delta;
			for (int i = 0; i < AirHistory.Count; i++)
			{
				AirOffsetEntry entry = AirHistory[i];
				entry.From += delta;
				entry.To += delta;
				AirHistory[i] = entry;
			}
		}

		/// <summary>
		/// Puts a new runtime entry in force, keeping the one it replaces in <see cref="AirHistory"/>. Every setter of the
		/// scene's air goes through this, on the server and on every client applying the delta, so the history is the same
		/// everywhere: pruned against the new entry's start, never against a local clock.
		/// </summary>
		public void ReplaceAir(in AirOffsetEntry next)
		{
			AirHistory.Add(Air);
			double oldest = next.StartSeconds - AirHistorySeconds;
			// An entry still matters while any of it, or the value it settled to, lies inside the window; the one before
			// the first that does is the value the window opens on, so it is kept too.
			int drop = 0;
			while (drop + 1 < AirHistory.Count && AirHistory[drop + 1].StartSeconds <= oldest)
			{
				drop++;
			}
			drop = System.Math.Max(drop, AirHistory.Count - AirHistoryLimit);
			if (drop > 0)
			{
				AirHistory.RemoveRange(0, drop);
			}
			Air = next;
		}

		/// <summary>Where a cell is at a (fractional) tick.</summary>
		public Vector2 CentreOf(in StormCell cell, double tick) => cell.CentreAtSeconds(WorldSecondsAt(tick));

		/// <summary>How strong a cell is at a tick, 0..1.</summary>
		public float EnvelopeOf(in StormCell cell, double tick) => cell.EnvelopeAtSeconds(WorldSecondsAt(tick));

		/// <summary>
		/// Forgets dead cells. Both sides run it, so they stay equal without messages. A storm someone started is kept
		/// for the ground's memory after it dies (<see cref="GroundCover.MemorySeconds"/>), and goes to a player who joins
		/// in that time, so the ground it wetted is worked out the same by everyone; the world's own storms are worked
		/// out again whenever they are wanted (<see cref="StormSchedule"/>).
		/// </summary>
		public void Prune(uint tick)
		{
			double now = WorldSecondsAt(tick);
			Cells.RemoveAll(c => StormSchedule.IsScheduled(c.ID) ? c.IsDeadAtSeconds(now) : c.IsDeadAtSeconds(now - GroundCover.MemorySeconds));
		}

		public bool TryGetCell(ushort id, out int index)
		{
			for (index = 0; index < Cells.Count; index++)
			{
				if (Cells[index].ID == id)
				{
					return true;
				}
			}
			index = -1;
			return false;
		}

		public void UpsertCell(StormCell cell)
		{
			if (TryGetCell(cell.ID, out int i)) Cells[i] = cell; else Cells.Add(cell);
		}

		// ── Wire ──────────────────────────────────────────────────────

		public WeatherTimelineBroadcast ToBroadcast()
		{
			return new WeatherTimelineBroadcast
			{
				SceneName = SceneName,
				Revision = Revision,
				Seed = Seed,
				SceneMode = (byte)SceneMode,
				// Only the cells that were started by hand: the schedule's are worked out by the client itself.
				Cells = Cells.FindAll(c => !StormSchedule.IsScheduled(c.ID)),
				Air = Air,
				AirHistory = new List<AirOffsetEntry>(AirHistory),
				Director = Director,
				AreaX = Area.x,
				AreaZ = Area.y,
				AreaWidth = Area.width,
				AreaDepth = Area.height,
				// Where and when, so the client's driver computes the server's weather rather than
				// the weather of world-time zero on the equator.
				Driver = Driver,
				WorldSecondsAtTick = WorldSecondsAtTick,
				WorldSecondsTick = WorldSecondsTick,
				LatitudeDegrees = LatitudeDegrees,
				LongitudeDegrees = LongitudeDegrees,
			};
		}

		/// <summary>Replaces this timeline with a full one from the server.</summary>
		public void Apply(in WeatherTimelineBroadcast msg)
		{
			SceneName = msg.SceneName ?? string.Empty;
			Revision = msg.Revision;
			Seed = msg.Seed;
			SceneMode = (WeatherSceneMode)msg.SceneMode;
			Cells.Clear();
			if (msg.Cells != null) Cells.AddRange(msg.Cells);
			Air = msg.Air;
			AirHistory.Clear();
			if (msg.AirHistory != null) AirHistory.AddRange(msg.AirHistory);
			Generation++;
			Director = msg.Director;
			Area = new Rect(msg.AreaX, msg.AreaZ, msg.AreaWidth, msg.AreaDepth);
			Driver = msg.Driver;
			WorldSecondsAtTick = msg.WorldSecondsAtTick;
			WorldSecondsTick = msg.WorldSecondsTick;
			LatitudeDegrees = msg.LatitudeDegrees;
			LongitudeDegrees = msg.LongitudeDegrees;
		}

		/// <summary>
		/// Applies a delta. An old or duplicate delta is ignored (true). A delta that skips a revision
		/// changes nothing and returns false: the caller must then ask for the whole timeline.
		/// </summary>
		public bool TryApply(in WeatherDeltaBroadcast msg)
		{
			if (msg.Revision != Revision + 1)
			{
				return msg.Revision <= Revision;   // an old or duplicate delta is harmless; a gap is not
			}
			Revision = msg.Revision;
			if (msg.RemovedCells != null)
			{
				foreach (ushort id in msg.RemovedCells)
				{
					if (TryGetCell(id, out int i)) Cells.RemoveAt(i);
				}
			}
			if (msg.Cells != null)
			{
				foreach (StormCell cell in msg.Cells) UpsertCell(cell);
			}
			if (msg.HasAir)
			{
				ReplaceAir(msg.Air);
			}
			if (msg.HasDirector)
			{
				Director = msg.Director;
			}
			return true;
		}

		/// <summary>True when a delta's revision is past the next one, meaning something was missed.</summary>
		public bool IsGap(in WeatherDeltaBroadcast msg) => msg.Revision > Revision + 1;
	}
}
