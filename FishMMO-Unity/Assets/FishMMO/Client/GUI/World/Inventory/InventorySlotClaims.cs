using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>
	/// Windows that, while open, take a right-click on an inventory slot for themselves: the Commerce module's trade
	/// window puts the item on the table instead of equipping or using it.
	/// </summary>
	/// <remarks>
	/// The inventory asks the claims first and falls back to its own meaning (equip, use) when none takes the click,
	/// so a game without the module keeps an ordinary bag. A claim answers true only when it handled the click.
	/// Registered once, before the first scene loads (<c>[RuntimeInitializeOnLoadMethod]</c>), as with
	/// <see cref="CharacterMenuEntries"/>.
	/// </remarks>
	public static class InventorySlotClaims
	{
		private static readonly List<KeyValuePair<string, Func<int, bool>>> claims = new List<KeyValuePair<string, Func<int, bool>>>();

		/// <summary>Adds (or replaces, by id) a claim on right-clicked inventory slots.</summary>
		public static void Register(string id, Func<int, bool> tryClaimRightClick)
		{
			if (string.IsNullOrEmpty(id) || tryClaimRightClick == null)
			{
				throw new ArgumentException("An inventory slot claim needs an id and a handler.");
			}
			Unregister(id);
			claims.Add(new KeyValuePair<string, Func<int, bool>>(id, tryClaimRightClick));
		}

		/// <summary>Removes a claim; false when there was none with that id.</summary>
		public static bool Unregister(string id)
		{
			return claims.RemoveAll(c => c.Key == id) > 0;
		}

		/// <summary>True when an open window took the right-click on <paramref name="slotIndex"/>.</summary>
		public static bool TryClaimRightClick(int slotIndex)
		{
			for (int i = 0; i < claims.Count; ++i)
			{
				if (claims[i].Value(slotIndex))
				{
					return true;
				}
			}
			return false;
		}
	}
}
