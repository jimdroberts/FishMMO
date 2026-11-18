using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Changes the scene's air, over a transition: the scripted storm, done the physical way.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Weather is worked out from the air, so a scripted beat changes the air and lets it do the
	/// rest. The ritual that brings the storm makes the air damp, low and unstable, and towers,
	/// rain and lightning follow from that air exactly as they would anywhere; the boss phase that
	/// chills the arena takes its heat away, and whatever was falling turns to snow. What happens is
	/// whatever that air does on this world, which is why it cannot contradict the sky.
	/// </para>
	/// <para>
	/// Additions on top of the scene's own air, never a replacement for it. <see cref="Relative"/>
	/// adds these to whatever has already been added at runtime, so two scripted effects stack and
	/// a second action with the opposite numbers takes the first away; off, the runtime additions
	/// are set to exactly these (all zero hands the scene back to its own air). For weather in one
	/// place use <see cref="SpawnStormCellAction"/>.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ChangeAirAction : BaseAction
	{
		[Tooltip("What to add to the scene's air.")]
		public AirOffsets Air;

		[Tooltip("Add these to whatever has already been added at runtime. Off sets the runtime additions to exactly these.")]
		public bool Relative = true;

		[Tooltip("Seconds to move into it. 0 snaps, which is rarely what anybody wants to look at.")]
		[Min(0f)] public float TransitionSeconds = 20f;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			IWeatherService weather = WeatherActionGate.Resolve(initiator, eventData, out Scene scene);
			if (weather == null)
			{
				return;
			}
			AirOffsets target = Air;
			if (Relative && weather.TryGetAirOffsets(scene, out AirOffsets current))
			{
				target = current + Air;
			}
			weather.SetAirOffsets(scene, target, TransitionSeconds);
		}
	}
}
