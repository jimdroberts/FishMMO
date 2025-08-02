using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Respawn condition that allows respawning only when all specified NPCs are dead.
	/// </summary>
	public class DeadNPCRespawnCondition : BaseRespawnCondition
	{
		/// <summary>
		/// The list of NPCs to check for alive/dead status before allowing respawn.
		/// </summary>
		public List<NPC> NPCs;

		/// <summary>
		/// Checks if the respawn condition is met. Returns true if all NPCs are dead or the list is empty/null.
		/// </summary>
		/// <param name="spawner">The object spawner requesting the condition check.</param>
		/// <returns>True if respawn is allowed, false otherwise.</returns>
		public override bool OnCheckCondition(ObjectSpawner spawner)
		{
			// If there are no NPCs to check, allow respawn.
			if (NPCs == null ||
				NPCs.Count < 1)
			{
				return true;
			}

			// Check each NPC for alive status.
			foreach (NPC npc in NPCs)
			{
				// Skip null NPCs or those without a damage controller.
				if (npc == null ||
					!npc.TryGet(out ICharacterDamageController damageController))
				{
					continue;
				}

				// If any NPC is alive, respawn is not allowed.
				if (damageController.IsAlive)
				{
					// If any NPC is alive still we can't respawn.
					return false;
				}
			}
			// If all NPCs are dead, respawn is allowed.
			return true;
		}
	}
}