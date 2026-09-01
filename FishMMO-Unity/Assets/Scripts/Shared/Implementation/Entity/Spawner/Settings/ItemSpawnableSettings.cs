using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Per-spawner configuration for world items: which item, how many, and how often the rare one
	/// shows up instead of the common one.
	/// </summary>
	/// <remarks>
	/// Like <see cref="NPCSpawnableSettings"/>, this exists so one world-item prefab can serve
	/// every pickup in the game. The prefab is the container; the template injected here is what it
	/// actually is. That keeps the object pool to a single bucket for all ground loot rather than
	/// one bucket per item type, which is the difference between a fixed memory cost and one that
	/// scales with the size of the item database.
	/// </remarks>
	[Serializable]
	public class ItemSpawnableSettings : SpawnableSettings
	{
		/// <summary>
		/// One possible item roll: a template, a stack range, and a weight.
		/// </summary>
		[Serializable]
		public class ItemRoll
		{
			/// <summary>The item template to inject.</summary>
			[Tooltip("The item this entry spawns.")]
			public BaseItemTemplate ItemTemplate;

			/// <summary>Minimum stack size.</summary>
			[Tooltip("Minimum stack size.")]
			[Min(1)]
			public int MinimumAmount = 1;

			/// <summary>Maximum stack size.</summary>
			[Tooltip("Maximum stack size.")]
			[Min(1)]
			public int MaximumAmount = 1;

			/// <summary>
			/// Relative weight of this entry against the others. Higher is more likely.
			/// </summary>
			[Tooltip("Relative likelihood of this entry against the others in the table.")]
			[Min(0f)]
			public float Weight = 1f;
		}

		/// <summary>
		/// The item template to inject when <see cref="RollTable"/> is empty.
		/// </summary>
		[Tooltip("Item spawned when the roll table below is empty.")]
		public BaseItemTemplate ItemTemplate;

		/// <summary>
		/// The minimum number of items to spawn in a single stack.
		/// </summary>
		[Min(1)]
		public int MinimumAmount = 1;

		/// <summary>
		/// The maximum number of items to spawn in a single stack.
		/// </summary>
		[Min(1)]
		public int MaximumAmount = 1;

		/// <summary>
		/// Optional weighted table. When it holds entries, one is rolled instead of using
		/// <see cref="ItemTemplate"/>.
		/// </summary>
		[Tooltip("Optional weighted table. When non-empty, one entry is rolled per spawn.")]
		public List<ItemRoll> RollTable = new List<ItemRoll>();

		/// <summary>
		/// Achievement granted to whoever picks the item up. 0 for none.
		/// </summary>
		[Tooltip("Achievement template ID granted on pickup. 0 = none.")]
		[TemplateReference(typeof(AchievementTemplate))]
		public int AchievementTemplateID;

		/// <summary>
		/// Injects the rolled item template, stack amount and generation seed into the spawned
		/// WorldItem component.
		/// </summary>
		/// <param name="nob">The instantiated network object to configure.</param>
		/// <param name="spawner">The spawner that created this object.</param>
		public override void OnSpawned(NetworkObject nob, ObjectSpawner spawner)
		{
			WorldItem worldItem = nob.GetComponent<WorldItem>();
			if (worldItem == null)
			{
				return;
			}

			BaseItemTemplate template = ItemTemplate;
			int minimum = MinimumAmount;
			int maximum = MaximumAmount;

			ItemRoll roll = RollEntry();
			if (roll != null)
			{
				template = roll.ItemTemplate;
				minimum = roll.MinimumAmount;
				maximum = roll.MaximumAmount;
			}

			if (template == null)
			{
				return;
			}

			worldItem.Template = template;
			worldItem.AchievementTemplateID = AchievementTemplateID;

			// Range's upper bound is exclusive, and a max below the min would otherwise throw.
			int high = Mathf.Max(minimum, maximum);
			worldItem.Amount = (uint)DeterministicRNG.Shared.Range(Mathf.Max(1, minimum), high + 1);

			/* The identity half of the roll. Item.Initialize treats seed 0 as "derive one from the
			 * database id", and a freshly granted item has no id yet — so a drop spawned without a
			 * seed rolled its attributes from RNG(0), identically for every drop of the template,
			 * and re-rolled them differently at the next relog once a real id existed. Zero is that
			 * sentinel, so it is re-rolled away. Two 16-bit draws cover the full int range; a single
			 * Range(int.MinValue, int.MaxValue) call spans 2^32 and is clamped with a warning. */
			int seed;
			do
			{
				seed = (DeterministicRNG.Shared.Next(0x10000) << 16) | DeterministicRNG.Shared.Next(0x10000);
			} while (seed == 0);
			worldItem.Seed = seed;
		}

		/// <summary>
		/// Rolls one entry from the weighted table.
		/// </summary>
		/// <returns>The rolled entry, or null when the table is empty or has no positive weight.</returns>
		private ItemRoll RollEntry()
		{
			if (RollTable == null || RollTable.Count < 1)
			{
				return null;
			}

			float total = 0f;
			for (int i = 0; i < RollTable.Count; ++i)
			{
				ItemRoll entry = RollTable[i];
				if (entry?.ItemTemplate != null && entry.Weight > 0f)
				{
					total += entry.Weight;
				}
			}

			// Every entry is null or zero-weight — fall back to the single-template fields.
			if (total <= 0f)
			{
				return null;
			}

			float value = DeterministicRNG.Shared.Range(0f, total);
			float cumulative = 0f;

			for (int i = 0; i < RollTable.Count; ++i)
			{
				ItemRoll entry = RollTable[i];
				if (entry?.ItemTemplate == null || entry.Weight <= 0f)
				{
					continue;
				}

				cumulative += entry.Weight;
				if (value <= cumulative)
				{
					return entry;
				}
			}

			// Float rounding can land past the last cumulative bound; take the last valid entry.
			for (int i = RollTable.Count - 1; i >= 0; --i)
			{
				ItemRoll entry = RollTable[i];
				if (entry?.ItemTemplate != null && entry.Weight > 0f)
				{
					return entry;
				}
			}

			return null;
		}

		/// <summary>
		/// Clamps the stack ranges so a misconfigured entry cannot throw at spawn time.
		/// </summary>
		public override void OnValidate()
		{
			base.OnValidate();

			if (MinimumAmount < 1) MinimumAmount = 1;
			if (MaximumAmount < MinimumAmount) MaximumAmount = MinimumAmount;

			if (RollTable == null)
			{
				return;
			}

			for (int i = 0; i < RollTable.Count; ++i)
			{
				ItemRoll entry = RollTable[i];
				if (entry == null) continue;

				if (entry.MinimumAmount < 1) entry.MinimumAmount = 1;
				if (entry.MaximumAmount < entry.MinimumAmount) entry.MaximumAmount = entry.MinimumAmount;
				if (entry.Weight < 0f) entry.Weight = 0f;
			}
		}
	}
}
