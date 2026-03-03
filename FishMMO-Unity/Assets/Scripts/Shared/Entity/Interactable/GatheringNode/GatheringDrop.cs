using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defines a single drop entry for a <see cref="GatheringNodeTemplate"/>.
	/// Each entry specifies an item template, amount range, and weight for weighted random selection.
	/// </summary>
	[Serializable]
	public class GatheringDrop
	{
		/// <summary>
		/// The item template to drop.
		/// </summary>
		public BaseItemTemplate Item;

		/// <summary>
		/// Minimum number of items to drop (inclusive).
		/// </summary>
		[Min(1)]
		public int MinAmount = 1;

		/// <summary>
		/// Maximum number of items to drop (inclusive).
		/// </summary>
		[Min(1)]
		public int MaxAmount = 1;

		/// <summary>
		/// Relative weight for weighted random selection. Higher values increase drop chance.
		/// </summary>
		[Min(0.01f)]
		public float Weight = 1.0f;
	}
}