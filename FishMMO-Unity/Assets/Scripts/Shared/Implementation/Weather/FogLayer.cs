using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A fog as the thing it is: a layer of chilled, saturated air lying on the ground, with a top —
	/// how deep it lies and whether the wind or the morning sun has lifted it off the ground.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>How much fog there is</b> is the weather's fog channel, and <see cref="AirPhysics.FogExtinction"/>
	/// turns that into the light its drops take out of every metre. <b>Where it is</b> is this. A fog
	/// drawn as the same extinction at every height is not a fog: a mist, a fog and a dense fog come out
	/// as three shades of one flat wash, because what tells them apart to anyone standing in them is how
	/// deep they are — whether the sun and the sky still show above a mist, whether a fog has a bright
	/// top a few hundred metres up, whether the whole world has gone.
	/// </para>
	/// <para>
	/// <b>A radiation fog is the air the night has chilled.</b> The ground radiates to a clear sky all
	/// night, and the air resting on it gives up heat to it: about two fifths of the ground's loss is
	/// drawn out of the air, the rest comes up out of the soil. That cooling reaches up through the air
	/// until it has chilled a depth of it through its dew-point spread — <c>h = Q / (ρ·c_p·ΔT)</c> — and
	/// the fog is that chilled air. Damp air (a small spread) makes a deep fog; drier air a shallow one;
	/// a cloudy night, whose sky sends the ground's heat back, hardly any. The fog channel says how far
	/// through its night a fog has come — it rises with the humidity and toward dawn — so the depth
	/// follows it: a shallow mist a few metres to a few tens of metres deep at first, a fog of a hundred
	/// or two by dawn. Past the base of the clouds there is no more fog to make — that is the cloud — and
	/// the lid of a high caps both.
	/// </para>
	/// <para>
	/// <b>A wind shares the night's chill out, and it is gone or lifted.</b> A wind mixes the air near
	/// the ground up through the stable air above it until the stratification wins, where the bulk
	/// Richardson number reaches a quarter — a few tens of metres in a light breeze, hundreds in a fresh
	/// one (<see cref="MixedDepth"/>). While that is shallower than the air the night has chilled it
	/// only stirs the fog within itself, and the fog lies on the ground. Deeper, the same chill is
	/// shared through more air than it could bring to its dew point: a well-mixed layer's spread closes
	/// with height as the column's does, used up at the cloud base, so shared through <c>h</c> the chill
	/// that would have saturated a depth <c>d</c> of still air saturates the stirred air only above
	/// <c>base·(1 − d/h)</c>. That is a sheet of stratus from there to the top of the stirred air when it
	/// lies under it — the grey ceiling a breezy night leaves over damp ground — and nothing at all when
	/// it does not: a breeze over air that is damp but a good way from saturation keeps the night
	/// clear, which is why a radiation fog wants a calm. The water is the chill's, so it is conserved;
	/// the wind never makes a deeper fog out of the same night.
	/// </para>
	/// <para>
	/// <b>The morning burns it off from below.</b> The sun warms the ground and the ground warms the air
	/// on it, so a fog clears from the bottom: it lifts into a stratus sheet through the morning and is
	/// gone by the time the day's mixed air has risen through it. The day does not spread the fog's
	/// drops through that mixed layer. A humid afternoon is milky, but with the air's own haze — its dust
	/// and salt swollen by the damp (<see cref="AirPhysics.HazeDistance"/>), which the sky draws — and
	/// drawn as a kilometre of fog on top of it, it glowed round the sun like a mist.
	/// </para>
	/// <para>
	/// Worked out on both peers from the physics they share, like everything else in the frame; the
	/// depth goes out in <see cref="WeatherChannel.FogHeight"/> and the lift in
	/// <see cref="WeatherChannel.VolumetricFog"/>, so nothing new goes on the wire.
	/// </para>
	/// </remarks>
	public struct FogLayer
	{
		/// <summary>How far the fog's top stands above the ground under it, m.</summary>
		public float Depth;
		/// <summary>
		/// How far off the ground the wind or the morning has lifted it: 0 lying on the ground … 1 a
		/// sheet of stratus whose base is <see cref="LiftedBase"/> of the way up to its top.
		/// </summary>
		public float Lift;
		/// <summary>
		/// How much of the weather's fog the wind took back, 0..1, as a share of the fog channel: none
		/// (the default) for a fog that lies as the night made it; all of it when the wind has shared the
		/// night's chill through air it could not bring to its dew point; some, for a sheet thinner than
		/// a lifted layer is drawn (<see cref="Stirred"/>). <see cref="WriteTo"/> takes it out of the frame.
		/// </summary>
		public float MixedOut;

		/// <summary>The depth a <see cref="WeatherChannel.FogHeight"/> of 1 means, m.</summary>
		public const float ChannelMetres = 1000f;
		/// <summary>Where a fully lifted fog's base sits, as a share of its top.</summary>
		public const float LiftedBase = 0.6f;
		/// <summary>The shallowest a fog lies: a ground fog of a couple of metres.</summary>
		public const float MinimumDepth = 2f;
		/// <summary>The share of the ground's night-time loss drawn out of the air above it; the rest comes up out of the soil.</summary>
		public const float AirShareOfNightLoss = 0.4f;
		/// <summary>How rough open country is to the wind, m: the length in its logarithmic profile.</summary>
		public const float Roughness = 0.1f;
		/// <summary>Where the wind is reported, m: head height.</summary>
		public const float WindHeight = 2f;
		/// <summary>The bulk Richardson number below which a stable layer is turbulent through.</summary>
		public const float CriticalRichardson = 0.25f;
		/// <summary>Stefan–Boltzmann, W/(m²·K⁴).</summary>
		public const float StefanBoltzmann = 5.670374e-8f;

		/// <summary>The fog layer this air makes.</summary>
		/// <param name="air">The air here: its wind, and the hour it was sampled at.</param>
		/// <param name="column">The air's vertical structure: its dew point, stability, cloud base and lid.</param>
		/// <param name="planet">The world's air: its weight, its heat capacity, how long its nights are.</param>
		/// <param name="fog01">How much fog the weather has, 0..1: how far through its night it has come.</param>
		/// <param name="cloudCover">The low cloud over it, 0..1, which sends the ground's heat back.</param>
		public static FogLayer Of(in WeatherDriver.Synoptic air, in AirColumn column, in PlanetAir planet, float fog01, float cloudCover)
		{
			var layer = new FogLayer();
			if (!planet.HasAir)
			{
				return layer;
			}
			float fog = Mathf.Clamp01(fog01);
			float kelvin = Mathf.Max(20f, column.SurfaceKelvin);
			float gravity = Mathf.Max(0.05f, planet.Gravity);
			// How far the air must be chilled to condense: the inversion a fog lies under is at least that.
			float spread = Mathf.Max(1f, kelvin - column.DewPointKelvin);
			float heat = Mathf.Max(1e-3f, planet.SurfacePressure / (Mathf.Max(1f, planet.GasConstant) * kelvin) * planet.SpecificHeat);

			// The night: the depth of air a whole night's radiating can chill through its spread, and
			// the share of that the fog has reached so far.
			float chilled = AirShareOfNightLoss * NightLoss(kelvin, column.DewPointKelvin, planet.Condensate, cloudCover)
				* NightSeconds(planet.RotationRate) / (heat * spread);
			float radiation = chilled * fog;

			// Past the cloud base the chilled air is the cloud, not a fog under it; the lid caps both.
			float lid = Mathf.Min(column.Cap, column.Tropopause);
			float ceiling = Mathf.Max(MinimumDepth, Mathf.Min(Mathf.Max(column.Base, MinimumDepth), lid));

			// The wind: how deep it stirs the air through the stable air above it — never past the lid,
			// whose inversion stops the stirring as it stops everything else.
			float wind = air.Wind.magnitude;
			float stability = Mathf.Max(1e-4f, column.DryLapse - column.EnvironmentLapse);
			float stirred = Mathf.Min(MixedDepth(wind, spread, stability, kelvin, gravity), lid);
			// Shallower than the chilled air, it only stirs the fog within itself and the fog lies on the
			// ground. Deeper, it shares the chill through more air than it could saturate.
			float depth = radiation;
			float lift = 0f;
			if (stirred > radiation)
			{
				Stirred(radiation, stirred, column.Base, ceiling, out depth, out lift, out layer.MixedOut);
			}

			// A thick air never loses its haze (WeatherDriver.UnderAtmosphere), and a haze is the whole
			// mixed layer's, not a ground fog's.
			if (planet.AirRelative >= 2f)
			{
				depth = Mathf.Max(depth, Mathf.Min(column.Base, lid));
				lift = 0f;
				layer.MixedOut = 0f;
			}
			else if (fog <= 0f)
			{
				// No fog, no layer.
				return default;
			}
			else if (depth <= 0f)
			{
				// The wind has taken it all: none of the stirred air reached its dew point.
				return new FogLayer { MixedOut = 1f };
			}
			layer.Depth = Mathf.Clamp(depth, MinimumDepth, ceiling);

			// And the morning lifts it, from below, on its way to being burned off. The day does not
			// deepen it: the drops go, and what is left in the day's mixed air is the air's own haze.
			float day = Daytime(air.LocalTime01, air.PolarDay, air.PolarNight);
			float bySun = Mathf.Repeat(air.LocalTime01, 1f) < 0.5f ? 3.2f * day * (1f - day) * (1f - Mathf.Clamp01(air.PolarDay)) : 0f;
			layer.Lift = Mathf.Clamp01(Mathf.Max(lift, bySun));
			return layer;
		}

		/// <summary>
		/// The fog a wind makes of the night's chill when it stirs deeper than the chill has reached: a
		/// sheet of stratus at the top of the stirred air, or nothing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The heat the night has drawn out of the air would have brought <paramref name="chilledDepth"/>
		/// of still air to its dew point. Stirred through <paramref name="stirredDepth"/>, it cools all of
		/// that by the share <c>chilledDepth / stirredDepth</c> of its spread instead. Well mixed, the
		/// stirred air's temperature falls with height at the dry lapse while its dew point falls far more
		/// slowly — the same closing that puts the cloud base where it is — so what is left of its spread
		/// is used up at <c>base·(1 − chilledDepth / stirredDepth)</c>: saturated from there to its top,
		/// clear under it. At the depth the chill reached that is the ground, and the fog lying on it; a
		/// little deeper, a fog lifted off it; much deeper, over air a long way from saturation, a height
		/// the stirred air never reaches, and there is no fog at all.
		/// </para>
		/// <para>
		/// A sheet is drawn as a lifted layer, whose base stands at most <see cref="LiftedBase"/> of the
		/// way up. One thinner than that — the chill lowering the underside of the stirred air's own cloud
		/// a little, just under the cloud base — is drawn where it is, as thick as a lifted layer is, and
		/// its fog thinned to keep the light it takes: a fog's extinction goes as the 7/3 power of the
		/// channel (<see cref="AirPhysics.FogExtinction"/>).
		/// </para>
		/// </remarks>
		/// <param name="chilledDepth">The depth of still air the night's loss so far would have saturated, m.</param>
		/// <param name="stirredDepth">How deep the wind stirs, m.</param>
		/// <param name="cloudBase">Where the unchilled air condenses, m.</param>
		/// <param name="ceiling">The highest a fog stands: the cloud base, under the lid.</param>
		/// <param name="top">The layer's top, m: 0 for none.</param>
		/// <param name="lift">How far it has lifted (<see cref="Lift"/>).</param>
		/// <param name="mixedOut">How much of the fog channel the wind took back (<see cref="MixedOut"/>).</param>
		public static void Stirred(float chilledDepth, float stirredDepth, float cloudBase, float ceiling, out float top, out float lift, out float mixedOut)
		{
			float condenses = Mathf.Max(0f, cloudBase) * (1f - Mathf.Max(0f, chilledDepth) / Mathf.Max(1e-3f, stirredDepth));
			top = Mathf.Min(stirredDepth, ceiling);
			float sheet = top - condenses;
			if (sheet < MinimumDepth)
			{
				top = 0f;
				lift = 0f;
				mixedOut = 1f;
				return;
			}
			float highestBase = LiftedBase * top;
			lift = Mathf.Clamp01(condenses / Mathf.Max(1e-3f, highestBase));
			float drawn = top - highestBase;
			mixedOut = sheet < drawn ? 1f - Mathf.Pow(sheet / drawn, 3f / 7f) : 0f;
		}

		/// <summary>
		/// Writes the layer into a frame's fog channels — and takes out of its fog what the wind took,
		/// so nothing is left to draw a layer that did not form (<see cref="MixedOut"/>).
		/// </summary>
		/// <remarks>
		/// A fog the wind has mixed away must leave the fog channel as well as the layer: with no depth
		/// the layer cannot be drawn, and the pipeline's distance fog takes the drops back as an even wash
		/// over everything (<c>WeatherFogPresenter.UniformExtinction</c>) — and everything that asks the
		/// weather whether it is foggy would still be told so.
		/// </remarks>
		public void WriteTo(ref WeatherFrame frame)
		{
			frame[WeatherChannel.FogHeight] = Mathf.Clamp01(Depth / ChannelMetres);
			frame[WeatherChannel.VolumetricFog] = Mathf.Clamp01(Lift);
			if (MixedOut > 0f)
			{
				frame[WeatherChannel.FogDensity] *= 1f - Mathf.Clamp01(MixedOut);
			}
		}

		/// <summary>The layer a frame carries.</summary>
		public static FogLayer In(in WeatherFrame frame)
		{
			return new FogLayer
			{
				Depth = Mathf.Max(0f, frame[WeatherChannel.FogHeight]) * ChannelMetres,
				Lift = Mathf.Clamp01(frame[WeatherChannel.VolumetricFog]),
			};
		}

		/// <summary>
		/// What the ground loses to the sky at night, W/m²: its own glow, less what the air and the
		/// clouds glow back at it.
		/// </summary>
		/// <remarks>
		/// A clear sky glows back with the vapour in it, Brunt's <c>ε = 0.52 + 0.065·√e</c> (e in hPa) —
		/// about three quarters on an ordinary damp night, which leaves some eighty or ninety watts a
		/// square metre going out. A low deck of cloud is nearly a black body only a few kelvin colder
		/// than the ground and sends most of the rest back: an overcast night hardly cools, and makes
		/// no radiation fog to speak of.
		/// </remarks>
		public static float NightLoss(float kelvin, float dewPointKelvin, Condensate condensate, float cloudCover)
		{
			float vapour = AirPhysics.SaturationPressure(dewPointKelvin, condensate) * 0.01f;
			float clear = Mathf.Min(0.95f, 0.52f + 0.065f * Mathf.Sqrt(Mathf.Max(0f, vapour)));
			float sky = clear + (1f - clear) * 0.75f * Mathf.Clamp01(cloudCover);
			float t2 = kelvin * kelvin;
			return StefanBoltzmann * t2 * t2 * Mathf.Max(0f, 1f - sky);
		}

		/// <summary>
		/// How long a night lasts here, s: half a turn of the world, held between three hours and
		/// sixteen.
		/// </summary>
		/// <remarks>
		/// A longer night than that does not go on deepening a fog: by then the ground is cooling no
		/// faster than the air above it can feed it, and what deepens a fog further is the wind.
		/// </remarks>
		public static float NightSeconds(float rotationRate)
		{
			float turn = Mathf.Abs(rotationRate);
			float night = turn > 1e-9f ? Mathf.PI / turn : float.MaxValue;
			return Mathf.Clamp(night, 3f * 3600f, 16f * 3600f);
		}

		/// <summary>The wind at a height, from the wind at head height: the logarithmic profile over open country.</summary>
		public static float WindAt(float windAtHead, float height)
		{
			return windAtHead * Mathf.Log(Mathf.Max(height, WindHeight) / Roughness) / Mathf.Log(WindHeight / Roughness);
		}

		/// <summary>
		/// How deep a wind stirs chilled air through the stable air above it, m: where the bulk
		/// Richardson number <c>(g/T)·(Δθ·h + S·h²) / U(h)²</c> reaches its critical quarter.
		/// </summary>
		/// <param name="inversion">The chilled layer's inversion, K.</param>
		/// <param name="stability">How much more slowly the air above cools with height than a dry parcel would, K/m.</param>
		public static float MixedDepth(float windAtHead, float inversion, float stability, float kelvin, float gravity)
		{
			if (windAtHead <= 0.05f)
			{
				return 0f;
			}
			float s = Mathf.Max(1e-4f, stability);
			float b = Mathf.Max(0f, inversion);
			float depth = 50f;
			// The wind that does the stirring is the wind at the top of what it stirs, which depends on
			// the depth: a few rounds settle it.
			for (int i = 0; i < 4; i++)
			{
				float u = WindAt(windAtHead, depth);
				float c = CriticalRichardson * u * u * kelvin / gravity;
				depth = (-b + Mathf.Sqrt(b * b + 4f * s * c)) / (2f * s);
			}
			return Mathf.Max(0f, depth);
		}

		/// <summary>
		/// How far the sun's warmth has stirred the lower air, 0 through the night … 1 through the
		/// afternoon: rising through the morning after sunrise, falling again around sunset as the
		/// ground starts to cool.
		/// </summary>
		public static float Daytime(float localTime01, float polarDay, float polarNight)
		{
			float t = Mathf.Repeat(localTime01, 1f);
			float day = t < 0.5f
				? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.27f, 0.42f, t))
				: Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 0.72f, t));
			// A sun that never sets warms the ground at every hour, if weakly at midnight; one that
			// never rises, at none.
			day = Mathf.Lerp(day, 0.5f, Mathf.Clamp01(polarDay));
			return day * (1f - Mathf.Clamp01(polarNight));
		}
	}
}
