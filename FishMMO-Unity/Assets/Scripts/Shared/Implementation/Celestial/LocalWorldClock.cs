using System;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The world's tick when there is no server: a stand-in for FishNet's synchronised tick that the
	/// World Sim bed (or any offline tool) runs, and drives the shared <see cref="WorldClock"/> exactly as
	/// a scene server does — so everything in the editor reads time down the same path as the game.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The tick never changes pace.</b> It counts at <see cref="TickRate"/> a real second, paused or
	/// not, as FishNet's does. Setting the time, holding it or racing it publishes a new anchor at the
	/// current tick (<see cref="WorldClock.Override"/>) — the same thing an admin's command does on a live
	/// server — so the editor exercises the game's own behaviour, not a copy of it.
	/// </para>
	/// <para>
	/// While one is <see cref="Active"/>, <see cref="WorldTime.CurrentHours"/> reads it in place of a
	/// TimeManager, and <see cref="WorldMotion"/> follows its pace.
	/// </para>
	/// </remarks>
	public sealed class LocalWorldClock
	{
		/// <summary>The clock in use offline, or null (a live game, where FishNet's tick is used).</summary>
		public static LocalWorldClock Active { get; private set; }

		/// <summary>Ticks a real second. Fixed, as the server's is.</summary>
		public double TickRate { get; }
		public double TickDelta => 1.0 / TickRate;

		/// <summary>The current (fractional) tick, counted from when the clock was made.</summary>
		public double Tick { get; private set; }

		/// <summary>The pace the world runs at before it was held, for <see cref="Resume"/>.</summary>
		private double resumeRate = 1.0;

		public LocalWorldClock(double tickRate = 30.0, double startWorldSeconds = 0.0)
		{
			TickRate = Math.Max(1.0, tickRate);
			// Started a little way in, so the tick is never 0: a storm's "0" means "no tick" in places.
			Tick = TickRate;
			WorldClock clock = WorldClock.Shared;
			clock.Reset();
			clock.TickDelta = TickDelta;
			clock.Override((uint)Tick, startWorldSeconds, 1.0, verified: true);
		}

		/// <summary>Makes this the clock offline time reads; null stops offline time (and forgets the anchor).</summary>
		public static void Activate(LocalWorldClock clock)
		{
			Active = clock;
			if (clock == null)
			{
				WorldClock.Shared.Reset();
			}
		}

		/// <summary>Counts the tick on by a stretch of real time. Call once a frame.</summary>
		public void Advance(double realSeconds)
		{
			Tick += Math.Max(0.0, realSeconds) * TickRate;
		}

		/// <summary>World seconds since the epoch now.</summary>
		public double WorldSeconds => WorldClock.Shared.WorldSecondsAt(Tick);

		/// <summary>World seconds per real second now: 0 held, 1 the world's own pace.</summary>
		public double Rate => WorldClock.Shared.Rate;

		public bool Held => Rate <= 0.0;

		/// <summary>Sets the world time outright, to the millisecond, keeping the pace.</summary>
		public void SetWorldSeconds(double worldSeconds)
		{
			Anchor(worldSeconds, Rate);
		}

		/// <summary>Sets the pace (0 holds), from the moment the clock reads now.</summary>
		public void SetRate(double rate)
		{
			rate = Math.Max(0.0, rate);
			if (rate > 0.0)
			{
				resumeRate = rate;
			}
			Anchor(WorldSeconds, rate);
		}

		/// <summary>Holds the world where it is; the tick goes on counting.</summary>
		public void Hold()
		{
			if (!Held)
			{
				resumeRate = Rate;
			}
			Anchor(WorldSeconds, 0.0);
		}

		/// <summary>Runs the world again at the pace it was held from.</summary>
		public void Resume()
		{
			Anchor(WorldSeconds, resumeRate > 0.0 ? resumeRate : 1.0);
		}

		/// <summary>Moves the world on (or back) by world seconds, held or running.</summary>
		public void Step(double worldSeconds)
		{
			Anchor(WorldSeconds + worldSeconds, Rate);
		}

		/// <summary>
		/// The anchor at the whole tick, so that the clock reads exactly <paramref name="worldSeconds"/> at
		/// the current fractional tick.
		/// </summary>
		private void Anchor(double worldSeconds, double rate)
		{
			uint whole = (uint)Math.Floor(Tick);
			double into = (Tick - whole) * TickDelta * rate;
			WorldClock.Shared.Override(whole, worldSeconds - into, rate, verified: true);
		}
	}
}
