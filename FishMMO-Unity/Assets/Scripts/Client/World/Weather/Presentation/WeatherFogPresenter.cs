using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>Turns the weather's fog and what is falling into the fog laid over the region's.</summary>
	/// <remarks>
	/// <para>
	/// Physical: the fog is how much light the drops in the air and whatever is falling through it
	/// take out per metre (<see cref="Extinction"/>), and light through it falls off as Beer and
	/// Lambert say it does — exponentially with distance. A dense fog is 0.3 g of water a cubic metre
	/// and about 60 m of visibility; a downpour a kilometre; a blizzard or a haboob far less. There
	/// is no floor on how far a player can see and no ceiling on how thick the air can get: what the
	/// air holds decides it.
	/// </para>
	/// <para>
	/// Two things, drawn two ways. What is falling fills the air evenly from the cloud base down, and
	/// goes to the pipeline's own distance fog. The fog's drops lie in a layer on the ground with a top
	/// (<see cref="FogLayer"/>) — cloud whose base is the ground — which the cloud march walks like any
	/// other cloud (<see cref="FogLayerView"/>); the distance fog carries them only where nothing draws
	/// that layer (<see cref="UniformExtinction"/>). Nor does the sky's horizon melt into them
	/// (<see cref="UniformAmount"/>): the march draws the fog along the sky's rays as well, to where the
	/// world curves out from under it, and a band painted on the sky dome on top of that was a second
	/// fog at the horizon, flat and the same in a mist as in a dense fog.
	/// </para>
	/// <para>
	/// The colour is the light the air scatters. Drops scatter every colour alike, so a fog is white
	/// lit by whatever lights it — which the sky hands over as the air's own hue; ash and sand are
	/// what they are made of, and keep their own.
	/// </para>
	/// </remarks>
	public static class WeatherFogPresenter
	{
		/// <summary>What the drops of a fog and a haze of falling stuff are, lit by a white light.</summary>
		private static readonly Color Droplets = new Color(0.72f, 0.72f, 0.72f, 1f);

		/// <summary>How much light the weather takes out of the view per metre: the fog's drops plus whatever is falling.</summary>
		public static float Extinction(in WeatherFrame frame)
		{
			return AirPhysics.FogExtinction(frame[WeatherChannel.FogDensity]) + FallingExtinction(frame);
		}

		/// <summary>How much light what is falling takes out of the view per metre: rain, snow, hail, ash, sand.</summary>
		public static float FallingExtinction(in WeatherFrame frame)
		{
			return AirPhysics.PrecipitationExtinction(frame[WeatherChannel.Precipitation],
				frame[WeatherChannel.RainWeight], frame[WeatherChannel.SnowWeight], frame[WeatherChannel.HailWeight],
				frame[WeatherChannel.AshWeight], frame[WeatherChannel.SandWeight]);
		}

		/// <summary>
		/// The share of the weather's extinction that is the same at every height, and so belongs in the
		/// pipeline's own distance fog: what is falling — and the fog's drops as well, when nothing is
		/// drawing their layer.
		/// </summary>
		/// <remarks>
		/// <para>
		/// What falls, falls through the whole air from the cloud base down, and thins the view the same
		/// at head height as on a hilltop: an even extinction is the truth of it, and the pipeline's
		/// exponential fog draws exactly that on everything, transparent or not.
		/// </para>
		/// <para>
		/// A fog is not. It is a layer on the ground with a top (<see cref="FogLayer"/>), and where that
		/// layer is drawn — by the cloud march, or where it does not run by the froxel volume and the
		/// analytic pass (<see cref="FogLayerView.Drawn"/>) — the distance fog must leave it out, or the
		/// same drops would take their light twice: once in the layer, once again everywhere as a flat
		/// wash, which is exactly the wash that made a mist, a fog and a dense fog look the same. Where
		/// nothing draws the layer, the distance fog is all there is, and the drops go back in, so the
		/// view is never clearer than the physics says.
		/// </para>
		/// </remarks>
		/// <param name="layerDrawn">Whether anything is drawing the fog's layer (<see cref="FogLayerView.Drawn"/>).</param>
		public static float UniformExtinction(in WeatherFrame frame, bool layerDrawn)
		{
			if (!SkySystem.DrawFog)
			{
				return 0f;
			}
			float falling = FallingExtinction(frame);
			if (layerDrawn && FogLayerView.Of(frame).Visible)
			{
				return falling;
			}
			return falling + AirPhysics.FogExtinction(frame[WeatherChannel.FogDensity]);
		}

		/// <summary>How strongly the weather fogs the view, 0..1: the contrast it takes out over 250 m.</summary>
		public static float Amount(in WeatherFrame frame)
		{
			return 1f - Mathf.Exp(-Extinction(frame) * 250f);
		}

		/// <summary>
		/// How strongly the pipeline's distance fog fogs the view, 0..1: the contrast its share of the
		/// weather (<see cref="UniformExtinction"/>) takes out over 250 m. What the distance fog's colour
		/// and the sky's horizon blend by — never the fog the layer's passes draw themselves.
		/// </summary>
		public static float UniformAmount(in WeatherFrame frame, bool layerDrawn)
		{
			return 1f - Mathf.Exp(-UniformExtinction(frame, layerDrawn) * 250f);
		}

		/// <summary>The colour in this world's air, when there is a sky to say what that is.</summary>
		private static Color Air(Color colour)
		{
			SkySystem sky = SkySystem.Instance;
			return sky != null ? sky.InAir(colour) : colour;
		}

		/// <summary>
		/// The fog colour: the drops' white in the air's light, pulled toward the colour of what is
		/// falling by its share of the extinction — of the distance fog's extinction, which leaves the
		/// fog's drops out while their layer is drawn (<paramref name="layerDrawn"/>).
		/// </summary>
		public static Color ColorOf(in WeatherFrame frame, WeatherRenderProfile profile, bool layerDrawn = false)
		{
			Color color = Air(Droplets);
			float p = frame[WeatherChannel.Precipitation];
			if (p <= 0.001f || profile == null)
			{
				return color;
			}
			Color falling = Air(profile.Rain.FogColor) * frame[WeatherChannel.RainWeight]
				+ Air(profile.Snow.FogColor) * frame[WeatherChannel.SnowWeight]
				+ Air(profile.Hail.FogColor) * frame[WeatherChannel.HailWeight]
				+ profile.Ash.FogColor * frame[WeatherChannel.AshWeight]
				+ profile.Sand.FogColor * frame[WeatherChannel.SandWeight];
			falling.a = 1f;
			float total = Mathf.Max(1e-6f, UniformExtinction(frame, layerDrawn));
			return Color.Lerp(color, falling, Mathf.Clamp01(FallingExtinction(frame) / total));
		}

		public static void Apply(in WeatherFrame frame, WeatherRenderProfile profile)
		{
			// Only what is the same at every height: the share the region's fog is blended toward, its
			// colour and its extinction alike. The share was the whole weather's, the fog's drops and all,
			// which switched the pipeline's fog ON in a fog with nothing for it to draw — and the sky dome
			// melts its horizon into that fog's colour by 45 % whenever it is on (SkySystem), a flat band
			// round the horizon over and above the fog the march itself draws there.
			bool layerDrawn = FogLayerView.Drawn;
			float extinction = UniformExtinction(frame, layerDrawn);
			// Visibility, by Koschmieder: where a dark object's contrast has fallen to 2%.
			float visibility = extinction > 1e-6f ? 3.912f / extinction : 1e6f;
			FogComposer.SetWeather(UniformAmount(frame, layerDrawn), ColorOf(frame, profile, layerDrawn), extinction, visibility);
		}
	}
}
