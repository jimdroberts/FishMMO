using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Behavior tree leaf that adopts the pack's focus. If the NPC belongs to an
	/// <see cref="NPCGroup"/> whose <see cref="NPCGroup.GroupTargetCharacter"/> is a living target,
	/// this node sets the NPC's <see cref="AIController.Target"/> to it and returns Success.
	/// <para>
	/// Use case: "Focus the tank's target" → place this before a StateTransition to AttackState.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Adopt Group Target Node", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Adopt Group Target Node")]
	public class AIAdoptGroupTargetNode : AIBehaviorNode
	{
		/// <summary>
		/// Adopts the pack's focus as this NPC's target. Returns Success if there is a living focus
		/// and it was adopted, Failure otherwise.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Fails for an evading NPC. A leash that ends a fight makes the walk home an evade, and a
		/// node that handed an evading NPC a target — for the next node to send it into its attacking
		/// state — would be the same way back into the fight that <see cref="AIController.ForceTarget"/>
		/// and every threat path refuse.
		/// </para>
		/// <para>
		/// The focus is the pack's identity-checked character, validated as a target: it used to be a
		/// bare transform, adopted whether it was dead, despawned or re-issued by the pool to someone
		/// else.
		/// </para>
		/// </remarks>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>Success if the focus was adopted, Failure if there is no pack, no living focus, or the NPC is evading.</returns>
		public override AINodeResult Evaluate(AIController controller)
		{
			if (controller.Group == null || controller.IsEvading)
				return AINodeResult.Failure;

			ICharacter focus = controller.Group.GroupTargetCharacter;
			if (!AITargetSelection.IsValidTarget(focus))
				return AINodeResult.Failure;

			controller.Target = focus.Transform;
			controller.LookTarget = focus.Transform;
			return AINodeResult.Success;
		}
	}
}
