#if UNITY_EDITOR
using UnityEditor;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.Shared
{
	/// <summary>The Pets module's AI states in the NPCs module's editor tools: the dashboard's AI States list and the asset organizer (2026-11-20).</summary>
	internal static class PetAIStates
	{
		[InitializeOnLoadMethod]
		private static void Install()
		{
			AIStateExtensions.Add("Pet Idle State", typeof(PetIdleState), "Pet", 10, AIAssetOrganizer.AI_ROOT + "/States/Pet");
			// A pet's attacking state is an attacking state: Combat group, the Attack folder.
			AIStateExtensions.Add("Pet Attacking State", typeof(PetAttackingState), null, 0, null);
		}
	}
}
#endif
