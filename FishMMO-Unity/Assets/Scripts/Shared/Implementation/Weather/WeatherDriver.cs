using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What makes the weather happen: large air masses drifting over the world, as a pure function
	/// of the world seed, the place and the world clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nothing drove the weather before this. Storm cells were spawned by a server-side director
	/// from an RNG seeded partly on <c>Environment.TickCount</c>, so the weather was different after
	/// every restart and could not be worked out from the clock; the background under them never
	/// changed at all, whatever the season or the hour. The sky therefore only ever varied when a
	/// cell happened to wander past.
	/// </para>
	/// <para>
	/// Here weather is a field, not a schedule. Highs and lows sit in a slowly turning noise field
	/// that drifts across the world on the prevailing wind, so weather varies from place to place as
	/// well as from hour to hour: a front takes its time crossing a scene, you can see it coming,
	/// and you can walk toward the weather you want — which is the whole point of the biome and
	/// storm-cell design, and was not true while the background was one fixed frame per biome.
	/// </para>
	/// <para>
	/// Every value here is a pure function of (seed, position, world seconds). The server and every
	/// client compute the same weather from the same clock without a byte on the wire, and the
	/// weather at any moment — past or future — can be worked out without having been there. That is
	/// what lets the same sky be drawn on two machines, and what makes a bug reproducible.
	/// </para>
	/// <para>
	/// The field drifts on the same vector the clouds do, deliberately. Cloud advection and weather
	/// advection are the same motion of the same air, and computing them separately is how a sky
	/// ends up showing clear weather in a cloud bank.
	/// </para>
	/// </remarks>
	public static class WeatherDriver
	{
		/// <summary>
		/// The seed the whole world's weather grows from, taken from the solar system so that the
		/// server and every client read the same number from the same asset.
		/// </summary>
		/// <remarks>
		/// Not <c>Environment.TickCount</c>, which is what the old server-side director mixed into
		/// its seed: that made the weather different after every restart and impossible to work out
		/// from the clock, which is the opposite of what a driver is for.
		/// </remarks>
		public static uint WorldSeed =>
			FishMMO.Shared.Celestial.SolarSystemProfile.Active != null
				? FishMMO.Shared.Celestial.SolarSystemProfile.Active.WeatherSeed
				: 238u;

		/// <summary>The wind a system's size is reckoned against, in metres per second.</summary>
		/// <remarks>
		/// The middle of what <see cref="PrevailingSpeed"/> produces. Systems are sized in time — so
		/// many hours to pass — and this is what converts that into a distance.
		/// </remarks>
		private const float TypicalSpeed = 8f;

		/// <summary>
		/// Metres one tile of the field covers: how big a weather system is.
		/// </summary>
		/// <remarks>
		/// Derived from how long a system should take to pass, because hours are the unit this is
		/// actually judged in and metres of noise are not. Sized directly it was thirty kilometres,
		/// which at an ordinary wind is about an hour — a tenth to a third of a six-hour day, so the
		/// weather turned over completely three to eleven times a day and never settled into
		/// anything. Note that the fix is the size and not the wind: slowing the wind would have
		/// slowed the clouds with it and left the sky becalmed.
		/// </remarks>
		public static float SystemMetres
		{
			get
			{
				FishMMO.Shared.Celestial.SolarSystemProfile system = FishMMO.Shared.Celestial.SolarSystemProfile.Active;
				float hours = system != null ? Mathf.Max(0.25f, system.WeatherSystemHours) : 5f;
				return hours * 3600f * TypicalSpeed;
			}
		}

		/// <summary>The larger swing the systems themselves ride on: settled spells and unsettled ones.</summary>
		/// <remarks>Several systems across, so a run of wet days is followed by a run of fine ones.</remarks>
		public static float RegimeMetres => SystemMetres * 4.7f;

		/// <summary>
		/// The window the single-precision drift is wrapped into, in metres, for callers that need a
		/// float. Nothing that samples the field uses it.
		/// </summary>
		/// <remarks>
		/// The field itself is sampled in double from the *unwrapped* drift. It used to be wrapped
		/// here on the claim that the window was "a whole number of nothing" — but the noise is a
		/// lattice hash, not a tiling texture, and has no period at all, so every time the drift
		/// crossed the window the whole weather teleported: one sky at 36 h 24 m of world time, a
		/// different one a second later, on the server and every client together. A float is only
		/// handed out for the renderer, and the renderer wraps that itself to each texture's own
		/// period, which is the one wrap that really is a whole number of nothing.
		/// </remarks>
		public const double DriftWrapMetres = 1048576.0;

		// ── Noise ─────────────────────────────────────────────────────
		// Value noise with its own hash, rather than Mathf.PerlinNoise: Unity does not promise the
		// same PerlinNoise across platforms or versions, and this has to agree between a Linux
		// server and a browser client for years. Determinism is the entire point of this file.

		private static float Hash(int x, int y, uint seed)
		{
			uint h = seed;
			h ^= (uint)x * 0x9E3779B1u;
			h ^= (uint)y * 0x85EBCA77u;
			h ^= h >> 15;
			h *= 0x2545F491u;
			h ^= h >> 13;
			h *= 0xC2B2AE35u;
			h ^= h >> 16;
			return (h & 0xFFFFFFu) / (float)0xFFFFFF;
		}

		/// <summary>
		/// Smooth value noise on the ground plane, 0..1. The coordinates arrive in double because
		/// they are the drift, which is unbounded; only the fraction within a cell is ever a float.
		/// </summary>
		private static float Noise(double x, double y, uint seed)
		{
			double fx = System.Math.Floor(x), fy = System.Math.Floor(y);
			int ix = (int)fx, iy = (int)fy;
			float tx = (float)(x - fx), ty = (float)(y - fy);
			// Smoothstep on each axis: value noise with linear blending shows its lattice.
			tx = tx * tx * (3f - 2f * tx);
			ty = ty * ty * (3f - 2f * ty);
			float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
			float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
			return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
		}

		private static int WrapCell(int cell, int period) => ((cell % period) + period) % period;

		/// <summary>
		/// Gradient noise on a lattice that repeats every <paramref name="period"/> cells, centred on
		/// 0.5 with the same spread as the value noise it replaced.
		/// </summary>
		/// <remarks>
		/// For the terms the renderer draws. Value noise can only peak and trough at its lattice
		/// points, and its interpolation runs along the lattice's axes, so a sky cut from it has its
		/// banks and lanes lined up with north and east: measured, its edges favour the two axes three
		/// times over any other direction. Gradient noise has no such preference (1.2). Scaled by
		/// 0.868 so two octaves of it have the 0.140 standard deviation the formation contrast was
		/// calibrated against. <c>FishCloudPeriodicGradient</c> in FishCloudVolume.hlsl is its twin.
		/// </remarks>
		private static float PeriodicGradient(double x, double y, int period, uint seed)
		{
			double fx = System.Math.Floor(x), fy = System.Math.Floor(y);
			int ix = (int)fx, iy = (int)fy;
			float tx = (float)(x - fx), ty = (float)(y - fy);
			float sx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);
			float sy = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);
			float n00 = GradientDot(ix, iy, tx, ty, period, seed);
			float n10 = GradientDot(ix + 1, iy, tx - 1f, ty, period, seed);
			float n01 = GradientDot(ix, iy + 1, tx, ty - 1f, period, seed);
			float n11 = GradientDot(ix + 1, iy + 1, tx - 1f, ty - 1f, period, seed);
			float a = n00 + (n10 - n00) * sx;
			float b = n01 + (n11 - n01) * sx;
			return 0.5f + (a + (b - a) * sy) * GradientSpread;
		}

		/// <summary>What makes gradient noise as widely spread as the value noise the calibration was measured on.</summary>
		private const float GradientSpread = 0.868f;

		private static float GradientDot(int cx, int cy, float dx, float dy, int period, uint seed)
		{
			float angle = Hash(WrapCell(cx, period), WrapCell(cy, period), seed) * (Mathf.PI * 2f);
			return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
		}

		/// <summary>Two octaves, which is all a weather map needs: a system and the swell under it.</summary>
		private static float Field(double xMetres, double yMetres, float scaleMetres, uint seed)
		{
			double x = xMetres / scaleMetres, y = yMetres / scaleMetres;
			return Noise(x, y, seed) * 0.65f + Noise(x * 2.3 + 11.7, y * 2.3 - 4.1, seed ^ 0x5BD1E995u) * 0.35f;
		}

		// ── Wind ──────────────────────────────────────────────────────

		/// <summary>
		/// Which way the air moves at a latitude, as a unit vector on the ground plane.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is where the planet's rotation reaches the weather. A spinning world sorts its winds
		/// into bands — easterly trades near the equator, westerlies in the middle latitudes,
		/// easterlies again at the poles — and that is why weather arrives from a reliable direction
		/// and why it is a different direction at a different latitude. Modelling the bands rather
		/// than picking a random heading per scene is what makes "the weather comes from the west
		/// here" a thing a player can learn.
		/// </para>
		/// <para>
		/// How wide the bands are is the world's, not ours (<see cref="WindBelts"/>): our thirty-degree
		/// cells are what a day of twenty-four hours makes of a world our size. A faster, smaller
		/// world packs more, narrower bands between equator and pole, and a slow one has a single cell
		/// from one to the other. The north–south part of the flow follows the cell as well: toward
		/// the equator in the trades and the polar easterlies, toward the pole in the westerlies —
		/// the surface half of each overturning cell. It was a fixed poleward lean everywhere, which
		/// is backwards for the trades.
		/// </para>
		/// </remarks>
		public static Vector2 PrevailingWind(float latitudeDegrees, in WindBelts belts)
		{
			float absolute = Mathf.Abs(latitudeDegrees);
			float cell = Mathf.Clamp(belts.CellDegrees, 1f, 90f);
			int index = Mathf.Min((int)(absolute / cell), 1000);
			float start = index * cell;
			bool last = start + cell >= 90f - 1e-3f;
			// Where in its cell this latitude is, 0 at the equatorward edge; the last cell runs to the pole.
			float span = last ? Mathf.Max(1e-3f, 90f - start) : cell;
			float t = Mathf.Clamp01((absolute - start) / span);
			// +1 blows toward the east, -1 toward the west.
			float eastward;
			// +1 toward the pole, -1 toward the equator.
			float poleward;
			if (index == 0)
			{
				// The Hadley cell's surface flow: out of the east, strongest at the equator.
				eastward = -Mathf.Cos(Mathf.Clamp01(absolute / cell) * Mathf.PI * 0.5f);
				poleward = -1f;
			}
			else if ((index & 1) == 1)
			{
				// An eddy-driven westerly belt, strongest in its middle.
				eastward = Mathf.Sin(t * Mathf.PI);
				poleward = 1f;
			}
			else
			{
				// Easterlies again; the last band's strongest at the pole itself.
				eastward = last ? -Mathf.Sin(t * Mathf.PI * 0.5f) : -Mathf.Sin(t * Mathf.PI);
				poleward = -1f;
			}
			// A world that turns backwards turns every belt round with it.
			eastward *= belts.Handedness;
			var wind = new Vector2(eastward, Mathf.Sign(latitudeDegrees == 0f ? 1f : latitudeDegrees) * poleward * 0.25f);
			return wind.sqrMagnitude < 1e-6f ? Vector2.right : wind.normalized;
		}

		/// <summary>The home world's belts: for callers with no body of their own to ask.</summary>
		public static Vector2 PrevailingWind(float latitudeDegrees) => PrevailingWind(latitudeDegrees, WindBelts.Home);

		/// <summary>
		/// How strongly the upper air runs at a latitude, 0.25..1: highest under a westerly jet.
		/// </summary>
		/// <remarks>
		/// A broad hump on each westerly belt's middle, falling to a quarter two cells away. With our
		/// own thirty-degree cells it is exactly the hump on forty-five degrees the field was tuned
		/// with; a world with more belts has more jets.
		/// </remarks>
		/// <summary>How strongly the upper air runs at a latitude, 0.25..1 (<see cref="JetBand"/>): where the jets are.</summary>
		public static float JetStrength(float latitudeDegrees, in WindBelts belts) => JetBand(latitudeDegrees, belts);

		private static float JetBand(float latitudeDegrees, in WindBelts belts)
		{
			float cell = Mathf.Clamp(belts.CellDegrees, 1f, 90f);
			float absolute = Mathf.Abs(latitudeDegrees);
			// The middles of the westerly belts sit at 1.5, 3.5, 5.5… cells.
			float k = Mathf.Round((absolute / cell - 1.5f) / 2f);
			float nearest = (1.5f + 2f * Mathf.Max(0f, k)) * cell;
			return Mathf.Clamp(1f - Mathf.Abs(absolute - nearest) / (2f * cell), 0.25f, 1f);
		}

		/// <summary>
		/// The steady speed the whole field is carried at, in metres per second. Constant in time.
		/// </summary>
		/// <remarks>
		/// This one must not vary with the clock, and the reason is worth keeping. The drift below is
		/// <c>speed × elapsed</c>, which is only an integral of the speed while the speed holds
		/// still; let it wobble and the derivative picks up an <c>elapsed × d(speed)/dt</c> term that
		/// grows without bound. A wobble of one metre per second a day into the world then throws the
		/// sampled position a hundred kilometres sideways in an instant — the field teleports, and
		/// the weather flips several times an hour no matter how large the systems are made. That is
		/// exactly what it did. The wind that varies is <see cref="PrevailingSpeed"/>, which is
		/// reported to the weather and never moves the field.
		/// </remarks>
		public static float AdvectionSpeed(uint worldSeed, float latitudeDegrees, in WindBelts belts)
		{
			float band = JetBand(latitudeDegrees, belts);
			// Seeded per latitude band, so one world's westerlies run faster than another's, and
			// steady for ever within a world.
			float place = Noise(latitudeDegrees * 0.05f, 61.3f, worldSeed ^ 0x3B9ACA07u);
			return Mathf.Lerp(4f, 12f, Mathf.Clamp01(band * 0.7f + place * 0.3f));
		}

		/// <summary>
		/// The wind as the weather reports it at a moment, in metres per second. Varies over hours.
		/// </summary>
		/// <remarks>
		/// What a player feels and what the wind channel carries. Kept apart from
		/// <see cref="AdvectionSpeed"/> on purpose: the large-scale march of the air is steady, while
		/// the wind over any given spot is not, and only the steady part may move the field.
		/// </remarks>
		public static float PrevailingSpeed(uint worldSeed, float latitudeDegrees, double worldSeconds, in WindBelts belts)
		{
			// The jet is strongest in the middle latitudes, and the whole thing breathes over hours.
			// The swell is centred so it can drop the wind as well as raise it: added only upward, as
			// it was at first, the air never fell below about ten metres a second anywhere — a stiff
			// breeze that never let up, which among other things made fog impossible to form.
			float band = JetBand(latitudeDegrees, belts);
			float swell = Noise((float)(worldSeconds / 4800.0), 37.2f, worldSeed ^ 0xA136AAADu);
			return Mathf.Lerp(1.5f, 18f, Mathf.Clamp01(band * 0.55f + (swell - 0.5f) * 0.7f));
		}

		/// <summary>
		/// How far the air — and with it the clouds and the weather field — has travelled by now.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the primitive, not the wind: the drift is defined directly as a function of time
		/// and the wind is its slope. Integrating a wind that changes is what a client used to do,
		/// frame by frame with <c>Time.deltaTime</c>, and it made cloud position a record of that
		/// client's frame rate and join time — two players stood together saw different skies, a
		/// hitch moved one of them for good, and a reconnect snapped the sky sideways. Defining the
		/// position instead means any machine can work out where the air is at any moment, in one
		/// step, and they all agree.
		/// </para>
		/// <para>
		/// A steady march plus a bounded wander: the wander keeps the motion from being a dead
		/// straight line without ever letting the two terms disagree about where the air is.
		/// </para>
		/// </remarks>
		public static Vector2 Drift(uint worldSeed, float latitudeDegrees, double worldSeconds, in WindBelts belts)
		{
			DriftExact(worldSeed, latitudeDegrees, worldSeconds, belts, out double x, out double y);
			// Wrapped only so that it fits a float without losing metres. Anything that samples the
			// field takes the exact one above; this is for readouts and for the renderer, which
			// re-wraps it to each of its textures' own periods before it is used.
			x -= System.Math.Floor(x / DriftWrapMetres) * DriftWrapMetres;
			y -= System.Math.Floor(y / DriftWrapMetres) * DriftWrapMetres;
			return new Vector2((float)x, (float)y);
		}

		/// <summary>The drift, exact, in double: what the field is sampled through.</summary>
		public static void DriftExact(uint worldSeed, float latitudeDegrees, double worldSeconds, in WindBelts belts, out double x, out double y)
		{
			Vector2 direction = PrevailingWind(latitudeDegrees, belts);
			float speed = AdvectionSpeed(worldSeed, latitudeDegrees, belts);
			// The linear term grows without bound and is the whole reason this is a double: at a
			// year in, single precision is already rounding it to sixteen metres.
			double travelled = worldSeconds * speed;

			// A slow wander across the flow, bounded, so a front does not track a ruler.
			// Slow, and bounded: a quarter-day lattice, so it bends the track of a front over hours
			// rather than shivering it minute to minute.
			double phase = worldSeconds / 5400.0;
			float wanderX = Noise(phase, 5.5, worldSeed ^ 0x27D4EB2Fu) - 0.5f;
			float wanderY = Noise(phase + 19.3, 88.1, worldSeed ^ 0x165667B1u) - 0.5f;
			x = direction.x * travelled + wanderX * 2500.0;
			y = direction.y * travelled + wanderY * 2500.0;
		}

		// ── The field ─────────────────────────────────────────────────

		// ── Formations ────────────────────────────────────────────────
		// Between a weather system (a hundred-odd kilometres) and a single cloud (a few) there is a
		// whole scale of organisation the field did not have: cumulus streets, the honeycomb of open
		// and closed cells behind a cold front, the banks and lanes a sky arranges itself into.
		// Without it, cover was one number for the whole sky and the cloud noise was cut by one flat
		// threshold everywhere — which is exactly what "random noise rather than cloud formations"
		// looks like, because a field with the same statistics at every point cut at the same level
		// at every point produces an even scatter of identical blobs. This term modulates the cover
		// across the ground at the scale of the masses themselves, so the sky has banks and gaps,
		// and it lives here rather than in the renderer because the rain has to fall under the
		// bank and not in the gap: the server reads the same term.

		/// <summary>Metres one tile of the formation noise covers.</summary>
		public const float MesoscaleMetres = 24000f;
		/// <summary>The lattice repeats every this many tiles: what lets the renderer wrap the drift.</summary>
		public const int MesoscalePeriodTiles = 10;
		/// <summary>How much cover a formation adds or takes away at its peak, either way.</summary>
		public const float MesoscaleAmplitude = 0.28f;
		/// <summary>Mixed into the world seed for the formation lattice; the shader is handed the mix.</summary>
		public const uint MesoscaleSeedMix = 0x7F4A7C15u;

		/// <summary>How hard the formations contrast: unstable air makes cells, stable air makes sheets.</summary>
		/// <remarks>
		/// Set from the lattice noise's measured spread, not guessed: two octaves of it have a
		/// standard deviation of 0.153 about 0.5, so a contrast of 3 puts the term's own spread at
		/// 0.44 (a cover spread of ±0.12 at the default amplitude, 3% of the sky clamped) and 4.5 at
		/// 0.6 (±0.17, 15% clamped into solid bank or clear lane). At the 1.4–2.6 it was first given
		/// the cover moved by four to seven hundredths and the sky looked exactly as it had.
		/// </remarks>
		public static float MesoscaleContrast(float instability) => Mathf.Lerp(3.0f, 4.5f, Mathf.Clamp01(instability));

		/// <summary>
		/// The formation term at a drifted position, −1..1. Computed identically by
		/// <c>FishCloudMesoscale</c> in FishCloudVolume.hlsl; change one and change the other.
		/// </summary>
		/// <remarks>
		/// Two octaves of a periodic lattice, and the second octave's ratio is 2.3 so that ten tiles
		/// hold exactly twenty-three of it: the period of the pair is ten tiles, and a drift wrapped
		/// to ten tiles lands on the same field. Contrast rises with instability — unstable air
		/// arranges itself into cells with hard edges and clear lanes, stable air into sheets.
		/// </remarks>
		public static float Mesoscale(uint worldSeed, double driftedX, double driftedY, float instability)
		{
			double x = driftedX / MesoscaleMetres, y = driftedY / MesoscaleMetres;
			uint seed = worldSeed ^ MesoscaleSeedMix;
			float n = PeriodicGradient(x, y, MesoscalePeriodTiles, seed) * 0.65f
				+ PeriodicGradient(x * 2.3 + 11.7, y * 2.3 - 4.1, MesoscalePeriodTiles * 23 / 10, seed ^ 0x5BD1E995u) * 0.35f;
			return Mathf.Clamp((n - 0.5f) * MesoscaleContrast(instability), -1f, 1f);
		}

		/// <summary>
		/// How much the formations shift the cover at a place, in cover units, at a moment. The sky
		/// subtracts this at the camera from the cover it is handed and adds the shader's own copy
		/// back per sample, so the cover overhead is exactly the forecast and the cover elsewhere
		/// follows the formations.
		/// </summary>
		public static float MesoscaleCoverAt(uint worldSeed, Vector2 positionMetres, double worldSeconds, float latitudeDegrees, float season01, in WindBelts belts)
		{
			Synoptic air = Sample(worldSeed, positionMetres, worldSeconds, latitudeDegrees, season01, 0.5f, belts);
			return air.Mesoscale * MesoscaleAmplitude;
		}

		/// <summary>The same, on the home world's belts.</summary>
		public static float MesoscaleCoverAt(uint worldSeed, Vector2 positionMetres, double worldSeconds, float latitudeDegrees, float season01)
		{
			return MesoscaleCoverAt(worldSeed, positionMetres, worldSeconds, latitudeDegrees, season01, WindBelts.Home);
		}

		// ── Columns ───────────────────────────────────────────────────
		// A cloud is one column of air from the condensation level up, and what differs from place
		// to place is how far up it gets: a few hundred metres makes a deck, a couple of kilometres a
		// heap, the whole depth a tower. That height is a property of the *place* — the deck, the
		// heap and the tower share a base and are the same field — so it is a map, not a layer.
		// This is the map's sparse part: where, within a sky, the air goes all the way up.

		/// <summary>Metres one tile of the tower noise covers: towers are a few kilometres across.</summary>
		public const float TowerMetres = 9000f;
		/// <summary>The lattice repeats every this many tiles, so the renderer can wrap its drift.</summary>
		public const int TowerPeriodTiles = 16;
		/// <summary>Mixed into the world seed for the tower lattice; the shader is handed the mix.</summary>
		public const uint TowerSeedMix = 0x2C1B3C6Du;

		/// <summary>
		/// 0..1: how strongly a tower wants to stand at a drifted position. Sparse on purpose — most
		/// of a sky is deck or heap and only some places go up. Computed identically by
		/// <c>FishCloudTower</c> in FishCloudVolume.hlsl; change one and change the other.
		/// </summary>
		/// <remarks>
		/// <para>
		/// One candidate tower per lattice cell, at a hashed place within it, of a hashed size and
		/// strength, falling away round a flat core. It was the peaks of a value-noise lattice, and
		/// value noise can only peak AT its lattice points: every tower in the world stood on a
		/// nine-kilometre square grid, with a squarish footprint, and neighbouring peaks ran together
		/// into ridges along north and east. Nothing in the air is on a grid.
		/// </para>
		/// <para>
		/// Sized to cover what the lattice did — a strong tower over 18% of the sky (was 19%), a mean
		/// of 0.20 (was 0.19) — because the rain and the lightning were calibrated against it.
		/// </para>
		/// </remarks>
		public static float Tower(uint worldSeed, double driftedX, double driftedY)
		{
			double x = driftedX / TowerMetres, y = driftedY / TowerMetres;
			uint seed = worldSeed ^ TowerSeedMix;
			double fx = System.Math.Floor(x), fy = System.Math.Floor(y);
			int ix = (int)fx, iy = (int)fy;
			float best = 0f;
			for (int dy = -1; dy <= 1; dy++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int cx = ix + dx, cy = iy + dy;
					int wx = WrapCell(cx, TowerPeriodTiles), wy = WrapCell(cy, TowerPeriodTiles);
					float strength = Hash(wx, wy, seed);
					strength = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((strength - TowerThreshold) / (0.85f - TowerThreshold)));
					if (strength <= 0f)
					{
						continue;
					}
					float jitterX = 0.2f + 0.6f * Hash(wx, wy, seed ^ 0x68E31DA4u);
					float jitterY = 0.2f + 0.6f * Hash(wx, wy, seed ^ 0xB5297A4Du);
					float radius = TowerRadiusCells * (0.75f + 0.5f * Hash(wx, wy, seed ^ 0x1B56C4E9u));
					float ox = (float)(x - (cx + jitterX)), oy = (float)(y - (cy + jitterY));
					float d = Mathf.Sqrt(ox * ox + oy * oy) / radius;
					float t = Mathf.Clamp01((d - TowerCore) / (1f - TowerCore));
					float fall = 1f - t * t * (3f - 2f * t);
					best = Mathf.Max(best, strength * fall);
				}
			}
			return best;
		}

		/// <summary>The hash above which a cell has a tower at all.</summary>
		private const float TowerThreshold = 0.55f;
		/// <summary>A tower's radius, in cells, before its own ±25%.</summary>
		private const float TowerRadiusCells = 0.65f;
		/// <summary>The share of a tower's radius that is its flat core.</summary>
		private const float TowerCore = 0.35f;

		/// <summary>How much of a tower the air allows: none in stable air, most of the way in unstable.</summary>
		public static float TowerGain(float instability) => Mathf.Lerp(0.05f, 0.6f, Mathf.Clamp01(instability));

		/// <summary>The type every column starts from before its tower: 0.1 a flat deck, 0.5 a heaped sky.</summary>
		public static float BaseColumnType(float instability) => Mathf.Lerp(0.1f, 0.5f, Mathf.Clamp01(instability));

		/// <summary>What the air is doing at a place and a moment.</summary>
		public struct Synoptic
		{
			/// <summary>0..1: how strongly a tower stands here. The rain and the lightning come out of these.</summary>
			public float Tower;
			/// <summary>0 a flat deck, 0.5 a heap, 1 a tower through the whole depth: how tall the column here grows.</summary>
			public float ColumnType;
			/// <summary>−1..1: how much the local formation adds to or takes from the cover here.</summary>
			public float Mesoscale;
			/// <summary>−1 the middle of a deep low, +1 a settled high.</summary>
			public float Pressure;
			/// <summary>0 dry, 1 saturated.</summary>
			public float Humidity;
			/// <summary>The temperature anomaly this air carries, −1..1, before the climate.</summary>
			public float Temperature;
			/// <summary>0..1: how willing this air is to build a storm.</summary>
			public float Instability;
			/// <summary>The wind here, in metres per second.</summary>
			public Vector2 Wind;
			/// <summary>The hour this air was sampled at, 0.5 noon. Fog and the daily swing need it.</summary>
			public float LocalTime01;
			/// <summary>
			/// How far the sun has stopped setting here (0 it sets as usual, 1 it does not set at all):
			/// the midnight sun, inside a polar circle in its summer.
			/// </summary>
			public float PolarDay;
			/// <summary>How far the sun has stopped rising here: the polar night, in its winter.</summary>
			public float PolarNight;
			/// <summary>
			/// How much of a rotating storm's updraught is here, 0..1: a supercell's mesocyclone,
			/// whose spin lowers the pressure inside it and pulls the rising air up faster than its
			/// buoyancy alone would.
			/// </summary>
			public float Mesocyclone;
		}

		/// <summary>
		/// What the amount of air on a world does to the weather the field gives it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The field describes how air behaves; how much of it there is decides what that amounts to.
		/// No air, no weather: nothing to carry water, hold a cloud, blow, or scatter light into a
		/// fog — an airless moon has a black sky and a bare ground whatever the field says, and this
		/// returns a clear frame for it. Thin air holds little water and lets what it has go quickly:
		/// cloud is sparse and high and thin, rain is rare and light, fog barely forms, and storms
		/// have nothing to build with — though the wind, with so little to slow it, runs faster.
		/// Thick air is the opposite way: a heavy, humid blanket that is rarely clear, rains more and
		/// harder, never quite loses its haze, breeds lightning, and moves sluggishly.
		/// </para>
		/// <para>
		/// Applied to what the field and the biome give, not to a preset, a layer or a storm cell:
		/// those are somebody asking for that weather by name, and they get what they asked for.
		/// </para>
		/// </remarks>
		public static WeatherFrame UnderAtmosphere(WeatherFrame frame, FishMMO.Shared.Celestial.AtmosphereKind air)
		{
			switch (air)
			{
				case FishMMO.Shared.Celestial.AtmosphereKind.None:
					return WeatherFrame.Clear;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thin:
					frame[WeatherChannel.CloudCover] = CloudCoverUnder(frame[WeatherChannel.CloudCover], air);
					frame[WeatherChannel.CloudDensity] *= 0.6f;
					frame[WeatherChannel.Precipitation] *= 0.3f;
					frame[WeatherChannel.FogDensity] *= 0.25f;
					frame[WeatherChannel.LightningRate] *= 0.2f;
					// An aurora is the upper air glowing, and there is less of it to glow.
					frame[WeatherChannel.Aurora] *= 0.5f;
					frame[WeatherChannel.WetnessTarget] *= 0.3f;
					frame[WeatherChannel.SnowCoverRate] *= 0.3f;
					frame[WeatherChannel.WindSpeed] = Mathf.Clamp01(frame[WeatherChannel.WindSpeed] * 1.2f);
					return frame;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thick:
					// Toward overcast, not merely more: a thick air's clear days are hazy ones.
					frame[WeatherChannel.CloudCover] = CloudCoverUnder(frame[WeatherChannel.CloudCover], air);
					frame[WeatherChannel.CloudDensity] = Mathf.Clamp01(frame[WeatherChannel.CloudDensity] * 1.2f);
					frame[WeatherChannel.Precipitation] = Mathf.Clamp01(frame[WeatherChannel.Precipitation] * 1.25f);
					frame[WeatherChannel.FogDensity] = Mathf.Clamp01(Mathf.Max(frame[WeatherChannel.FogDensity] * 1.5f, 0.06f));
					frame[WeatherChannel.LightningRate] = Mathf.Clamp01(frame[WeatherChannel.LightningRate] * 1.4f);
					frame[WeatherChannel.WindSpeed] *= 0.8f;
					return frame;
				default:
					return frame;
			}
		}

		/// <summary>
		/// Cloud cover in this much air: thin air holds little water and clouds over sparsely; a thick
		/// one tends toward overcast.
		/// </summary>
		public static float CloudCoverUnder(float cover, FishMMO.Shared.Celestial.AtmosphereKind air)
		{
			switch (air)
			{
				case FishMMO.Shared.Celestial.AtmosphereKind.None: return 0f;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thin: return Mathf.Clamp01(cover) * 0.45f;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thick: return 1f - (1f - Mathf.Clamp01(cover)) * 0.6f;
				default: return Mathf.Clamp01(cover);
			}
		}

		/// <summary>How far the banks and gaps of the formations swing the cover, for this much air.</summary>
		public static float FormationScale(FishMMO.Shared.Celestial.AtmosphereKind air)
		{
			switch (air)
			{
				case FishMMO.Shared.Celestial.AtmosphereKind.None: return 0f;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thin: return 0.45f;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thick: return 0.8f;
				default: return 1f;
			}
		}

		/// <summary>
		/// Samples the weather field. Pure: the same arguments give the same answer on any machine,
		/// at any time, without having watched the weather get there.
		/// </summary>
		public static Synoptic Sample(uint worldSeed, Vector2 positionMetres, double worldSeconds, float latitudeDegrees, float season01, float localTime01, in WindBelts belts)
		{
			// The field is read at the place the air came from, so the whole pattern moves downwind:
			// the same drift the clouds use, so what the sky shows and what the weather says are the
			// same air rather than two systems that happen to be near each other. In double, and
			// never wrapped: the field has no period to wrap to.
			DriftExact(worldSeed, latitudeDegrees, worldSeconds, belts, out double driftX, out double driftY);
			double driftedX = positionMetres.x - driftX;
			double driftedY = positionMetres.y - driftY;

			float regime = Field(driftedX, driftedY, RegimeMetres, worldSeed ^ 0x6C078965u);
			float system = Field(driftedX, driftedY, SystemMetres, worldSeed);
			// Settled spells and unsettled ones: the regime decides how deep the lows get, so the
			// weather has weeks that are mostly fine and weeks that are not.
			float pressure = Mathf.Lerp(0.35f, -0.35f, regime) + (system - 0.5f) * 1.6f;
			pressure = Mathf.Clamp(pressure, -1f, 1f);

			// Low pressure draws in damp air; a high dries it out.
			float humidity = Mathf.Clamp01(0.5f - pressure * 0.45f + (Field(driftedX, driftedY, SystemMetres * 0.45f, worldSeed ^ 0x9E3779B9u) - 0.5f) * 0.3f);

			// Summer is far more unstable than winter: the ground heats the air from below.
			float summer = Mathf.Sin(season01 * Mathf.PI * 2f - Mathf.PI * 0.5f) * 0.5f + 0.5f;
			float hemisphere = latitudeDegrees < 0f ? 1f - summer : summer;

			/* The air mass's own warmth, and nothing else. A high is subsiding air, warmed as it
			 * comes down; and the systems carry warm and cold air masses about with them. The season
			 * and the latitude are NOT here: the climate already has both — the sun's height at noon
			 * for this latitude on this day — and this anomaly is added to the climate. It used to
			 * carry its own seasonal swing and its own latitude cooling as well, so a pole was cooled
			 * twice and every summer warmed twice. */
			float airMass = Field(driftedX, driftedY, SystemMetres * 0.8f, worldSeed ^ 0x3C6EF372u);
			float temperature = Mathf.Clamp(pressure * 0.15f + (airMass - 0.5f) * 0.8f, -1f, 1f);

			float instability = Mathf.Clamp01((-pressure * 0.5f + 0.5f) * (0.45f + hemisphere * 0.55f) * (0.4f + humidity * 0.6f));

			float tower = Tower(worldSeed, driftedX, driftedY);
			return new Synoptic
			{
				Mesoscale = Mesoscale(worldSeed, driftedX, driftedY, instability),
				Tower = tower,
				ColumnType = Mathf.Clamp01(BaseColumnType(instability) + tower * TowerGain(instability)),
				LocalTime01 = Mathf.Repeat(localTime01, 1f),
				Pressure = pressure,
				Humidity = humidity,
				Temperature = temperature,
				Instability = instability,
				Wind = PrevailingWind(latitudeDegrees, belts) * PrevailingSpeed(worldSeed, latitudeDegrees, worldSeconds, belts),
			};
		}

		/// <summary>The same, on the home world's belts, for callers with no body to ask about.</summary>
		public static Synoptic Sample(uint worldSeed, Vector2 positionMetres, double worldSeconds, float latitudeDegrees, float season01, float localTime01 = 0.5f)
		{
			return Sample(worldSeed, positionMetres, worldSeconds, latitudeDegrees, season01, localTime01, WindBelts.Home);
		}

		/// <summary>
		/// The same air over a particular place: a wet place makes it damper and a dry one drier.
		/// </summary>
		/// <remarks>
		/// The field knows nothing about the ground it passes over, so a desert and a rainforest
		/// under the same patch of it had exactly the same chance of cloud, rain and fog. The
		/// place's own humidity — its biome and climate, plus what its world's water and heat add —
		/// leans on the air here: a quarter of the range either way, which is enough to make a
		/// desert's fronts pass over mostly dry and a jungle's afternoon towers rain nearly daily,
		/// without ever letting the ground overrule the weather. Instability goes with it, since
		/// damp air is what a tower is built out of. Both sides read the same climate from the same
		/// assets, so this stays a pure function and costs nothing on the wire.
		/// </remarks>
		/// <param name="localHumidity">The place's humidity, −1 parched to +1 sodden.</param>
		public static Synoptic OverPlace(in Synoptic air, float localHumidity)
		{
			Synoptic local = air;
			float before = 0.4f + air.Humidity * 0.6f;
			local.Humidity = Mathf.Clamp01(air.Humidity + Mathf.Clamp(localHumidity, -1f, 1f) * 0.25f);
			float after = 0.4f + local.Humidity * 0.6f;
			local.Instability = Mathf.Clamp01(air.Instability * after / Mathf.Max(0.01f, before));
			local.ColumnType = Mathf.Clamp01(BaseColumnType(local.Instability) + air.Tower * TowerGain(local.Instability));
			return local;
		}

		// ── Aurora ────────────────────────────────────────────────────

		/// <summary>How long the star's activity takes to rise and fall, in world seconds: thirty days.</summary>
		public const double StellarCycleSeconds = 30.0 * 86400.0;
		/// <summary>How long a geomagnetic storm lasts, roughly: two days.</summary>
		public const float AuroraStormSeconds = 2f * 86400f;
		/// <summary>How long a single brightening within a storm lasts: three hours.</summary>
		public const float AuroraSubstormSeconds = 3f * 3600f;

		/// <summary>
		/// How disturbed the body's magnetic field is right now, 0 quiet to 1 a great storm. One figure
		/// for the whole world: a storm is the star's doing, and arrives everywhere at once.
		/// </summary>
		/// <remarks>
		/// The star's activity rises and falls over a cycle, and storms come out of it: rare when it
		/// is quiet, frequent near its peak, each lasting a day or two with brightenings of a few
		/// hours inside it. They are also commoner round the equinoxes, when the body's field lies
		/// best to catch the wind — which is why the season is asked for. From the seed and the clock
		/// alone, so every machine has the same storm at the same moment.
		/// </remarks>
		public static float GeomagneticActivity(uint worldSeed, double worldSeconds, float season01)
		{
			float cycle = 0.5f + 0.5f * Mathf.Sin((float)(worldSeconds / StellarCycleSeconds % 1.0) * Mathf.PI * 2f + (worldSeed & 0xFFFF) * 0.0001f * Mathf.PI * 2f);
			float storm = Field(worldSeconds, 0.0, AuroraStormSeconds, worldSeed ^ 0xA0205A17u);
			float burst = Field(worldSeconds, 0.0, AuroraSubstormSeconds, worldSeed ^ 0x51AB07E5u);
			// Mostly under the line, so mostly quiet; the cycle lowers the line. Measured over a year of
			// the clock: quiet two thirds of the time, a minor disturbance a fifth, a proper storm one
			// hour in eleven and a great one one in forty.
			float disturbed = Mathf.Clamp01((storm * 0.65f + burst * 0.35f - (0.62f - 0.16f * cycle)) / 0.36f);
			// 1 at the equinoxes (a quarter and three quarters of the way round the year), 0 at the solstices.
			float equinox = Mathf.Abs(Mathf.Sin(season01 * Mathf.PI * 2f));
			return Mathf.Clamp01(disturbed * (0.8f + 0.35f * equinox));
		}

		/// <summary>
		/// How much aurora stands overhead at a latitude, 0..1 — before the night, the cloud and the
		/// air have their say, which is the sky's business and the atmosphere's.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An aurora is the star's wind brought down by the body's magnetic field into its upper air,
		/// so it needs all three and sits where the field puts it: in a ring round each magnetic pole,
		/// some twenty-three degrees out. The ring is always there, faintly — at sixty-seven degrees a
		/// clear dark night nearly always has something in it. A storm brightens it and pushes it
		/// toward the equator, as far as fifty degrees in a great one, which is when the middle
		/// latitudes see an aurora at all; and it widens as it goes. Poleward of the ring, inside the
		/// polar cap, there is less, not more.
		/// </para>
		/// <para>
		/// The magnetic pole is taken to be the pole. <paramref name="stellarWind"/> is how much of
		/// its star's output the body gets, against the home world's at 1: a world close in is swept
		/// harder. A body with no field has no ring and no aurora.
		/// </para>
		/// </remarks>
		public static float Aurora(uint worldSeed, double worldSeconds, float latitudeDegrees, float season01, float magneticField, float stellarWind)
		{
			float field = Mathf.Clamp(magneticField, 0f, 2f);
			if (field <= 0.001f)
			{
				return 0f;
			}
			float activity = GeomagneticActivity(worldSeed, worldSeconds, season01);
			// A band, not a blob. A stronger field holds the ring nearer the pole. A storm does not MOVE
			// it toward the equator, it WIDENS it that way: the equatorward edge is driven out — as far
			// as the high forties in a great storm — while the poleward edge holds, so the far north has
			// its best nights during the very storms that let the middle latitudes see one at all.
			// Moved as one piece, the ring deserted the auroral zone whenever anything happened, and
			// sixty-seven degrees never once had a bright night in a measured year.
			//
			// Measured, with a field of 1: something overhead on every dark clear night between about
			// 64° and 72° and bright on one in eight; bright one night in ten at 57°, one in twenty-five
			// at 52°, under one in a hundred at 47°, and never below 40° or above 80°.
			float quiet = Mathf.Lerp(64f, 69f, Mathf.Clamp01(field));
			float poleward = quiet + 3f;
			float equatorward = quiet - 3f - 17f * activity;
			float at = Mathf.Abs(latitudeDegrees);
			float outside = at > poleward ? (at - poleward) / 3.5f
				: at < equatorward ? (equatorward - at) / (3f + 2f * activity)
				: 0f;
			float ring = Mathf.Exp(-outside * outside);
			float wind = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, stellarWind)), 0.4f, 1.6f);
			return Mathf.Clamp01((0.22f + 0.78f * activity) * ring * Mathf.Clamp01(field) * wind);
		}

		/// <summary>
		/// Tells the air how much of a day it is having: the share of this body's day, here and now,
		/// that the sun is up.
		/// </summary>
		/// <remarks>
		/// From the latitude, the body's tilt and lean, and the season, so it is the body's own: a
		/// world tilted sixty degrees has a polar circle that comes down to thirty, and an upright
		/// one has none. Eased in over the last of the daylight and the last of the dark, so a place
		/// crossing into its midnight sun does not have its weather change on one particular day.
		/// </remarks>
		public static Synoptic UnderSun(in Synoptic air, float daylightShare)
		{
			Synoptic local = air;
			float share = Mathf.Clamp01(daylightShare);
			local.PolarDay = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, share));
			local.PolarNight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0f, share));
			return local;
		}

		/// <summary>
		/// What the warmth of a place does to the air over it: how much water that air can carry, and
		/// so how much cloud, rain and storm there is to be had.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Warm air holds far more water than cold — about twice as much for every ten degrees — and
		/// it is water that makes weather. The temperature here is the climate's: the biome, the
		/// latitude and the season, and above all how far the world is from its suns, which is the
		/// largest single thing in it (a world twice as far out reads about −0.64 on this scale, and
		/// four times as far is frozen at −1). It used to decide only WHAT fell, rain or snow. How
		/// MUCH was the field's alone, so a world frozen solid at the edge of its system had the home
		/// world's summer thunderstorms, falling as snow.
		/// </para>
		/// <para>
		/// At the temperate zero the field was tuned at, this does nothing at all. Colder, the air
		/// thins out toward a polar desert: little cloud, light dry snow, no convection to build a
		/// storm on.
		/// </para>
		/// <para>
		/// Warmer, it does nothing either. The humidity is how near saturation the air is, and warmth
		/// raises what the air CAN hold, not how near it is: that is already the air column's, whose
		/// dew point, moist lapse and water condensed per metre all rise with the warmth (Clausius and
		/// Clapeyron) — more water, heavier rain, taller storms. It was scaled up here as well, a
		/// quarter more at +1, which counted the warmth twice: every warm place came out muggy, and a
		/// hot scene's sky could not break at all.
		/// </para>
		/// </remarks>
		public static Synoptic InClimate(in Synoptic air, float temperature)
		{
			float t = Mathf.Clamp(temperature, -1f, 1f);
			// 0.4 of the water at −1, all of it at 0 and above.
			float carries = t < 0f ? Mathf.Lerp(1f, 0.4f, -t) : 1f;
			if (Mathf.Approximately(carries, 1f))
			{
				return air;
			}
			Synoptic local = air;
			float before = 0.4f + air.Humidity * 0.6f;
			local.Humidity = Mathf.Clamp01(air.Humidity * carries);
			float after = 0.4f + local.Humidity * 0.6f;
			// Convection needs warmth as well as water: cold air is stable air.
			float lively = t < 0f ? Mathf.Lerp(1f, 0.35f, -t) : 1f;
			local.Instability = Mathf.Clamp01(air.Instability * after / Mathf.Max(0.01f, before) * lively);
			local.ColumnType = Mathf.Clamp01(BaseColumnType(local.Instability) + air.Tower * TowerGain(local.Instability));
			return local;
		}

		// ── What that looks like ──────────────────────────────────────

		/// <summary>The cloud cover the systems and fronts ask for at one point, with the formations left out.</summary>
		public static float SynopticCoverAt(uint worldSeed, Vector2 positionMetres, double worldSeconds,
			float latitudeDegrees, float season01, float localTime01, in WindBelts belts)
		{
			Synoptic air = Sample(worldSeed, positionMetres, worldSeconds, latitudeDegrees, season01, localTime01, belts);
			air.Mesoscale = 0f;
			return Background(air)[WeatherChannel.CloudCover];
		}

		/// <summary>The field's cloud cover at one point.</summary>
		public static float CoverAt(uint worldSeed, Vector2 positionMetres, double worldSeconds,
			float latitudeDegrees, float season01, float localTime01, in WindBelts belts)
		{
			Synoptic air = Sample(worldSeed, positionMetres, worldSeconds, latitudeDegrees, season01, localTime01, belts);
			return Background(air)[WeatherChannel.CloudCover];
		}

		/// <summary>
		/// The cloud cover the air mass asks for, before the formations put it in banks and lanes:
		/// damp air clouds over, and a low — rising air — clouds over harder.
		/// </summary>
		public static float SynopticCloudCover(in Synoptic air)
		{
			return Mathf.Clamp01(air.Humidity * 1.15f - 0.18f + Mathf.Max(0f, -air.Pressure) * 0.45f);
		}

		/// <summary>
		/// The weather the field asks for here, before the biome's own background and any cells.
		/// </summary>
		/// <remarks>
		/// Deliberately conservative about precipitation: a sky that rains whenever the pressure dips
		/// rains most of the time. Cloud comes on with humidity well before anything falls, which is
		/// what makes an overcast that never quite breaks — the commonest weather there is, and the
		/// one a system with no driver can never produce.
		/// </remarks>
		public static WeatherFrame Background(in Synoptic air)
		{
			var frame = new WeatherFrame();

			// Cloud: damp air clouds over, and a low clouds over harder — and then the formations
			// decide where in that sky the banks and the gaps are.
			float cover = Mathf.Clamp01(SynopticCloudCover(air) + air.Mesoscale * MesoscaleAmplitude);
			frame[WeatherChannel.CloudCover] = cover;
			frame[WeatherChannel.CloudDensity] = Mathf.Clamp01(0.25f + cover * 0.5f + Mathf.Max(0f, -air.Pressure) * 0.35f);
			// A damp low hangs its cloud base low; dry high air lifts it.
			frame[WeatherChannel.CloudBase] = Mathf.Clamp01(0.8f - air.Humidity * 0.4f + air.Pressure * 0.15f);

			// What falls, and from what. Measured over a simulated year (memory/tools/driverstats.py,
			// a port of this file): as first written, lightning fired 0.00% of the year — its gate
			// sat at an instability of 0.72 and instability never gets past 0.71 — hail was never
			// emitted at all, heavy rain fell under 1% of the time and thick fog never formed. The
			// weather was cloud and the odd drizzle. These gates are set from the field's measured
			// spread (humidity p50 0.50 / p90 0.67; instability p50 0.24 / p90 0.44) to give, at mid
			// latitudes: rain about a third of the time, steady rain a tenth, heavy 2-3%; thunder
			// 3-4% of the year and nearly all of it under towers in summer; hail about one hour in
			// two hundred; fog on 8-17% of the clock with real thick-fog mornings.
			//
			// Two sources. Frontal rain: damp air, in proportion to how unsettled it is. And the
			// convective shower — a tower standing in unsettled air rains on its own account, which
			// is what makes a summer afternoon's isolated downpour, and is where the thunder is.
			float wet = Mathf.Clamp01((air.Humidity + air.Mesoscale * 0.06f - 0.52f) / 0.22f);
			float convective = air.Tower * Mathf.Clamp01((air.Instability - 0.22f) / 0.30f);
			float precipitation = Mathf.Clamp01(
				wet * (0.3f + air.Instability * 0.7f) * Mathf.Lerp(0.6f, 1.6f, air.Tower)
				+ convective * 0.55f * Mathf.Clamp01((air.Humidity - 0.35f) / 0.2f));
			// Hail wants the strongest towers in properly unstable air: the updraught has to hold a
			// stone up long enough to grow it.
			float hail = Mathf.Clamp01((convective - 0.75f) / 0.25f) * Mathf.Clamp01((air.Instability - 0.4f) / 0.2f) * precipitation;
			frame[WeatherChannel.Precipitation] = precipitation;
			if (precipitation > 0f)
			{
				float hailShare = Mathf.Clamp01(hail / Mathf.Max(0.01f, precipitation));
				frame[WeatherChannel.DropSize] = Mathf.Clamp01(0.25f + air.Instability * 0.6f + hailShare * 0.3f);
				// Left as water here. The model retypes it for the temperature where it falls, which
				// is the one place that decision belongs; the hail is hail whatever the ground is.
				frame[WeatherChannel.RainWeight] = 1f - hailShare;
				frame[WeatherChannel.HailWeight] = hailShare;
			}

			frame[WeatherChannel.WindSpeed] = Mathf.Clamp01(air.Wind.magnitude / 30f);
			frame[WeatherChannel.WindGust] = Mathf.Clamp01(air.Instability * 0.6f + Mathf.Max(0f, -air.Pressure) * 0.3f);
			frame[WeatherChannel.WindHeading] = Mathf.Repeat(Mathf.Atan2(air.Wind.x, air.Wind.y) * Mathf.Rad2Deg, 360f);

			// Fog wants damp air that is standing still, near dawn — which is when the ground has
			// given up its heat and the air reaches its dew point without having to be lifted.
			// It must not also be asked for high pressure: humidity here is *anti*-correlated with
			// pressure by construction, so "humid and settled" is a condition that cannot occur, and
			// asking for it meant fog never formed once in a simulated week.
			// It builds through the night and burns off by mid-morning, so the night counts as well
			// as the dawn; and light rain does not clear a fog, it only thins it.
			float still = Mathf.Clamp01(1f - (air.Wind.magnitude - 4f) / 10f);
			float dawn = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(air.LocalTime01 - 0.25f + 0.5f, 1f) - 0.5f) * 5f);
			// Centred before dawn and gone by mid-morning: at 2.6 it reached from twenty past six in
			// the evening to ten to one in the afternoon, a night's fog still hanging on at lunchtime.
			float night = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(air.LocalTime01 - 0.15f + 0.5f, 1f) - 0.5f) * 3.7f);
			// The night is the sun's, not the clock's. Inside a polar circle the clock still goes
			// round but the sun may not: in the summer it never sets, the ground never gets its hours
			// of cooling, and there is no night for a fog to build through — and in the winter it
			// never rises, there is no morning to burn a fog off, and one that forms can sit for days.
			// By the hour alone the pole had a fog at four every "morning" of a sunlit summer, clearing
			// at ten on a winter's day that was pitch dark.
			float nightly = Mathf.Max(dawn, night * 0.7f);
			nightly = Mathf.Lerp(nightly, 0f, Mathf.Clamp01(air.PolarDay));
			nightly = Mathf.Lerp(nightly, 0.7f, Mathf.Clamp01(air.PolarNight));
			// By day a fog needs the air itself all but saturated — a front's drizzle, a sea fog
			// rolling in — since the sun has lifted the ground above the dew point. A floor of a fifth
			// in any damp air gave every humid noon a mist of drops; what a humid noon has is haze, and
			// the haze is the air's own (AirPhysics.HazeDistance), not drops.
			float saturated = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 0.97f, air.Humidity));
			frame[WeatherChannel.FogDensity] = Mathf.Clamp01((air.Humidity - 0.42f) / 0.3f) * Mathf.Clamp01(still * 1.3f)
				* (0.2f * saturated + nightly * 0.8f) * (1f - precipitation * 0.7f);

			// Lightning is the tower's: a strong convective column that is actually raining. Gated on
			// instability alone, at 0.72, it could not fire — the field never gets there.
			frame[WeatherChannel.LightningRate] = Mathf.Clamp01(convective * 1.25f - 0.25f) * Mathf.Clamp01(precipitation * 3f);
			frame[WeatherChannel.TemperatureOffset] = air.Temperature;
			frame[WeatherChannel.HumidityOffset] = (air.Humidity - 0.5f) * 0.4f;
			return frame;
		}
	}
}
