using System;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Whether, and in what, an archetype's NPCs go and stand out of the weather.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Off by default, deliberately (Q15).</b> Every existing archetype in the project gets this
	/// struct at its defaults, and at its defaults it does nothing at all. Weather arrives across a
	/// whole region at once, so a default of "on" would have every NPC on the continent abandon its
	/// post the first time a front came through — patrols leaving their routes, merchants leaving
	/// their stalls, and quest targets nowhere to be found. Sheltering is something an archetype
	/// opts into because it makes sense for that creature.
	/// </para>
	/// <para>
	/// <b>It reads the same weather the player's exposure does.</b> A <see cref="WeatherSample"/>
	/// taken at the NPC's own position, so an NPC already under a canopy has nothing to walk away
	/// from and an NPC in the open does — from the same numbers that are soaking the player standing
	/// next to it.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AIShelterSettings
	{
		[Tooltip("Whether this archetype's NPCs go and stand out of the weather at all. Off by default: weather covers whole regions, and every NPC in one leaving its post at once is rarely what anybody wanted.")]
		public bool Enabled;

		[Tooltip("The kinds of weather worth walking away from.")]
		public WeatherKindMask ShelterFrom = WeatherKindMask.Precipitation | WeatherKindMask.Lightning;

		[Tooltip("How exposed the NPC has to be before it bothers. At 0.5 an NPC already half under cover stays where it is.")]
		[Range(0f, 1f)] public float MinimumExposure = 0.5f;

		[Tooltip("How hard it has to be coming down. 0 means any trace of the chosen kinds counts.")]
		[Range(0f, 1f)] public float MinimumSeverity = 0.35f;

		[Tooltip("How far from home the NPC will go looking for cover.")]
		[Min(1f)] public float SearchRadius = 45f;

		[Tooltip("How well a volume has to shelter before it counts as shelter. A thin canopy is not a roof.")]
		[Range(0f, 1f)] public float MinimumShelterStrength = 0.5f;

		[Tooltip("Seconds between asking the question. Weather moves slowly; this need not be quick.")]
		[Min(0.5f)] public float CheckInterval = 6f;

		[Tooltip("The state entered to walk to cover. Without one the NPC has no way to get there and the whole setting stays inert.")]
		public SeekShelterState ShelterState;

		/// <summary>
		/// Whether an NPC standing in this weather should be looking for cover.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Pure, and the reason it is: sheltering has to be decided the same way whether it is asked
		/// on a tick, in a test, or by a tool showing a designer what an archetype will do — and
		/// every input is in the sample.
		/// </para>
		/// <para>
		/// <b>Severity is read from the kinds actually chosen</b>, not from one fixed channel. An
		/// archetype that shelters from lightning and nothing else must not be driven out by heavy
		/// rain it does not care about, and would be if this measured precipitation regardless.
		/// </para>
		/// </remarks>
		public bool WantsShelter(in WeatherSample weather)
		{
			if (!Enabled || ShelterFrom == WeatherKindMask.None)
			{
				return false;
			}
			// Already under something: nothing to walk away from.
			if (weather.Exposure < MinimumExposure)
			{
				return false;
			}
			if (!weather.Matches(ShelterFrom))
			{
				return false;
			}
			return SeverityOf(weather) >= MinimumSeverity;
		}

		/// <summary>How hard the chosen kinds are coming down here, 0..1: the worst of them.</summary>
		public float SeverityOf(in WeatherSample weather)
		{
			float worst = 0f;
			if ((ShelterFrom & WeatherKindMask.Precipitation) != 0)
			{
				worst = Mathf.Max(worst, weather.Frame[WeatherChannel.Precipitation]);
			}
			if ((ShelterFrom & WeatherKindMask.Wind) != 0)
			{
				worst = Mathf.Max(worst, weather.Frame[WeatherChannel.WindSpeed]);
			}
			if ((ShelterFrom & WeatherKindMask.Fog) != 0)
			{
				worst = Mathf.Max(worst, weather.Frame[WeatherChannel.FogDensity]);
			}
			if ((ShelterFrom & WeatherKindMask.Lightning) != 0)
			{
				worst = Mathf.Max(worst, weather.Frame[WeatherChannel.LightningRate]);
			}
			if ((ShelterFrom & WeatherKindMask.Clouds) != 0)
			{
				worst = Mathf.Max(worst, weather.Frame[WeatherChannel.CloudCover]);
			}
			return Mathf.Clamp01(worst);
		}

		/// <summary>
		/// Whether an NPC that is already sheltering should stay put.
		/// </summary>
		/// <remarks>
		/// A lower bar than <see cref="WantsShelter"/> on purpose. With one threshold, weather
		/// hovering on it would send an NPC out of the doorway and back in again for as long as it
		/// held there — the same flicker the exposure states use two thresholds to avoid, except
		/// this one is a creature visibly pacing in and out of a barn.
		/// </remarks>
		public bool WouldStay(in WeatherSample weather)
		{
			if (!Enabled || ShelterFrom == WeatherKindMask.None)
			{
				return false;
			}
			if (!weather.Matches(ShelterFrom))
			{
				return false;
			}
			return SeverityOf(weather) >= MinimumSeverity * 0.6f;
		}
	}
}
