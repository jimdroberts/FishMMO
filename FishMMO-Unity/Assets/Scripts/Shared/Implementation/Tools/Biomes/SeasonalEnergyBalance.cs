using System;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A body's surface temperature by latitude through its year: a seasonal, zonally averaged energy balance with a
	/// land and an ocean column in every latitude band, solved once per body. The one source every temperature on a
	/// world is read from — the globe, the scene cut, the biomes, the ice, the water and the running seasons.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a model and not a formula (2026-10-15).</b> Temperature was the sun's height at noon and nothing else: no
	/// day length, so a polar summer under a sun that never sets came out 15–24 K too cold; no heat capacity, so every
	/// season peaked on the solstice and the subtropics swung twice as far as Earth's do; no land or sea, so a coast and a
	/// continent's heart at one latitude had one climate. Each of those is a physical fact a few lines of energy balance
	/// get right, and fitting a formula to them one at a time was how the old model had grown three disagreeing season
	/// terms.
	/// </para>
	/// <para>
	/// <b>The model</b> (North &amp; Coakley 1979; the two-column land/ocean form of North, Mengel &amp; Short 1983). In each
	/// equal-area band, for each column:
	/// <c>C·dT/dt = Q·a(x, T) − (A + B·T) + D·∂ₓ[(1 − x²)∂ₓT̄] + ν·(T_other − T) / f_own</c>.
	/// Q is the day's mean starlight at that latitude (day length and the sun's height together, from the body's own
	/// declination and distance), a the absorbed fraction (clouds and the low sun at high latitude in the ice-free value,
	/// bright snow and ice below freezing), A + B·T the heat radiated away, D the transport poleward by the air and the
	/// sea, ν the exchange between the land and the sea of one band. C is the column's heat capacity: a land column is a
	/// few metres of ground and the air over it, an ocean column its mixed layer, which drops to sea ice's below freezing.
	/// </para>
	/// <para>
	/// <b>Calibrated on Earth</b> (tilt 23.44°, its land by latitude): zonal annual means within a few kelvin, the
	/// seasonal swing at every latitude within about a kelvin, the season lagging the sun by about a month over land and
	/// two over the sea. A is not a constant: it is solved so the world's area-mean temperature is exactly its absolute
	/// mean (<see cref="ClimateModel.MeanSurfaceKelvin"/>), so the greenhouse, the distance and the brightness of its suns
	/// all keep coming from the one place they always have.
	/// </para>
	/// <para>
	/// <b>A year is the body's own year.</b> Heat capacities are per year, not per second: a world's year in the game is
	/// far shorter than its physical one, and measuring heat in real seconds would let its seasons come and go before the
	/// sea noticed them.
	/// </para>
	/// <para>Pure and deterministic: arrays in, arrays out, the same on every machine.</para>
	/// </remarks>
	public static class SeasonalEnergyBalance
	{
		/// <summary>Equal-area latitude bands the model is solved in.</summary>
		public const int Bands = 90;

		/// <summary>Points round the year the result is kept at.</summary>
		public const int Phases = 48;

		/// <summary>Steps through one year: a whole number of steps per kept phase.</summary>
		public const int StepsPerYear = Phases * 8;

		/// <summary>Years run before the kept one, from a cold start, until the seasons repeat.</summary>
		public const int SpinUpYears = 16;

		/// <summary>Most secant steps taken on A toward the world's mean.</summary>
		public const int MeanCorrections = 8;

		/// <summary>Most halvings of the bracket on A after the secant steps.</summary>
		public const int BisectionSteps = 12;

		/// <summary>How close the solved mean must come to the world's, K.</summary>
		public const double MeanTolerance = 0.05;

		/// <summary>Earth's solar constant, W m⁻²: the starlight at the reference distance from a star of unit luminosity.</summary>
		public const double SolarConstant = 1361.0;

		/// <summary>Radiated heat per kelvin, W m⁻² K⁻¹ (Graves, Lee &amp; North 1993's fit to the outgoing longwave).</summary>
		public const double B = 2.09;

		/// <summary>Ice-free absorbed fraction: its value at the equator and its fall toward the poles (P₂, P₄ terms).</summary>
		public const double CoalbedoMean = 0.697, CoalbedoP2 = -0.200, CoalbedoP4 = -0.100;

		/// <summary>Absorbed fraction over snow and ice at high latitude.</summary>
		public const double IceCoalbedo = 0.34;

		/// <summary>
		/// The most snow and ice may brighten ground over its bare value. Ice at low latitude lies under the deep tropical
		/// cloud and is bare sea ice rather than fresh snow, so it is far less bright against its surroundings than a polar
		/// cap is against the dark polar sea; at full contrast everywhere the model froze a whole world over for one kelvin
		/// of mean temperature (the ice-albedo runaway of Budyko–Sellers balances), and Arthis, at 280 K, fell into it.
		/// </summary>
		public const double MaxIceContrast = 0.10;

		/// <summary>The span, °C, over which ground goes from bare to snow-bright: complete below the first, gone above the second.</summary>
		public const double SnowColdC = -25.0, SnowWarmC = -2.0;

		/// <summary>Heat capacity of a land column, W yr m⁻² K⁻¹: the seasonally active ground and the air over it.</summary>
		public const double LandCapacity = 0.50;

		/// <summary>Heat capacity of an ocean column, W yr m⁻² K⁻¹: about a 40 m mixed layer.</summary>
		public const double OceanCapacity = 5.0;

		/// <summary>Heat capacity of an ocean column under sea ice, W yr m⁻² K⁻¹: the ice insulates the water below it.</summary>
		public const double SeaIceCapacity = 0.60;

		/// <summary>The sea surface's span, °C, from open water to a full ice cover.</summary>
		public const double SeaIceWarmC = -1.8, SeaIceColdC = -4.8;

		/// <summary>Exchange between the land and the sea of one band, W m⁻² K⁻¹.</summary>
		public const double Exchange = 2.0;

		/// <summary>Poleward transport on a world with an Earth-like air, W m⁻² K⁻¹.</summary>
		public const double EarthDiffusion = 1.0;

		/// <summary>
		/// Latent heat held by a metre of water as ice, W yr m⁻² (334 kJ kg⁻¹ × 1000 kg m⁻³ over a year's seconds): the unit
		/// a snowpack is kept in, so melting it is paid for in the same currency as warming the ground.
		/// </summary>
		public const double LatentPerMetre = 10.6;

		/// <summary>Snowfall on an Earth-like world while it is below freezing, metres of water a year.</summary>
		public const double EarthSnowfallMetres = 0.35;

		/// <summary>Snowpack, metres of water, that covers the ground completely; less covers it in proportion.</summary>
		public const double FullCoverMetres = 0.05;

		/// <summary>Most snowpack a column keeps, metres of water: a standing ice sheet, beyond which the ice flows away.</summary>
		public const double MaximumPackMetres = 4.0;

		/// <summary>
		/// How much weaker the poleward transport is at the pole than at the equator: D(x) = D·(1 − share·x⁴). The storm
		/// tracks that carry heat poleward die out over the polar caps, whose air is a cold, stable dome; at one strength
		/// everywhere the model's poles ran 5–20 K warm, the classic fault of a constant-diffusion balance.
		/// </summary>
		public const double PolarTransportDrop = 0.8;

		/// <summary>Smallest share of a band either column is weighted by, so a band of all sea still has a land column to read.</summary>
		public const double MinimumShare = 0.02;

		/// <summary>What the model is solved for.</summary>
		public sealed class Inputs
		{
			/// <summary>The sun's declination at each of <see cref="StepsPerYear"/> steps of the year, radians.</summary>
			public double[] Declination;

			/// <summary>Starlight arriving at the body at each step, W m⁻² (<see cref="SolarConstant"/> × insolation).</summary>
			public double[] Flux;

			/// <summary>Share of each of the <see cref="Bands"/> bands that is land, 0..1, south to north.</summary>
			public double[] LandFraction;

			/// <summary>The world's area-mean surface temperature over the year, K: what A is solved to.</summary>
			public double TargetMeanKelvin;

			/// <summary>Poleward transport, W m⁻² K⁻¹ (<see cref="DiffusionFor"/>).</summary>
			public double Diffusion = EarthDiffusion;

			/// <summary>Heat capacity added to both columns by the air itself, W yr m⁻² K⁻¹: nothing for an Earth-like air, much for a thick one.</summary>
			public double AirCapacity;

			/// <summary>Whether the ocean column is a sea that freezes (water), or the same ground as the land (a dry world).</summary>
			public bool HasSea = true;

			/// <summary>Snow that falls while a column is below freezing, metres of water a year: none without water or air.</summary>
			public double SnowfallMetres = EarthSnowfallMetres;
		}

		/// <summary>The year at every degree of latitude: K, [latitude + 90, phase], 181 × <see cref="Phases"/>.</summary>
		public sealed class Result
		{
			/// <summary>The land column, K.</summary>
			public readonly float[] Land = new float[181 * Phases];

			/// <summary>The ocean column, K (the sea's surface, or the air over its ice).</summary>
			public readonly float[] Ocean = new float[181 * Phases];

			/// <summary>The solved radiation constant, W m⁻².</summary>
			public double A;

			/// <summary>The area-mean temperature of the result, K: the world's own.</summary>
			public double MeanKelvin;

			/// <summary>
			/// What was shifted onto the whole table to make its mean the world's, K: near zero, except where the world's mean
			/// sits in the gap at the edge of the ice's reach that no balanced climate has.
			/// </summary>
			public double Residual;

			/// <summary>Index into the tables.</summary>
			public static int Index(int latitude, int phase) => (latitude + 90) * Phases + phase;
		}

		/// <summary>Poleward transport for an air, W m⁻² K⁻¹: none to carry heat without air, a thick air evens a world out.</summary>
		public static double DiffusionFor(FishMMO.Shared.Celestial.AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case FishMMO.Shared.Celestial.AtmosphereKind.None: return 0.02;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thin: return 0.25;
				case FishMMO.Shared.Celestial.AtmosphereKind.Thick: return 6.0;
				default: return EarthDiffusion;
			}
		}

		/// <summary>Heat capacity an air adds to both columns, W yr m⁻² K⁻¹: a thick air holds the year's heat as a sea does.</summary>
		public static double AirCapacityFor(FishMMO.Shared.Celestial.AtmosphereKind atmosphere)
		{
			return atmosphere == FishMMO.Shared.Celestial.AtmosphereKind.Thick ? 8.0 : 0.0;
		}

		/// <summary>The day's mean starlight at a latitude, W m⁻², for a declination and the starlight overhead.</summary>
		/// <remarks>The standard daily insolation (e.g. Berger 1978): day length and the sun's height together, zero in polar night, more than the equator's at a pole in midsummer.</remarks>
		public static double DailyInsolation(double flux, double latitudeRadians, double declination)
		{
			double sinPhi = Math.Sin(latitudeRadians), cosPhi = Math.Cos(latitudeRadians);
			double sinDelta = Math.Sin(declination), cosDelta = Math.Cos(declination);
			double cosH0 = cosPhi * cosDelta > 1e-12 ? -(sinPhi * sinDelta) / (cosPhi * cosDelta) : (sinPhi * sinDelta > 0.0 ? -2.0 : 2.0);
			double h0 = cosH0 <= -1.0 ? Math.PI : cosH0 >= 1.0 ? 0.0 : Math.Acos(cosH0);
			return Math.Max(0.0, flux / Math.PI * (h0 * sinPhi * sinDelta + cosPhi * cosDelta * Math.Sin(h0)));
		}

		/// <summary>The absorbed fraction at a band and temperature.</summary>
		public static double Coalbedo(double x, double celsius)
		{
			double bare = BareCoalbedo(x);
			double ice = Math.Max(IceCoalbedo, bare - MaxIceContrast);
			return ice + (bare - ice) * Smooth(SnowColdC, SnowWarmC, celsius);
		}

		/// <summary>The ice-free absorbed fraction at a band.</summary>
		public static double BareCoalbedo(double x)
		{
			double p2 = 0.5 * (3.0 * x * x - 1.0);
			double p4 = (35.0 * x * x * x * x - 30.0 * x * x + 3.0) / 8.0;
			return CoalbedoMean + CoalbedoP2 * p2 + CoalbedoP4 * p4;
		}

		/// <summary>Solves the model.</summary>
		public static Result Solve(Inputs inputs)
		{
			if (inputs == null || inputs.Declination == null || inputs.Flux == null || inputs.LandFraction == null
				|| inputs.Declination.Length != StepsPerYear || inputs.Flux.Length != StepsPerYear || inputs.LandFraction.Length != Bands)
			{
				throw new ArgumentException("SeasonalEnergyBalance needs a declination and a flux per step and a land fraction per band.");
			}

			const double dx = 2.0 / Bands;
			const double dt = 1.0 / StepsPerYear;
			var x = new double[Bands];
			var landShare = new double[Bands];
			var seaShare = new double[Bands];
			for (int i = 0; i < Bands; i++)
			{
				x[i] = -1.0 + (i + 0.5) * dx;
				double landHere = inputs.HasSea ? Clamp01(inputs.LandFraction[i]) : 1.0;
				landShare[i] = Math.Max(MinimumShare, landHere);
				seaShare[i] = Math.Max(MinimumShare, 1.0 - landHere);
				double total = landShare[i] + seaShare[i];
				landShare[i] /= total;
				seaShare[i] /= total;
			}
			// (1 − x²) at the band edges, for the diffusion.
			var edge = new double[Bands + 1];
			for (int i = 0; i <= Bands; i++)
			{
				double xe = -1.0 + i * dx;
				edge[i] = (1.0 - xe * xe) * (1.0 - PolarTransportDrop * xe * xe * xe * xe);
			}

			double target = inputs.TargetMeanKelvin - 273.15;
			var land = new double[Bands];
			var sea = new double[Bands];
			var landPack = new double[Bands];
			var seaPack = new double[Bands];
			var record = new double[2, Bands, Phases];
			var work = new Workspace(Bands);

			/* A is found by the secant method on the year's mean, each trial from the same warm start.
			 *
			 * A world near the freezing edge has two climates for one A — open and green, or iced over and bright enough
			 * to stay that way — and a correction that carried each trial's state into the next walked Arthis into the
			 * iced one and could not climb back out: it settled 14 K below its own mean. Starting every trial warm keeps
			 * the solution on the branch the world's mean temperature says it is on. */
			// Every trial's A and mean error, kept as a bracket: the mean falls as A rises.
			double bestA = 0.0, bestF = double.PositiveInfinity;
			double below = double.NegativeInfinity, above = double.PositiveInfinity;
			double TryA(double trial)
			{
				double error = Trial(inputs, trial, target, x, landShare, seaShare, edge, dx, dt, land, sea, landPack, seaPack, record, work) - target;
				if (Math.Abs(error) < Math.Abs(bestF))
				{
					bestA = trial;
					bestF = error;
				}
				if (error > 0.0)
				{
					below = Math.Max(below, trial);
				}
				else
				{
					above = Math.Min(above, trial);
				}
				return error;
			}

			double a0 = InitialA(inputs, target);
			double f0 = TryA(a0);
			if (Math.Abs(f0) > MeanTolerance)
			{
				double a1 = a0 + B * f0;
				double f1 = TryA(a1);
				for (int i = 0; i < MeanCorrections && Math.Abs(f1) > MeanTolerance; i++)
				{
					double slope = (f1 - f0) / (a1 - a0);
					// A flat or wrong-signed slope (a jump between branches) falls back to the radiative one.
					double next = slope < -1e-6 ? a1 - f1 / slope : a1 + B * f1;
					if (!double.IsInfinity(below) && !double.IsInfinity(above) && (next <= below || next >= above))
					{
						next = 0.5 * (below + above);
					}
					a0 = a1;
					f0 = f1;
					a1 = next;
					f1 = TryA(a1);
				}
				/* Still short: bisect the bracket. Near the edge of the ice's reach a world can have no climate with exactly
				 * its mean — the cap either holds a little further or gives way — and bisection finds the A at that edge. */
				for (int i = 0; i < BisectionSteps && Math.Abs(bestF) > MeanTolerance && !double.IsInfinity(below) && !double.IsInfinity(above); i++)
				{
					TryA(0.5 * (below + above));
				}
			}
			double a = bestA;
			// The best trial's year, again, to keep (the last trial run may not have been the best).
			double reached = Trial(inputs, a, target, x, landShare, seaShare, edge, dx, dt, land, sea, landPack, seaPack, record, work);
			/* What is left is shifted out evenly, so the world's mean is always exactly its own: the global model is the
			 * authority on how warm the world is, this one only on how that warmth is spread over its latitudes and year. */
			double shift = target - reached;
			for (int i = 0; i < Bands; i++)
			{
				for (int p = 0; p < Phases; p++)
				{
					record[0, i, p] += shift;
					record[1, i, p] += shift;
				}
			}

			var result = new Result { A = a, Residual = shift };
			double sum = 0.0;
			for (int i = 0; i < Bands; i++)
			{
				for (int p = 0; p < Phases; p++)
				{
					sum += landShare[i] * record[0, i, p] + seaShare[i] * record[1, i, p];
				}
			}
			result.MeanKelvin = sum / (Bands * Phases) + 273.15;

			// To whole degrees of latitude, linearly between band centres (held flat beyond the outermost).
			for (int latitude = -90; latitude <= 90; latitude++)
			{
				double xs = Math.Sin(latitude * Math.PI / 180.0);
				double u = (xs + 1.0) / dx - 0.5;
				int i0 = (int)Math.Floor(u);
				double frac = u - i0;
				if (i0 < 0)
				{
					i0 = 0;
					frac = 0.0;
				}
				else if (i0 >= Bands - 1)
				{
					i0 = Bands - 2;
					frac = 1.0;
				}
				for (int p = 0; p < Phases; p++)
				{
					int k = Result.Index(latitude, p);
					result.Land[k] = (float)(record[0, i0, p] + (record[0, i0 + 1, p] - record[0, i0, p]) * frac + 273.15);
					result.Ocean[k] = (float)(record[1, i0, p] + (record[1, i0 + 1, p] - record[1, i0, p]) * frac + 273.15);
				}
			}
			return result;
		}

		private sealed class Workspace
		{
			public readonly double[] Mean, Capacity, Lower, Diagonal, Upper, Right, Solved;

			public Workspace(int n)
			{
				Mean = new double[n];
				Capacity = new double[n];
				Lower = new double[n];
				Diagonal = new double[n];
				Upper = new double[n];
				Right = new double[n];
				Solved = new double[n];
			}
		}

		/// <summary>A first A: what radiates away the absorbed mean starlight at the world's mean temperature.</summary>
		private static double InitialA(Inputs inputs, double targetC)
		{
			double flux = 0.0;
			foreach (double v in inputs.Flux)
			{
				flux += v;
			}
			flux /= inputs.Flux.Length;
			return CoalbedoMean * flux / 4.0 - B * targetC;
		}

		/// <summary>One trial A from the warm start: <see cref="SpinUpYears"/> years, the last kept. Returns its mean, °C.</summary>
		private static double Trial(Inputs inputs, double a, double targetC, double[] x, double[] landShare, double[] seaShare, double[] edge,
			double dx, double dt, double[] land, double[] sea, double[] landPack, double[] seaPack, double[,,] record, Workspace w)
		{
			for (int i = 0; i < x.Length; i++)
			{
				// Warm at the equator and cool at the poles about the world's mean, with no snow yet lying.
				land[i] = sea[i] = targetC + 15.0 * (1.0 - 3.0 * x[i] * x[i]) / 2.0;
				landPack[i] = seaPack[i] = 0.0;
			}
			return Run(inputs, a, x, landShare, seaShare, edge, dx, dt, land, sea, landPack, seaPack, SpinUpYears, record, w);
		}

		/// <summary>Runs whole years from the state given (left at the end of the last), keeping the last year at each phase. Returns its area mean, °C.</summary>
		private static double Run(Inputs inputs, double a, double[] x, double[] landShare, double[] seaShare, double[] edge, double dx, double dt,
			double[] land, double[] sea, double[] landPack, double[] seaPack, int years, double[,,] record, Workspace w)
		{
			int bands = x.Length;
			double air = Math.Max(0.0, inputs.AirCapacity);
			double d = Math.Max(0.0, inputs.Diffusion);
			double snowfall = Math.Max(0.0, inputs.SnowfallMetres);
			double sum = 0.0;
			int stride = StepsPerYear / Phases;
			var cl = new double[bands];
			var co = new double[bands];
			for (int year = 0; year < years; year++)
			{
				bool last = year == years - 1;
				for (int step = 0; step < StepsPerYear; step++)
				{
					double declination = inputs.Declination[step];
					double flux = inputs.Flux[step];
					for (int i = 0; i < bands; i++)
					{
						double phi = Math.Asin(x[i]);
						double q = DailyInsolation(flux, phi, declination);
						cl[i] = LandCapacity + air;
						co[i] = (inputs.HasSea ? SeaCapacity(sea[i], seaPack[i]) : LandCapacity) + air;
						double absorbedLand = q * Covered(x[i], Coalbedo(x[i], land[i]), landPack[i]);
						double absorbedSea = q * (inputs.HasSea ? SeaCoalbedo(x[i], sea[i], seaPack[i]) : Covered(x[i], Coalbedo(x[i], sea[i]), seaPack[i]));
						// Radiation implicit in its B term, the land–sea exchange implicit as a pair.
						double rl = cl[i] / dt * land[i] + absorbedLand - a;
						double ro = co[i] / dt * sea[i] + absorbedSea - a;
						double kl = Exchange / landShare[i], ko = Exchange / seaShare[i];
						double m11 = cl[i] / dt + B + kl, m12 = -kl;
						double m21 = -ko, m22 = co[i] / dt + B + ko;
						double det = m11 * m22 - m12 * m21;
						land[i] = (rl * m22 - m12 * ro) / det;
						sea[i] = (m11 * ro - m21 * rl) / det;
						// The band's mean, and the capacity it answers transport with (each column takes the same flux).
						w.Mean[i] = landShare[i] * land[i] + seaShare[i] * sea[i];
						w.Capacity[i] = 1.0 / (landShare[i] / cl[i] + seaShare[i] / co[i]);
					}
					if (d > 0.0)
					{
						// Implicit diffusion of the band means: C̃ (T' − T)/dt = D/dx² [e₊ (T'₊ − T') − e₋ (T' − T'₋)].
						double k = d / (dx * dx);
						for (int i = 0; i < bands; i++)
						{
							double lower = k * edge[i], upper = k * edge[i + 1];
							w.Lower[i] = -lower;
							w.Upper[i] = -upper;
							w.Diagonal[i] = w.Capacity[i] / dt + lower + upper;
							w.Right[i] = w.Capacity[i] / dt * w.Mean[i];
						}
						Tridiagonal(w.Lower, w.Diagonal, w.Upper, w.Right, w.Solved, bands);
						for (int i = 0; i < bands; i++)
						{
							// The same flux into both columns, so the band gains exactly what the transport brought.
							double heat = w.Capacity[i] * (w.Solved[i] - w.Mean[i]);
							land[i] += heat / cl[i];
							sea[i] += heat / co[i];
						}
					}
					for (int i = 0; i < bands; i++)
					{
						Pack(ref land[i], ref landPack[i], cl[i], 0.0, snowfall, dt);
						Pack(ref sea[i], ref seaPack[i], co[i], inputs.HasSea ? SeaIceWarmC : 0.0, snowfall, dt);
					}
					if (last && (step + 1) % stride == 0)
					{
						int p = (step + 1) / stride - 1;
						for (int i = 0; i < bands; i++)
						{
							record[0, i, p] = land[i];
							record[1, i, p] = sea[i];
							sum += landShare[i] * land[i] + seaShare[i] * sea[i];
						}
					}
				}
			}
			return sum / (bands * Phases);
		}

		/// <summary>
		/// A column's snow (on the sea, its ice) over one step: it gathers while the column is below its melting point, and
		/// while any lies the column cannot warm past that point — the heat goes into melting it first.
		/// </summary>
		/// <remarks>
		/// What holds a polar summer near freezing and keeps an ice sheet bright through it. Without it the land at the
		/// pole warmed to +9 °C under the midsummer sun and the Antarctic plateau sat 20 K above its real temperature.
		/// </remarks>
		private static void Pack(ref double celsius, ref double pack, double capacity, double meltC, double snowfall, double dt)
		{
			if (celsius < meltC)
			{
				pack = Math.Min(MaximumPackMetres, pack + snowfall * dt);
				return;
			}
			if (pack <= 0.0)
			{
				return;
			}
			double heat = capacity * (celsius - meltC);
			double melt = Math.Min(pack, heat / LatentPerMetre);
			pack -= melt;
			celsius -= melt * LatentPerMetre / capacity;
		}

		/// <summary>An absorbed fraction under a snowpack: the snow's brightness over the share it covers.</summary>
		private static double Covered(double x, double coalbedo, double pack)
		{
			double cover = Clamp01(pack / FullCoverMetres);
			double ice = Math.Max(IceCoalbedo, BareCoalbedo(x) - MaxIceContrast);
			return Math.Min(coalbedo, coalbedo + (ice - coalbedo) * cover);
		}

		/// <summary>The sea's absorbed fraction: open water dark, its ice bright as it forms and while it lasts.</summary>
		private static double SeaCoalbedo(double x, double celsius, double pack)
		{
			double open = BareCoalbedo(x);
			double ice = Math.Max(Smooth(SeaIceWarmC, SeaIceColdC, celsius), Clamp01(pack / FullCoverMetres));
			return open + (Math.Max(IceCoalbedo, open - MaxIceContrast) - open) * ice;
		}

		/// <summary>The ocean column's heat capacity: the mixed layer's while open, the ice's once frozen over.</summary>
		private static double SeaCapacity(double celsius, double pack)
		{
			double ice = Math.Max(Smooth(SeaIceWarmC, SeaIceColdC, celsius), Clamp01(pack / FullCoverMetres));
			return OceanCapacity + (SeaIceCapacity - OceanCapacity) * ice;
		}

		private static void Tridiagonal(double[] lower, double[] diagonal, double[] upper, double[] right, double[] solved, int n)
		{
			var c = new double[n];
			var dd = new double[n];
			c[0] = upper[0] / diagonal[0];
			dd[0] = right[0] / diagonal[0];
			for (int i = 1; i < n; i++)
			{
				double m = diagonal[i] - lower[i] * c[i - 1];
				c[i] = i < n - 1 ? upper[i] / m : 0.0;
				dd[i] = (right[i] - lower[i] * dd[i - 1]) / m;
			}
			solved[n - 1] = dd[n - 1];
			for (int i = n - 2; i >= 0; i--)
			{
				solved[i] = dd[i] - c[i] * solved[i + 1];
			}
		}

		private static double Smooth(double edge0, double edge1, double value)
		{
			double t = Clamp01((value - edge0) / (edge1 - edge0));
			return t * t * (3.0 - 2.0 * t);
		}

		private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;

		private static double Mean(double[] values)
		{
			double s = 0.0;
			foreach (double v in values)
			{
				s += v;
			}
			return values.Length > 0 ? s / values.Length : 0.0;
		}
	}
}
