using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One item line in a <see cref="LootTableTemplate"/>: what may drop, how likely it is, and how
	/// much of it.
	/// </summary>
	/// <remarks>
	/// Entries roll independently rather than competing for a single weighted slot. A weighted pick
	/// forces every table to be a "choose exactly one" table, which cannot express the ordinary case
	/// of a creature that always drops its hide and sometimes also drops a tooth. Independent rolls
	/// express both, and <see cref="LootTableTemplate.MaximumItemDrops"/> is what bounds the result.
	/// </remarks>
	[Serializable]
	public class LootTableEntry
	{
		/// <summary>
		/// The item that may drop. An unassigned template makes the entry inert.
		/// </summary>
		[Tooltip("Item that may drop from this entry.")]
		public BaseItemTemplate ItemTemplate;

		/// <summary>
		/// Probability in the range 0-1 that this entry produces a stack. 1 is a guaranteed drop.
		/// </summary>
		[Tooltip("Chance this entry drops at all. 1 = always.")]
		[Range(0f, 1f)]
		public float DropChance = 0.1f;

		/// <summary>
		/// Smallest stack size produced when the entry drops.
		/// </summary>
		[Tooltip("Minimum stack size when this entry drops.")]
		[Min(1)]
		public int MinimumAmount = 1;

		/// <summary>
		/// Largest stack size produced when the entry drops.
		/// </summary>
		[Tooltip("Maximum stack size when this entry drops. Clamped to the item's MaxStackSize.")]
		[Min(1)]
		public int MaximumAmount = 1;

		/// <summary>
		/// Repairs inspector values that cannot produce a sane roll.
		/// </summary>
		public void Validate()
		{
			if (MinimumAmount < 1)
			{
				MinimumAmount = 1;
			}
			if (MaximumAmount < MinimumAmount)
			{
				MaximumAmount = MinimumAmount;
			}
			DropChance = Mathf.Clamp01(DropChance);
		}
	}
}
