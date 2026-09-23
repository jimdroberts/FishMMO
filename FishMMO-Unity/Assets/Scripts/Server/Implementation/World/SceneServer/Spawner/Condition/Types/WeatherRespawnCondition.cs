using System;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Allows a respawn only while the weather over the spawner is, or is not, doing something
	/// (Q16).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The creature that only comes out in the rain, and the one that will not. Both are this one
	/// condition with <see cref="Invert"/> either way, dropped onto a spawner beside any other
	/// condition it already has — nothing about the spawner knows or cares that it is weather doing
	/// the gating.
	/// </para>
	/// <para>
	/// <b>It gates the respawn, not the life.</b> Something that spawned in a storm stays when the
	/// storm passes; it simply is not replaced while the sky is clear. Despawning a creature the
	/// moment the weather turned would take a mob out from under whoever was fighting it.
	/// </para>
	/// <para>
	/// <b>Server-side, and not predicted.</b> A spawn is authoritative and always was — the client
	/// is told what appeared. So unlike everything else in this weather pass, this reads the
	/// server's own <see cref="WeatherQuery"/> sample at whatever tick it is called on, and there is
	/// nothing for a client to agree with.
	/// </para>
	/// </remarks>
	[Serializable]
	public class WeatherRespawnCondition : RespawnCondition
	{
		[Tooltip("The kinds of weather this looks for over the spawner.")]
		public WeatherKindMask Weather = WeatherKindMask.Rain;

		[Tooltip("How hard it has to be doing it. 0 means any trace counts.")]
		[Range(0f, 1f)] public float MinimumSeverity = 0.3f;

		[Tooltip("Flip it: allow the respawn only while the weather is NOT doing this. The creature that hides from the rain.")]
		public bool Invert;

		[Tooltip("What to do when the scene has no weather at all. Most scenes without weather should spawn normally, so this defaults to allowing it.")]
		public bool AllowWhenSceneHasNoWeather = true;

		/// <inheritdoc />
		public override bool OnCheckCondition(SpawnerRuntime spawner)
		{
			if (spawner == null)
			{
				return false;
			}

			/* A scene with no timeline registered has no weather to gate on. Answering "no" there
			 * would silently empty every gated spawner in any scene that had not been given weather
			 * yet — which is most of them while this is being built — so the default is to let the
			 * spawn through and let the setting say otherwise. */
			if (!WeatherQuery.TryGetTimeline(spawner.Scene, out _))
			{
				return AllowWhenSceneHasNoWeather;
			}

			WeatherSample sample = WeatherQuery.Sample(spawner.Scene, spawner.Definition.Position);
			bool matches = Weather != WeatherKindMask.None
				&& sample.Matches(Weather)
				&& SeverityOf(sample) >= MinimumSeverity;

			return Invert ? !matches : matches;
		}

		/// <summary>How hard the chosen kinds are coming down, 0..1: the worst of them.</summary>
		/// <remarks>
		/// Read from the kinds actually chosen, so a spawner waiting on a thunderstorm is not
		/// released by heavy rain with no lightning in it.
		/// </remarks>
		public float SeverityOf(in WeatherSample sample)
		{
			float worst = 0f;
			if ((Weather & WeatherKindMask.Precipitation) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.Precipitation]);
			}
			if ((Weather & WeatherKindMask.Wind) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.WindSpeed]);
			}
			if ((Weather & WeatherKindMask.Fog) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.FogDensity]);
			}
			if ((Weather & WeatherKindMask.Lightning) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.LightningRate]);
			}
			if ((Weather & WeatherKindMask.Clouds) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.CloudCover]);
			}
			if ((Weather & WeatherKindMask.Aurora) != 0)
			{
				worst = Mathf.Max(worst, sample.Frame[WeatherChannel.Aurora]);
			}
			if ((Weather & WeatherKindMask.ClearSky) != 0)
			{
				// "Clear" is the absence of the others, so its severity is how clear it is.
				worst = Mathf.Max(worst, 1f - sample.Frame[WeatherChannel.CloudCover]);
			}
			return Mathf.Clamp01(worst);
		}
	}
}
