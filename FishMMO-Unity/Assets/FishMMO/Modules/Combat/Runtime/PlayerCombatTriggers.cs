using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The combat triggers every player's <see cref="CharacterDamageController"/> runs (the Combat module): what
	/// each race prefab authored on its controller before the controller became a part the character adds at wake. Today the
	/// achievement counters: damage dealt, healing done, monsters killed.
	/// </summary>
	/// <remarks>
	/// One asset, addressable with the shared permanent label, so the client's postboot and the server's launcher put it in
	/// the cache before any character wakes (<see cref="Active"/>). An NPC's controller runs none: no NPC prefab authored any.
	/// </remarks>
	[CreateAssetMenu(fileName = "PlayerCombatTriggers", menuName = "FishMMO/Combat/Player Combat Triggers", order = 1)]
	public class PlayerCombatTriggers : CachedScriptableObject<PlayerCombatTriggers>, ICachedObject
	{
		[Header("ECA - Damage")]
		[Tooltip("Triggers invoked when a player deals damage to another character.")]
		public List<Trigger> OnDamage = new List<Trigger>();
		[Tooltip("Triggers invoked when a player receives damage.")]
		public List<Trigger> OnDamaged = new List<Trigger>();

		[Header("ECA - Healing")]
		[Tooltip("Triggers invoked when a player heals a character.")]
		public List<Trigger> OnHeal = new List<Trigger>();
		[Tooltip("Triggers invoked when a player is healed.")]
		public List<Trigger> OnHealed = new List<Trigger>();

		[Header("ECA - Kill")]
		[Tooltip("Triggers invoked when a player kills a character.")]
		public List<Trigger> OnKill = new List<Trigger>();
		[Tooltip("Triggers invoked when a player is killed.")]
		public List<Trigger> OnKilled = new List<Trigger>();

		[Header("ECA - Resurrect")]
		[Tooltip("Triggers invoked when a player resurrects another.")]
		public List<Trigger> OnResurrect = new List<Trigger>();
		[Tooltip("Triggers invoked when a player is resurrected.")]
		public List<Trigger> OnResurrected = new List<Trigger>();

		/// <summary>The loaded asset, or null (an edit-mode test, a build without it).</summary>
		public static PlayerCombatTriggers Active => GetFirst<PlayerCombatTriggers>();

		/// <summary>Adds this asset's triggers to a player's controller lists (once: a part initialises once).</summary>
		public void ApplyTo(CharacterDamageController controller)
		{
			if (controller == null)
			{
				return;
			}
			Append(controller.OnDamageTriggers, OnDamage);
			Append(controller.OnDamagedTriggers, OnDamaged);
			Append(controller.OnHealTriggers, OnHeal);
			Append(controller.OnHealedTriggers, OnHealed);
			Append(controller.OnKillTriggers, OnKill);
			Append(controller.OnKilledTriggers, OnKilled);
			Append(controller.OnResurrectTriggers, OnResurrect);
			Append(controller.OnResurrectedTriggers, OnResurrected);
		}

		private static void Append(List<Trigger> into, List<Trigger> from)
		{
			if (into == null || from == null)
			{
				return;
			}
			foreach (Trigger trigger in from)
			{
				if (trigger != null && !into.Contains(trigger))
				{
					into.Add(trigger);
				}
			}
		}
	}
}
