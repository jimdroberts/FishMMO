using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Blinks the caster up to <see cref="MaxDistance"/> metres the way it faces, stopping short of whatever is in the way,
	/// and plays <see cref="ArriveFX"/> where it lands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Wire it to the OnSpawn of a PointBlank ability</b> (an invisible object at the caster's feet). The departure effect is
	/// an ordinary <see cref="PlayFXAction"/> before this one in the same event, played at the object, which stays where
	/// the caster stood. Because the object is what observers are sent, every client sees both ends of the blink.
	/// </para>
	/// <para>
	/// <b>One destination on every peer.</b> It is measured from the cast's spawn pose (the caster's feet and facing on the
	/// tick it cast, which the server sends observers exactly), or from the caster's own pose on a replayed tick, where the
	/// object is not made again and the pose is the same one. A capsule the size of the caster's is swept along the
	/// ground a step high, so a blink stops at a wall rather than passing it, and then dropped onto the ground there.
	/// </para>
	/// <para>
	/// <b>The move</b> runs on the server and the owner (<see cref="KCCController.TeleportTo"/>) and again on a replayed tick
	/// (<see cref="ReappliesOnReplay"/>): the caster's position is motor state, so a correction for an earlier tick takes
	/// the blink back and the replay of the cast's tick must put it back. Refused while rooted or incapacitated. Players
	/// only: an NPC's body belongs to its NavMesh agent.
	/// </para>
	/// </remarks>
	[Serializable]
	public class TeleportCasterAction : BaseAction
	{
		/// <summary>The furthest the blink goes, metres.</summary>
		[Tooltip("The furthest the blink goes, metres.")]
		public float MaxDistance = 5f;

		/// <summary>The effect played where the caster lands.</summary>
		[Tooltip("The effect played where the caster lands.")]
		public GameObject ArriveFX;

		/// <summary>The arrival effect's size in metres (effects are authored to a 1 m cube).</summary>
		[Tooltip("The arrival effect's size in metres (effects are authored to a 1 m cube).")]
		public float ArriveFXSize = 2f;

		/// <summary>Metres above the landing point to play the arrival effect.</summary>
		[Tooltip("Metres above the landing point to play the arrival effect.")]
		public float ArriveFXHeight = 1f;

		/// <summary>How high a step the blink rises over: a kerb or a stair does not stop it, a wall does.</summary>
		public const float StepHeight = 0.35f;

		/// <summary>Kept between the capsule and what stopped it.</summary>
		public const float Skin = 0.05f;

		/// <inheritdoc/>
		public override bool ReappliesOnReplay => true;

		/// <inheritdoc/>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			ICharacter caster = initiator ?? eventData?.Initiator;
			if (caster?.Transform == null)
			{
				return;
			}

			Vector3 start;
			Quaternion facing;
			if (AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject))
			{
				start = abilityObject.SpawnPosition;
				facing = abilityObject.SpawnRotation;
			}
			else
			{
				start = caster.Transform.position;
				facing = caster.Transform.rotation;
			}

			KCCController controller = caster is IPlayerCharacter player ? player.CharacterController : null;
			float radius = 0.3f, height = 1.8f;
			int layers = ~0;
			PhysicsScene physics = caster.GameObject.scene.GetPhysicsScene();
			if (controller != null && controller.Motor != null && controller.Motor.Capsule != null)
			{
				radius = controller.Motor.Capsule.radius;
				height = controller.Motor.Capsule.height;
				layers = controller.Motor.CollidableLayers;
				physics = controller.Motor.PhysicsScene;
			}

			Vector3 destination = ResolveDestination(physics, start, facing * Vector3.forward, MaxDistance, radius, height, layers);

			if (controller != null && EcaAuthority.MayPredict(caster, eventData) &&
				!CharacterIncapacitation.IsRooted(caster) && !CharacterIncapacitation.IsIncapacitated(caster) &&
				!caster.IsTeleporting)
			{
				controller.TeleportTo(destination);
			}

			if (ArriveFX != null && IsClientPeer(caster, eventData) && !IsReplayTick(eventData))
			{
				Vector3 at = destination + Vector3.up * ArriveFXHeight;
				Scene scene = PlayFXAction.ResolveSpawnScene(caster, eventData);
				// Held with the departure effect when this is a spawn chain, so the two ends show together.
				if (!AbilityObject.TryHoldSpawnEffect(ArriveFX, scene, null, at, ArriveFXSize))
				{
					PlayFXAction.SpawnFX(ArriveFX, at, scene, ArriveFXSize);
				}
			}
		}

		/// <summary>
		/// Where a blink from <paramref name="start"/> (the feet) along <paramref name="forward"/> lands: as far as
		/// <paramref name="maxDistance"/> goes before a capsule of the caster's size, raised a step, meets anything, then
		/// dropped onto the ground beneath (or left at the start's height over a drop, to fall from there).
		/// </summary>
		public static Vector3 ResolveDestination(PhysicsScene physics, Vector3 start, Vector3 forward, float maxDistance,
			float radius, float height, int layers)
		{
			forward.y = 0f;
			if (forward.sqrMagnitude < 1e-6f || maxDistance <= 0f)
			{
				return start;
			}
			forward.Normalize();

			float castRadius = Mathf.Max(0.05f, radius * 0.95f);
			Vector3 bottom = start + Vector3.up * (StepHeight + radius);
			Vector3 top = start + Vector3.up * Mathf.Max(StepHeight + radius, height - radius);
			float distance = maxDistance;
			if (physics.IsValid() &&
				physics.CapsuleCast(bottom, top, castRadius, forward, out RaycastHit hit, maxDistance, layers, QueryTriggerInteraction.Ignore))
			{
				distance = Mathf.Max(0f, hit.distance - Skin);
			}

			Vector3 landing = start + forward * distance;
			float probe = StepHeight + 1.5f;
			if (physics.IsValid() &&
				physics.Raycast(landing + Vector3.up * (StepHeight + 0.1f), Vector3.down, out RaycastHit ground, probe, layers, QueryTriggerInteraction.Ignore))
			{
				landing.y = ground.point.y;
			}
			return landing;
		}
	}
}
