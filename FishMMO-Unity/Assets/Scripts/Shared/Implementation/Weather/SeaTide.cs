using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A scene's sea level as the simulation knows it: mean level plus the tide, at a synced server tick. The server
	/// and every client work it out alike from the world clock, so a swimmer's float height and the depth an agent
	/// judges agree on both sides, and the water the client draws (<c>WaterEnvironment</c>) stands at the same tide.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>On a grid, to the millimetre.</b> The tide is worked out at every <see cref="GridTicks"/>th tick and
	/// interpolated between, then rounded to a millimetre: a prediction and its reconcile read the identical level
	/// even when one is a frame's worth of floating point away from the other, and the moons' sums are added up
	/// once every ten seconds rather than for every swimmer every tick. A tide moves the sea a few metres in six
	/// hours, a tenth of a millimetre a second: nothing a grid that fine can show.
	/// </para>
	/// <para>
	/// <b>Never the waves.</b> The simulation's sea is still: waves are drawn, not stood on, and a wave's height
	/// depends on the frame's clock, which no two peers share.
	/// </para>
	/// </remarks>
	public sealed class SeaTide
	{
		/// <summary>Ticks between the tide's samples.</summary>
		public const uint GridTicks = 300;

		/// <summary>Mean sea level, world metres: the level the scene was generated against.</summary>
		public float MeanLevel;

		/// <summary>Whether the tide moves this sea at all.</summary>
		public bool Drive = true;

		/// <summary>How much this coast magnifies the open-ocean tide.</summary>
		public float Amplification = 2.5f;

		/// <summary>The most the tide may move the sea, metres.</summary>
		public float MaximumMetres = 2.5f;

		/// <summary>The scene's place on its world, degrees.</summary>
		public double Latitude;
		public double Longitude;

		/// <summary>The world the tide is raised on; null, the active system's home world.</summary>
		public WorldBody Body;

		/// <summary>The clock the tide keeps time by; null, <see cref="WorldClock.Shared"/>.</summary>
		public WorldClock Clock;

		/// <summary>
		/// Grid cells kept. Three, because a client replaying its inputs walks back across a grid line and forward again
		/// every reconcile near one, and the ships pose every one of those ticks: with one cell each crossing summed the
		/// moons twice over. A cell is a pure function of its grid number and the clock's anchor, so keeping more changes
		/// no answer.
		/// </summary>
		private const int Cells = 3;
		private readonly uint[] cellGrid = { uint.MaxValue, uint.MaxValue, uint.MaxValue };
		private readonly float[] cellLow = new float[Cells];
		private readonly float[] cellHigh = new float[Cells];
		private int cellNext;
		/// <summary>The clock's anchor the cells were sampled under: a set or a correction moves every tick's hour, so all go.</summary>
		private WorldClock anchorClock;
		private WorldClockAnchor anchorCurrent, anchorPrevious;
		private bool anchorHas, anchorHasPrevious;
		private uint anchorSlew;
		private double anchorTickDelta;

		/// <summary>The still sea's level at a synced server tick, world metres.</summary>
		public float LevelAt(uint tick) => MeanLevel + TideAt(tick);

		/// <summary>How far the tide stands above mean level at a synced server tick, metres, to the millimetre.</summary>
		public float TideAt(uint tick)
		{
			if (!Drive)
			{
				return 0f;
			}
			uint grid = tick / GridTicks;
			CheckAnchor();
			int cell = -1;
			for (int c = 0; c < Cells; c++)
			{
				if (cellGrid[c] == grid)
				{
					cell = c;
					break;
				}
			}
			if (cell < 0)
			{
				cell = cellNext;
				cellNext = (cellNext + 1) % Cells;
				cellLow[cell] = SampleAt((double)grid * GridTicks);
				cellHigh[cell] = SampleAt((double)(grid + 1) * GridTicks);
				cellGrid[cell] = grid;
			}
			float f = (tick - grid * GridTicks) / (float)GridTicks;
			return Mathf.Round(Mathf.Lerp(cellLow[cell], cellHigh[cell], f) * 1000f) / 1000f;
		}

		/// <summary>Forgets the cached samples: after a setting changed, or the clock was re-anchored.</summary>
		public void Invalidate()
		{
			for (int c = 0; c < Cells; c++)
			{
				cellGrid[c] = uint.MaxValue;
			}
			cellNext = 0;
		}

		/// <summary>Forgets the cells when the clock they were sampled by has been set, corrected or swapped.</summary>
		private void CheckAnchor()
		{
			WorldClock clock = Clock ?? WorldClock.Shared;
			WorldClockAnchor current = clock.Current, previous = clock.Previous;
			if (ReferenceEquals(clock, anchorClock) && clock.HasAnchor == anchorHas && clock.HasPrevious == anchorHasPrevious
				&& clock.SlewTicks == anchorSlew && clock.TickDelta == anchorTickDelta
				&& Same(current, anchorCurrent) && Same(previous, anchorPrevious))
			{
				return;
			}
			anchorClock = clock;
			anchorHas = clock.HasAnchor;
			anchorHasPrevious = clock.HasPrevious;
			anchorSlew = clock.SlewTicks;
			anchorTickDelta = clock.TickDelta;
			anchorCurrent = current;
			anchorPrevious = previous;
			Invalidate();
		}

		private static bool Same(WorldClockAnchor a, WorldClockAnchor b)
			=> a.Tick == b.Tick && a.WorldSeconds.Equals(b.WorldSeconds) && a.Rate.Equals(b.Rate);

		private float SampleAt(double tick)
		{
			WorldClock clock = Clock ?? WorldClock.Shared;
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = Body != null ? Body : system != null ? system.HomeWorld : null;
			return TideMetres(system, body, clock.WorldHoursAt(tick), Latitude, Longitude, Amplification, MaximumMetres);
		}

		/// <summary>The coast's tide at <paramref name="hours"/>, metres above mean level: the open-ocean tide amplified, and scaled to fit the maximum.</summary>
		public static float TideMetres(SolarSystemProfile system, WorldBody body, double hours, double latitude, double longitude, float amplification, float maximum)
		{
			if (system == null || body == null)
			{
				return 0f;
			}
			double equilibrium = PlanetTides.HeightMetres(system, body, hours, latitude, longitude, out double reach);
			return ScaledTide(equilibrium, reach, amplification, maximum);
		}

		/// <summary>
		/// The tide at the coast, in metres: the equilibrium tide amplified for the coast, and scaled
		/// down WHOLE when it could pass the most this scene allows.
		/// </summary>
		/// <param name="equilibrium">The open-ocean tide now (PlanetTides.HeightMetres).</param>
		/// <param name="reach">The most that tide could stand from mean level here, with the perturbers where they are.</param>
		/// <param name="amplification">How much the coast magnifies it.</param>
		/// <param name="maximum">The most the tide may move the sea.</param>
		/// <remarks>
		/// <para>
		/// <b>Scaled, never clipped.</b> The limit is a level-design tool — the terrain is fixed and
		/// the waterline is not, and two metres of tide on a gentle beach moves the shore tens of
		/// metres — and it used to be a clamp. On Arthis, whose moon Helis orbits under fourteen of
		/// its radii out, the equilibrium tide is ninety metres; amplified and clamped, the sea sat at
		/// the top of the clamp for hours, fell five metres almost at once, sat at the bottom, and
		/// jumped back — the whole sea visibly lurching with the moon. Divided by its own reach
		/// instead, a tide of any size keeps its shape — two highs a moon-day, springs and neaps — and
		/// its highest high water is the maximum. The reach moves only as the perturbers move north
		/// and south and nearer and further, so the scale does too: slowly, and smoothly.
		/// </para>
		/// <para>
		/// The clamp stays, and never binds: the reach bounds the tide, so it is only a guard.
		/// </para>
		/// </remarks>
		public static float ScaledTide(double equilibrium, double reach, float amplification, float maximum)
		{
			double gain = System.Math.Max(0f, amplification);
			double limit = System.Math.Max(0f, maximum);
			double peak = reach * gain;
			double scale = peak > limit && peak > 1e-9 ? limit / peak : 1.0;
			return Mathf.Clamp((float)(equilibrium * gain * scale), -(float)limit, (float)limit);
		}
	}
}
