using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Starts a storm cell — weather in one place, drifting — rather than changing the whole sky.
	/// </summary>
	/// <remarks>
	/// The one to reach for most of the time. A cell has a centre, a radius and a velocity, so it
	/// arrives, passes over and moves on, and everyone outside it sees a storm in the distance
	/// instead of the weather simply being different now.
	/// </remarks>
	[Serializable]
	public class SpawnStormCellAction : BaseAction
	{
		[Tooltip("The weather inside the cell. Without one this action does nothing.")]
		public WeatherPreset Preset;

		[Tooltip("How big it is, in metres.")]
		[Min(1f)] public float RadiusMeters = 250f;

		[Tooltip("Where it starts, relative to whoever set it off. Leave at zero to put it on top of them.")]
		public Vector3 Offset;

		[Tooltip("Which way it drifts and how fast, in metres per second on the ground plane.")]
		public Vector2 Velocity = new Vector2(6f, 0f);

		[Tooltip("Seconds before it fades out. 0 leaves it to the server's own limit.")]
		[Min(0f)] public float LifetimeSeconds = 180f;

		[Tooltip("Put it where the storm is heading FROM, so it blows in rather than appearing overhead. Metres upwind, along the velocity.")]
		[Min(0f)] public float UpwindMeters = 400f;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (Preset == null)
			{
				return;
			}
			IWeatherService weather = WeatherActionGate.Resolve(initiator, eventData, out Scene scene);
			if (weather == null)
			{
				return;
			}

			Vector3 at = initiator.Transform.position + Offset;

			/* Started upwind, so it is seen coming. Dropped on top of the player instead, a storm
			 * simply switches on overhead — which reads as a bug rather than as weather, and throws
			 * away the one thing a cell has over a preset. */
			if (UpwindMeters > 0f && Velocity.sqrMagnitude > 1e-6f)
			{
				Vector2 heading = Velocity.normalized;
				at -= new Vector3(heading.x, 0f, heading.y) * UpwindMeters;
			}

			weather.SpawnCell(scene, Preset, at, RadiusMeters, Velocity, LifetimeSeconds);
		}
	}
}
