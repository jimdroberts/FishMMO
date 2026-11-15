using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Prey's "attacking" state: it never attacks, it runs. Slotted as a prey archetype's AttackingState, so everything that
	/// sends an NPC into a fight sends prey into flight instead: being hit (OnThreatReceived), a herd-mate's alarm (the
	/// spawner's pack, NPCGroup.AlertGroup) and its own search noticing a threat (WildlifeRules.Notices).
	/// </summary>
	/// <remarks>
	/// It never turns to fight and never gives up because it is cornered or tired: while any threat is within the species'
	/// safe distance it keeps running, away from all of them at once (<see cref="WildlifeRules.FleeHeading"/>), trying
	/// other directions when the way is blocked. Only when nothing threatening is in range does it calm down, and it settles
	/// where it is (its home moves there) rather than walking back into what it ran from.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Wildlife Flee State", menuName = "FishMMO/Character/NPC/AI/Wildlife Flee State", order = 0)]
	public class WildlifeFleeState : BaseAttackingState
	{
		/// <summary>Positions of the threats this tick, reused (states are shared assets, so it is filled and read in one call).</summary>
		private readonly List<Vector3> threatPositions = new List<Vector3>(8);

		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			// Turn and run rather than backpedal (see RetreatState.Enter): keep the target, drop the look target.
			controller.LookTarget = null;
			controller.Resume();
			// Away from the threat it was just handed; the next update widens that to everything in sight.
			Flee(controller, null);
		}

		/// <summary>The nearest threat is the one to watch.</summary>
		public override void PickTarget(AIController controller, List<ICharacter> targets)
		{
			ICharacter nearest = Nearest(controller, targets, float.PositiveInfinity);
			if (nearest == null)
			{
				OnCombatEnded(controller);
				return;
			}
			controller.Target = nearest.Transform;
			controller.LookTarget = null;
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (!controller.Character.TryGet(out ICharacterDamageController damage) || !damage.IsAlive)
			{
				controller.TransitionToIdleState();
				return;
			}

			float safe = SafeMetres(controller);
			List<ICharacter> threats = controller.CombatTargetBuffer;
			threats.Clear();
			// Everything it can see that it would flee from, plus whatever it is already running from.
			SweepForEnemies(controller, threats);
			ICharacter current = controller.TargetCharacter;
			if (current != null && AITargetSelection.IsValidTarget(current) && !threats.Contains(current))
			{
				threats.Add(current);
			}

			ICharacter nearest = Nearest(controller, threats, safe * safe);
			if (nearest == null)
			{
				Calm(controller);
				return;
			}
			if (!ReferenceEquals(nearest, current))
			{
				controller.Target = nearest.Transform;
			}

			switch (controller.GetMovementProgress(deltaTime))
			{
				case AIMovementProgress.Arrived:
				case AIMovementProgress.Idle:
				case AIMovementProgress.Stuck:
					Flee(controller, threats);
					return;
				default:
					return;
			}
		}

		/// <summary>Nothing threatening in range: stop running and settle here.</summary>
		private void Calm(AIController controller)
		{
			if (controller.OwningPet == null)
			{
				controller.Home = controller.Character.Transform.position;
			}
			controller.Target = null;
			controller.LookTarget = null;
			controller.ClearPath();
			OnCombatEnded(controller);
		}

		/// <summary>Sets off on the next leg, away from every threat; tries the sides, then anywhere, when that is blocked.</summary>
		private void Flee(AIController controller, List<ICharacter> threats)
		{
			Vector3 position = controller.Character.Transform.position;
			threatPositions.Clear();
			if (threats != null)
			{
				for (int i = 0; i < threats.Count; ++i)
				{
					if (threats[i] != null && threats[i].Transform != null)
					{
						threatPositions.Add(threats[i].Transform.position);
					}
				}
			}
			if (threatPositions.Count == 0 && controller.Target != null)
			{
				threatPositions.Add(controller.Target.position);
			}
			Vector3 away = WildlifeRules.FleeHeading(position, threatPositions, -controller.Character.Transform.forward);
			float leg = LegMetres(controller);
			Vector3 right = Vector3.Cross(Vector3.up, away);

			// Straight away, then the two diagonals, then the two sides: never give up while there is anywhere to go.
			if (controller.TryMoveTo(position + away * leg, throttle: false) != AIMovementResult.Failed ||
				controller.TryMoveTo(position + (away + right).normalized * leg, throttle: false) != AIMovementResult.Failed ||
				controller.TryMoveTo(position + (away - right).normalized * leg, throttle: false) != AIMovementResult.Failed ||
				controller.TryMoveTo(position + right * leg, throttle: false) != AIMovementResult.Failed ||
				controller.TryMoveTo(position - right * leg, throttle: false) != AIMovementResult.Failed)
			{
				return;
			}
			controller.TryRecoverFromStuck(position + away * leg);
		}

		private static ICharacter Nearest(AIController controller, List<ICharacter> candidates, float maxSqr)
		{
			if (candidates == null)
			{
				return null;
			}
			Vector3 position = controller.Character.Transform.position;
			ICharacter best = null;
			float bestSqr = maxSqr;
			for (int i = 0; i < candidates.Count; ++i)
			{
				ICharacter candidate = candidates[i];
				if (candidate == null || candidate.Transform == null || !AITargetSelection.IsValidTarget(candidate))
				{
					continue;
				}
				float sqr = (candidate.Transform.position - position).sqrMagnitude;
				if (sqr <= bestSqr)
				{
					bestSqr = sqr;
					best = candidate;
				}
			}
			return best;
		}

		private static WildlifeTemplate SpeciesOf(AIController controller)
		{
			return controller.Character is NPC npc ? npc.Wildlife : null;
		}

		private float SafeMetres(AIController controller)
		{
			WildlifeTemplate species = SpeciesOf(controller);
			return species != null ? species.SafeMetres : Mathf.Max(DetectionRadius * 1.5f, 20f);
		}

		private static float LegMetres(AIController controller)
		{
			WildlifeTemplate species = SpeciesOf(controller);
			return species != null ? species.FleeLegMetres : 14f;
		}
	}
}
