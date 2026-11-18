using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Decides which of a GameObject's interactables a player means.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One GameObject routinely carries several. An NPC is itself a lootable corpse, and an NPC
	/// that also trades, banks or hands out quests carries that component too. A plain
	/// <c>GetComponent&lt;IInteractable&gt;()</c> returns whichever the component order happens to
	/// yield, so a player looting a dead merchant could have their request answered by the shop —
	/// and a player talking to a live one could be answered by its corpse.
	/// </para>
	/// <para>
	/// The rule lives here rather than in each caller because it was previously written out three
	/// times — in the client's input handler, in the scene server's interaction system, and not at
	/// all in the quest system, which still used the raw <c>GetComponent</c> that the other two had
	/// already been fixed away from. Three copies of a rule is three chances for them to disagree
	/// about which component answers, and the two that disagreed were on opposite ends of the same
	/// request.
	/// </para>
	/// <para>
	/// <b>The rule.</b> A corpse wins whenever there is one, because being dead is the more
	/// specific state: a body cannot trade, and the loot on it is on a timer while the shop will
	/// still be there after the NPC respawns. Otherwise the first non-corpse interactable is used,
	/// which is the historical behaviour for every single-component object.
	/// </para>
	/// </remarks>
	public static class InteractableResolver
	{
		/// <summary>
		/// Returns the interactable a player targeting this GameObject means.
		/// </summary>
		/// <param name="gameObject">The targeted GameObject.</param>
		/// <returns>The interactable, or null when the object has none.</returns>
		public static IInteractable Resolve(GameObject gameObject)
		{
			if (gameObject == null)
			{
				return null;
			}

			IInteractable[] interactables = gameObject.GetComponents<IInteractable>();
			if (interactables == null || interactables.Length < 1)
			{
				return null;
			}

			// A corpse wins outright while it is one.
			for (int i = 0; i < interactables.Length; ++i)
			{
				if (interactables[i] is ILootableCorpse corpse && corpse.IsCorpse)
				{
					return corpse;
				}
			}

			/* Fall through to the first non-corpse. A corpse component that is not currently a
			 * corpse must not be returned here — its CanInteract refuses while the NPC is alive,
			 * and returning it would suppress the merchant sharing the GameObject. */
			for (int i = 0; i < interactables.Length; ++i)
			{
				if (interactables[i] is ILootableCorpse)
				{
					continue;
				}
				return interactables[i];
			}

			return interactables[0];
		}

		/// <summary>
		/// Returns the interactable a registered scene object refers to.
		/// </summary>
		/// <remarks>
		/// Identity first. The scene object <em>is</em> the interactable whenever it implements the
		/// interface, and that identity is exactly what the client's ID named — a GameObject
		/// carrying two interactables registers two scene objects with two distinct IDs, and the
		/// one the client sent is the one it meant. Falls back to the GameObject rule for
		/// interactables that register through some other component.
		/// </remarks>
		/// <param name="sceneObject">The resolved scene object.</param>
		/// <returns>The interactable, or null.</returns>
		public static IInteractable Resolve(ISceneObject sceneObject)
		{
			if (sceneObject == null)
			{
				return null;
			}
			if (sceneObject is IInteractable interactable)
			{
				return interactable;
			}
			return Resolve(sceneObject.GameObject);
		}

		/// <summary>
		/// True when this GameObject currently presents as a corpse.
		/// </summary>
		/// <remarks>
		/// The question a non-corpse interactable asks before accepting an interaction: a dead
		/// merchant must not open its shop, and a dead quest giver must not hand out work.
		/// </remarks>
		/// <param name="gameObject">The GameObject to test.</param>
		/// <returns>True if something on it is a corpse right now.</returns>
		public static bool IsCorpse(GameObject gameObject)
		{
			if (gameObject == null)
			{
				return false;
			}

			ILootableCorpse[] corpses = gameObject.GetComponents<ILootableCorpse>();
			for (int i = 0; i < corpses.Length; ++i)
			{
				if (corpses[i] != null && corpses[i].IsCorpse)
				{
					return true;
				}
			}
			return false;
		}
	}
}
