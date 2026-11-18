using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Walks to the nearest cover and waits there until the weather passes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Entered by <see cref="AIController"/> when the archetype's <see cref="AIShelterSettings"/>
	/// says the weather at the NPC's own position is worth walking away from, and left again when it
	/// is not. The state does the walking; the archetype decides whether it ever happens (Q15).
	/// </para>
	/// <para>
	/// A shelter is a <see cref="WeatherVolume"/> of kind <see cref="WeatherVolumeKind.Shelter"/> —
	/// the same volumes that keep the rain off a player standing in them. So an NPC that has reached
	/// one is genuinely sheltered by the same measure the player's exposure uses, and is not merely
	/// standing somewhere a designer once labelled dry.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Seek Shelter State", menuName = "FishMMO/Character/NPC/AI/Seek Shelter State", order = 0)]
	public class SeekShelterState : BaseAIState
	{
		[Tooltip("How far into the volume to aim, 0 its edge and 1 its centre. Short of the centre so a crowd does not pile onto one point.")]
		[Range(0f, 1f)] public float DepthIntoShelter = 0.7f;

		[Tooltip("Metres of scatter around the chosen spot, so several NPCs sheltering together do not stack.")]
		[Min(0f)] public float Spread = 2f;

		/// <summary>Sets off toward the nearest cover. With none in reach, gives up at once.</summary>
		public override void Enter(AIController controller)
		{
			controller.Resume();
			if (!Head(controller))
			{
				/* No cover within reach. Standing in the rain doing nothing would leave the NPC
				 * frozen here for as long as the weather lasted, because nothing else drives this
				 * state — so hand it straight back to ordinary behaviour, which the controller will
				 * not immediately undo: it only enters this state when a shelter was found. */
				controller.TransitionToRandomMovementState();
			}
		}

		public override void Exit(AIController controller)
		{
			controller.Resume();
		}

		/// <summary>Keeps walking; once there, stands still and lets the controller decide when to leave.</summary>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			switch (controller.GetMovementProgress(deltaTime))
			{
				case AIMovementProgress.Arrived:
					// Under cover. The controller's shelter check is what takes it out of here again,
					// when the weather it came in from has passed.
					controller.Stop();
					return;

				case AIMovementProgress.Stuck:
					// Wedged on the way in. Try once more for a spot, and give up if there is none.
					controller.ClearPath();
					if (!Head(controller))
					{
						controller.TransitionToRandomMovementState();
					}
					return;

				case AIMovementProgress.Idle:
					if (!Head(controller))
					{
						controller.TransitionToRandomMovementState();
					}
					return;

				default:
					return;
			}
		}

		/// <summary>Picks a spot inside the nearest cover and sets off. False when there is none.</summary>
		private bool Head(AIController controller)
		{
			AIShelterSettings settings = controller.Shelter;
			if (settings == null || controller.Character?.GameObject == null)
			{
				return false;
			}

			WeatherVolume shelter = WeatherVolumeRegistry.NearestShelter(
				controller.Character.GameObject.scene,
				controller.Home,
				settings.SearchRadius,
				settings.MinimumShelterStrength);

			if (shelter == null || shelter.Shape == null)
			{
				return false;
			}

			Bounds bounds = shelter.Shape.bounds;
			Vector3 target = Vector3.Lerp(bounds.ClosestPoint(controller.Character.Transform.position), bounds.center, DepthIntoShelter);

			if (Spread > 0f)
			{
				// Scattered with the NPC's own seeded stream, so a group shelters in a huddle rather
				// than all standing on one point — and does it the same way on every run.
				DeterministicRNG rng = controller.NpcRNG ?? DeterministicRNG.Shared;
				target.x += (rng.NextFloat() * 2f - 1f) * Spread;
				target.z += (rng.NextFloat() * 2f - 1f) * Spread;
			}

			return controller.SetThrottledDestination(target);
		}
	}
}
