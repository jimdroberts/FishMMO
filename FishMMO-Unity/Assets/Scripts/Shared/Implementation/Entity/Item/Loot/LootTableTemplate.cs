using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defines what a corpse holds: a set of independently-rolled item entries and a currency range.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Rolled exactly once, on the server, at the moment the NPC dies — never on the client and
	/// never again afterwards. The corpse then owns concrete <see cref="Item"/> instances, so what
	/// two looters see is the same pile rather than two independent rolls of the same table.
	/// </para>
	/// <para>
	/// Assigned on the NPC prefab and overridable per spawner via
	/// <see cref="NPCSpawnableSettings.LootTableOverride"/>, matching how attributes and abilities
	/// already vary one prefab across a zone.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Loot Table", menuName = "FishMMO/Character/Loot/Loot Table", order = 1)]
	public class LootTableTemplate : CachedScriptableObject<LootTableTemplate>, ICachedObject
	{
		/// <summary>
		/// Item lines rolled independently of one another.
		/// </summary>
		[Tooltip("Item entries. Each is rolled independently.")]
		public List<LootTableEntry> Entries = new List<LootTableEntry>();

		/// <summary>
		/// Hard cap on how many item stacks a single roll may produce.
		/// </summary>
		/// <remarks>
		/// Independent rolls have no natural ceiling, and the corpse's slot count is what the loot
		/// window renders — an unbounded table would produce a window of arbitrary size and a
		/// spawn payload to match. Entries are considered in list order, so the cap makes the
		/// earlier entries in a table the ones that survive it.
		/// </remarks>
		[Tooltip("Maximum item stacks a single roll may produce.")]
		[Min(1)]
		public int MaximumItemDrops = 8;

		/// <summary>
		/// Smallest currency amount a corpse may carry. Zero disables currency for this table.
		/// </summary>
		[Header("Currency")]
		[Tooltip("Minimum currency dropped. 0 with a 0 maximum means no currency.")]
		[Min(0)]
		public int MinimumCurrency;

		/// <summary>
		/// Largest currency amount a corpse may carry.
		/// </summary>
		[Tooltip("Maximum currency dropped.")]
		[Min(0)]
		public int MaximumCurrency;

		/// <inheritdoc />
		public string Name { get { return this.name; } }

		/// <summary>
		/// Rolls this table into concrete items and a currency amount.
		/// </summary>
		/// <remarks>
		/// The caller supplies the RNG so a corpse's contents can be reproduced from the NPC's own
		/// seed rather than from global shared state, which is what makes a death replayable when
		/// diagnosing a loot report.
		/// </remarks>
		/// <param name="rng">Random source. Falls back to the shared generator when null.</param>
		/// <param name="results">Receives the rolled items. Cleared before use.</param>
		/// <param name="currency">Receives the rolled currency amount.</param>
		public void Roll(DeterministicRNG rng, List<Item> results, out int currency)
		{
			currency = 0;

			if (results == null)
			{
				return;
			}
			results.Clear();

			if (rng == null)
			{
				rng = DeterministicRNG.Shared;
			}

			if (MaximumCurrency > 0)
			{
				int minimum = Mathf.Max(0, MinimumCurrency);
				int maximum = Mathf.Max(minimum, MaximumCurrency);
				// Range's upper bound is exclusive, so an inclusive maximum needs the +1.
				currency = minimum == maximum ? minimum : rng.Range(minimum, maximum + 1);
			}

			if (Entries == null || Entries.Count < 1)
			{
				return;
			}

			int cap = Mathf.Max(1, MaximumItemDrops);

			for (int i = 0; i < Entries.Count && results.Count < cap; ++i)
			{
				LootTableEntry entry = Entries[i];
				if (entry == null || entry.ItemTemplate == null)
				{
					continue;
				}

				if (entry.DropChance < 1f && rng.Range(0f, 1f) > entry.DropChance)
				{
					continue;
				}

				int minimum = Mathf.Max(1, entry.MinimumAmount);
				int maximum = Mathf.Max(minimum, entry.MaximumAmount);
				int amount = minimum == maximum ? minimum : rng.Range(minimum, maximum + 1);

				/* Clamp to the template's own stack ceiling. A corpse slot holds one Item, and an
				 * Item over its MaxStackSize is a stack the inventory can never accept whole —
				 * the take would half-transfer and strand the remainder on the corpse forever. */
				uint maxStack = entry.ItemTemplate.MaxStackSize > 0 ? entry.ItemTemplate.MaxStackSize : 1;
				if ((uint)amount > maxStack)
				{
					amount = (int)maxStack;
				}

				results.Add(new Item(entry.ItemTemplate, (uint)amount));
			}
		}

		/// <summary>
		/// Repairs inspector values that cannot produce a sane roll.
		/// </summary>
		private void OnValidate()
		{
			if (MaximumItemDrops < 1)
			{
				MaximumItemDrops = 1;
			}
			if (MinimumCurrency < 0)
			{
				MinimumCurrency = 0;
			}
			if (MaximumCurrency < MinimumCurrency)
			{
				MaximumCurrency = MinimumCurrency;
			}
			if (Entries == null)
			{
				return;
			}
			for (int i = 0; i < Entries.Count; ++i)
			{
				Entries[i]?.Validate();
			}
		}
	}
}
