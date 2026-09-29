using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A scene's weather as data: what changes and when, never the per-frame state. Owned by the
	/// server, mirrored on each client, and evaluated the same way on both.
	/// </summary>
	/// <remarks>
	/// Every change the server makes takes effect <see cref="LeadTicks"/> after it is sent, so a
	/// client has it before it matters and predicted weather effects match the server's.
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
		public WeatherCover Cover;
		public uint CoverTick;
		/// <summary>
		/// Counts the times this timeline was replaced whole — a join, a resync, a change of scene.
		/// Not the revision, which moves on every delta: the ground map used to start itself over
		/// whenever the revision changed, so every storm cell that spawned or retired, and every
		/// preset clicked, threw away the ground it had been drying and redrew it from one number.
		/// </summary>
		public uint Generation;
		/// <summary>Counts the cover snapshots the server has actually sent. Local integration does not move it.</summary>
		public uint CoverSnapshots;

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
		public double WorldSecondsAt(uint tick) =>
			WorldSecondsAtTick + (double)((long)tick - WorldSecondsTick) * TickDelta;

		public uint LeadTicks => (uint)Mathf.CeilToInt((float)(LeadSeconds / Math.Max(1e-6, TickDelta)));

		public uint SecondsToTicks(float seconds) => (uint)Math.Max(0, Math.Round(seconds / Math.Max(1e-6, TickDelta)));

		// ── Evaluation ────────────────────────────────────────────────

		/// <summary>What has been added to the scene's air at runtime, at a tick.</summary>
		public AirOffsets AirAt(uint tick) => Air.At(tick);

		/// <summary>Forgets dead cells. Both sides run it, so they stay equal without messages.</summary>
		public void Prune(uint tick)
		{
			Cells.RemoveAll(c => c.IsDead(tick));
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
				Cells = new List<StormCell>(Cells),
				Air = Air,
				Cover = Cover,
				CoverTick = CoverTick,
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
			Cover = msg.Cover;
			CoverTick = msg.CoverTick;
			Generation++;
			CoverSnapshots++;
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
				Air = msg.Air;
			}
			if (msg.HasCover)
			{
				Cover = msg.Cover;
				CoverTick = msg.CoverTick;
				CoverSnapshots++;
			}
			return true;
		}

		/// <summary>True when a delta's revision is past the next one, meaning something was missed.</summary>
		public bool IsGap(in WeatherDeltaBroadcast msg) => msg.Revision > Revision + 1;
	}
}
