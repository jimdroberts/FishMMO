using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>Where around its target a <see cref="BlinkToHitTargetAction"/> puts the caster.</summary>
	/// <remarks>Serialized by value on authored actions: append only, never reorder.</remarks>
	public enum BlinkPlacement : byte
	{
		/// <summary>In front of the target (along the frame's forward).</summary>
		InFront = 0,
		/// <summary>Behind the target.</summary>
		Behind = 1,
		/// <summary>At the target's left side.</summary>
		Left = 2,
		/// <summary>At the target's right side.</summary>
		Right = 3,
		/// <summary>Over the target's head, to drop onto it.</summary>
		Above = 4,
	}

	/// <summary>Which way is "forward" for a <see cref="BlinkPlacement"/>.</summary>
	/// <remarks>Serialized by value on authored actions: append only, never reorder.</remarks>
	public enum BlinkFrame : byte
	{
		/// <summary>The way the target faces: Behind is at its back (a backstab).</summary>
		TargetFacing = 0,
		/// <summary>
		/// As if the target had turned to face its attacker: In Front is between the two, Behind is the far side of the target
		/// from the caster, Left and Right are the target's as it faces the caster.
		/// </summary>
		AttackDirection = 1,
	}

	/// <summary>
	/// Blinks the caster to the character a hit struck, at a chosen place around it: in front, behind, to either side, or
	/// above. Wire it to an OnHit event (a "Blink On Hit" component); combine it with damage, a root, a knockback.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where.</b> <see cref="Placement"/> in the <see cref="Frame"/>, <see cref="Distance"/> metres from the target's
	/// centre. The spot is found the way <see cref="TeleportCasterAction"/> finds a landing: a capsule of the caster's size is
	/// swept out from the target a step high, stops short of anything in the way, and is dropped onto the ground there. A
	/// side with no room (the target's back against a wall) gives way to the next side that has it
	/// (<see cref="ResolveDestination"/>), so the blink never lands inside a wall or inside the target. Above checks there
	/// is headroom over the target and falls back the same way when there is not. The caster is turned to face the target.
	/// </para>
	/// <para>
	/// <b>Server only, deliberately</b>, like <see cref="KnockbackHitAction"/>'s displacement. A hit is not run again when
	/// the owner replays a correction, so a predicted blink would be undone by every correction from before the hit and redone
	/// a round trip later: the caster would flicker between the two places. Moved by the server, the owner instead receives
	/// the new position in its next reconcile and is moved once, a round trip after the hit. A player moves through its
	/// motor (<see cref="KCCController.TeleportTo(Vector3, Quaternion)"/>), an NPC through its brain
	/// (<see cref="INPCBrain.BlinkTo"/>, onto the NavMesh). Refused while rooted, incapacitated, dead or changing scene.
	/// </para>
	/// <para>
	/// <b>Effects</b> play on every client at once, for the hit it saw: <see cref="DepartFX"/> where the caster is and
	/// <see cref="ArriveFX"/> where it will land, which on an observer is measured from the characters as it draws them.
	/// </para>
	/// </remarks>
	[Serializable]
	[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]
	public class BlinkToHitTargetAction : BaseAction
	{
		/// <summary>Where around the target to land.</summary>
		[Tooltip("Where around the target to land.")]
		public BlinkPlacement Placement = BlinkPlacement.Behind;

		/// <summary>Which way is forward for the placement.</summary>
		[Tooltip("TargetFacing: Behind = at the target's back. AttackDirection: Behind = the far side of the target from the caster.")]
		public BlinkFrame Frame = BlinkFrame.TargetFacing;

		/// <summary>Metres from the target's centre to the caster's (for Above: clearance over the target's head).</summary>
		[Tooltip("Metres from the target's centre to the caster's. For Above, the gap over the target's head.")]
		public float Distance = 1.2f;

		/// <summary>The effect played where the caster leaves.</summary>
		[Tooltip("Played where the caster leaves.")]
		public GameObject DepartFX;

		/// <summary>The effect played where the caster lands.</summary>
		[Tooltip("Played where the caster lands.")]
		public GameObject ArriveFX;

		/// <summary>Size of both effects in metres (effects are authored to a 1 m cube).</summary>
		public float FXSize = 2f;

		/// <summary>Metres above the feet to play both effects.</summary>
		public float FXHeight = 1f;

		/// <summary>Scratch for the headroom test; only whether anything is there is read.</summary>
		[NonSerialized]
		private static readonly Collider[] headroomHits = new Collider[1];

		/// <summary>The order sides are tried in when the wanted one has no room.</summary>
		private static readonly BlinkPlacement[] Fallbacks = { BlinkPlacement.Behind, BlinkPlacement.Left, BlinkPlacement.Right, BlinkPlacement.InFront };

		/// <inheritdoc/>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			ICharacter caster = initiator ?? eventData?.Initiator;
			if (caster?.Transform == null || !TryResolveTarget(eventData, out ICharacter target) || target?.Transform == null ||
				ReferenceEquals(target, caster))
			{
				return;
			}

			ReadBody(caster, out PhysicsScene physics, out float radius, out float height, out int layers);
			Vector3 targetFeet = target.Transform.position;
			Vector3 forward = FrameForward(Frame, target.Transform.forward, targetFeet - caster.Transform.position, caster.Transform.forward);
			Vector3 destination = ResolveDestination(physics, targetFeet, TargetHeight(target), forward, Placement, Distance, radius, height, layers);
			Vector3 toTarget = targetFeet - destination;
			toTarget.y = 0f;
			Quaternion facing = toTarget.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toTarget, Vector3.up) : caster.Transform.rotation;

			// What every client sees, for the hit it saw.
			if (IsClientPeer(caster, eventData) && !IsReplayTick(eventData))
			{
				Scene scene = PlayFXAction.ResolveSpawnScene(caster, eventData);
				if (DepartFX != null)
				{
					PlayFXAction.SpawnFX(DepartFX, caster.Transform.position + Vector3.up * FXHeight, scene, FXSize);
				}
				if (ArriveFX != null)
				{
					PlayFXAction.SpawnFX(ArriveFX, destination + Vector3.up * FXHeight, scene, FXSize);
				}
			}

			// The move: the server's alone (see the remarks).
			if (!EcaAuthority.IsServer(caster, eventData))
			{
				return;
			}
			if (caster.IsTeleporting || CharacterIncapacitation.IsRooted(caster) || CharacterIncapacitation.IsIncapacitated(caster) ||
				(caster.TryGet(out ICharacterDamageController damage) && !damage.IsAlive))
			{
				return;
			}
			if (caster is IPlayerCharacter player && player.CharacterController != null)
			{
				player.CharacterController.TeleportTo(destination, facing);
			}
			else if (caster.TryGet(out INPCBrain brain))
			{
				brain.BlinkTo(destination, facing);
			}
		}

		/// <summary>
		/// The frame's forward, flat: the target's facing, or (<see cref="BlinkFrame.AttackDirection"/>) the way from the target
		/// back toward the caster. The caster's own forward, reversed to face it, when that is undefined (standing on the target).
		/// </summary>
		public static Vector3 FrameForward(BlinkFrame frame, Vector3 targetForward, Vector3 casterToTarget, Vector3 casterForward)
		{
			Vector3 forward = frame == BlinkFrame.AttackDirection ? -casterToTarget : targetForward;
			forward.y = 0f;
			if (forward.sqrMagnitude < 1e-6f)
			{
				forward = frame == BlinkFrame.AttackDirection ? -casterForward : casterForward;
				forward.y = 0f;
			}
			return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
		}

		/// <summary>The flat direction from the target a horizontal placement lies in (zero for <see cref="BlinkPlacement.Above"/>).</summary>
		public static Vector3 PlacementDirection(BlinkPlacement placement, Vector3 forward)
		{
			Vector3 right = Vector3.Cross(Vector3.up, forward);
			switch (placement)
			{
				case BlinkPlacement.InFront: return forward;
				case BlinkPlacement.Behind: return -forward;
				case BlinkPlacement.Left: return -right;
				case BlinkPlacement.Right: return right;
				default: return Vector3.zero;
			}
		}

		/// <summary>
		/// Where the caster lands: the wanted placement if there is room for a body there, else the first side of
		/// <see cref="Fallbacks"/> that has it, else the side with the most room.
		/// </summary>
		public static Vector3 ResolveDestination(PhysicsScene physics, Vector3 targetFeet, float targetHeight, Vector3 forward,
			BlinkPlacement placement, float distance, float radius, float height, int layers)
		{
			if (placement == BlinkPlacement.Above)
			{
				Vector3 over = targetFeet + Vector3.up * (targetHeight + Mathf.Max(0.1f, distance * 0.25f));
				if (!physics.IsValid() ||
					physics.OverlapCapsule(over + Vector3.up * radius, over + Vector3.up * Mathf.Max(radius, height - radius), radius * 0.95f,
						headroomHits, layers, QueryTriggerInteraction.Ignore) == 0)
				{
					return over;
				}
				/* No headroom: the nearest thing to a drop from above is a step behind. Above's distance is the gap over the
				 * head, not a distance out from the body, so the ground placement takes a body-and-a-half of its own. */
				placement = BlinkPlacement.Behind;
				distance = Mathf.Max(distance, StandOffDistance(radius));
			}

			// Room enough that the caster does not land against (or in) the target: two bodies' width. A hair of tolerance,
			// so a landing measured at exactly the asked distance is not refused for its last bit of float.
			float needed = Mathf.Min(distance, radius * 2f + 0.1f) - 0.01f;

			Vector3 best = targetFeet;
			float bestRoom = -1f;
			foreach (BlinkPlacement side in Candidates(placement))
			{
				Vector3 direction = PlacementDirection(side, forward);
				Vector3 landing = TeleportCasterAction.ResolveDestination(physics, targetFeet, direction, distance, radius, height, layers);
				Vector3 across = landing - targetFeet;
				across.y = 0f;
				float room = across.magnitude;
				if (room >= needed)
				{
					return landing;
				}
				if (room > bestRoom)
				{
					bestRoom = room;
					best = landing;
				}
			}
			return best;
		}

		/// <summary>How far out from a target a ground landing stands when nothing says otherwise: clear of both bodies.</summary>
		public static float StandOffDistance(float radius)
		{
			return radius * 2f + 0.6f;
		}

		private static System.Collections.Generic.IEnumerable<BlinkPlacement> Candidates(BlinkPlacement wanted)
		{
			yield return wanted;
			foreach (BlinkPlacement side in Fallbacks)
			{
				if (side != wanted)
				{
					yield return side;
				}
			}
		}

		/// <summary>The caster's body, as the blink must fit it: its motor's capsule and collision layers, or a person's.</summary>
		private static void ReadBody(ICharacter caster, out PhysicsScene physics, out float radius, out float height, out int layers)
		{
			physics = caster.GameObject.scene.GetPhysicsScene();
			radius = 0.3f;
			height = 1.8f;
			layers = ~0;
			if (caster is IPlayerCharacter player && player.CharacterController != null &&
				player.CharacterController.Motor != null && player.CharacterController.Motor.Capsule != null)
			{
				var motor = player.CharacterController.Motor;
				physics = motor.PhysicsScene;
				radius = motor.Capsule.radius;
				height = motor.Capsule.height;
				layers = motor.CollidableLayers;
			}
		}

		/// <summary>How tall the target stands over its feet (its collider's top), or a person's height.</summary>
		private static float TargetHeight(ICharacter target)
		{
			Collider collider = target.GameObject != null ? target.GameObject.GetComponent<Collider>() : null;
			if (collider != null && collider.enabled)
			{
				float top = collider.bounds.max.y - target.Transform.position.y;
				if (top > 0.2f)
				{
					return top;
				}
			}
			return 1.8f;
		}
	}
}
