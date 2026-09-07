using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The waypoints a character has discovered in one scene, as a growable bit set: bit
	/// <c>N</c> of page <c>N / 64</c> is set when waypoint <c>N</c> is unlocked.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a bit set.</b> A scene has tens of waypoints, every character eventually
	/// unlocks most of them, and the whole set is read and written together: on login, in the
	/// spawn payload, and on save. One row per (character, waypoint) would be a forty-byte row for
	/// a single bit; the set stores those bits as bits. The same shape is on the wire and in the
	/// database (<c>character_waypoints</c>, one row per non-empty page), so nothing translates
	/// between representations — see <c>CharacterDialogueChoiceData</c> for the precedent.</para>
	/// <para><b>Why pages.</b> A single 64-bit word would cap a scene at 64 waypoints, and the day
	/// that cap is hit is the day a designer's sixty-fifth waypoint silently fails to persist.
	/// Pages make the set unbounded (to <see cref="MaxIndex"/>, a sanity cap on authoring rather
	/// than on storage) at the cost of one extra small integer per row.</para>
	/// <para><b>Persistence without a version.</b> Bits are only ever set, and the database merge
	/// is an OR. So "what still needs writing" is exactly <c>unlocked &amp; ~persisted</c>, and
	/// confirming a write is <c>persisted |= written</c>; a bit set while a write was in flight is
	/// not in <c>written</c> and stays dirty. No counter to bump and no stale-confirmation hazard —
	/// the trap the versioned tables fell into (see the attribute/ability persistence notes).</para>
	/// </remarks>
	public sealed class WaypointUnlockMask
	{
		/// <summary>Bits per page. A page is one <see cref="ulong"/> and one database row.</summary>
		public const int BitsPerPage = 64;

		/// <summary>
		/// Highest waypoint index a scene may author. Sixteen pages. A bound on authoring mistakes
		/// and on what a payload may ask the controller to allocate, not a design limit anyone is
		/// expected to reach.
		/// </summary>
		public const int MaxIndex = 1023;

		/// <summary>Most pages a scene can have, derived from <see cref="MaxIndex"/>.</summary>
		public const int MaxPages = (MaxIndex / BitsPerPage) + 1;

		private ulong[] unlocked = Array.Empty<ulong>();
		private ulong[] persisted = Array.Empty<ulong>();

		/// <summary>Number of pages allocated. Trailing pages may be empty.</summary>
		public int PageCount => unlocked.Length;

		/// <summary>Whether no waypoint is unlocked.</summary>
		public bool IsEmpty
		{
			get
			{
				for (int i = 0; i < unlocked.Length; ++i)
				{
					if (unlocked[i] != 0)
					{
						return false;
					}
				}
				return true;
			}
		}

		/// <summary>Whether an index is within <c>[0, MaxIndex]</c>.</summary>
		public static bool IsValidIndex(int waypointIndex)
		{
			return waypointIndex >= 0 && waypointIndex <= MaxIndex;
		}

		/// <summary>Which page an index lives on.</summary>
		public static int PageOf(int waypointIndex)
		{
			return waypointIndex / BitsPerPage;
		}

		/// <summary>The bit an index occupies within its page.</summary>
		public static ulong BitOf(int waypointIndex)
		{
			return 1UL << (waypointIndex % BitsPerPage);
		}

		/// <summary>Whether a waypoint is unlocked.</summary>
		public bool Contains(int waypointIndex)
		{
			if (!IsValidIndex(waypointIndex))
			{
				return false;
			}
			int page = PageOf(waypointIndex);
			return page < unlocked.Length && (unlocked[page] & BitOf(waypointIndex)) != 0;
		}

		/// <summary>
		/// Unlocks a waypoint.
		/// </summary>
		/// <returns>True when the bit was newly set; false when it was already set or the index is invalid.</returns>
		public bool Add(int waypointIndex)
		{
			if (!IsValidIndex(waypointIndex))
			{
				return false;
			}
			int page = PageOf(waypointIndex);
			ulong bit = BitOf(waypointIndex);
			EnsurePage(page);
			if ((unlocked[page] & bit) != 0)
			{
				return false;
			}
			unlocked[page] |= bit;
			return true;
		}

		/// <summary>The unlocked bits of a page. Zero for a page beyond the allocated range.</summary>
		public ulong GetPage(int page)
		{
			return page >= 0 && page < unlocked.Length ? unlocked[page] : 0UL;
		}

		/// <summary>
		/// Installs persisted bits: unlocked <i>and</i> already confirmed, so they are not dirty.
		/// </summary>
		/// <remarks>
		/// OR-ed in, never assigned. A restore arriving after an unlock in the same session (a
		/// slow load racing a quick interaction) must not drop the unlock; and on the owner client
		/// the payload may be re-read on a pooled respawn.
		/// </remarks>
		public void Restore(int page, ulong mask)
		{
			if (page < 0 || page >= MaxPages || mask == 0)
			{
				return;
			}
			EnsurePage(page);
			unlocked[page] |= mask;
			persisted[page] |= mask;
		}

		/// <summary>Whether any bit of a page has not been confirmed by the database.</summary>
		public bool IsPageDirty(int page)
		{
			return page >= 0 && page < unlocked.Length && (unlocked[page] & ~persisted[page]) != 0;
		}

		/// <summary>Whether any page is dirty.</summary>
		public bool IsDirty
		{
			get
			{
				for (int i = 0; i < unlocked.Length; ++i)
				{
					if ((unlocked[i] & ~persisted[i]) != 0)
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>Records that the database holds these bits of a page.</summary>
		public void MarkPersisted(int page, ulong writtenMask)
		{
			if (page < 0 || page >= unlocked.Length)
			{
				return;
			}
			/* Only bits that are actually unlocked can be confirmed. A confirmation for a bit
			 * this set does not hold is a confirmation from another owner of the character or a
			 * bug; either way, claiming it as persisted here would be a lie about local state. */
			persisted[page] |= writtenMask & unlocked[page];
		}

		/// <summary>Appends every page with unconfirmed bits.</summary>
		/// <param name="sceneName">The scene, stamped onto each snapshot.</param>
		/// <param name="results">Receives the dirty pages.</param>
		public void CollectDirtyPages(string sceneName, List<Core.WaypointPageSnapshot> results)
		{
			if (results == null)
			{
				return;
			}
			for (int i = 0; i < unlocked.Length; ++i)
			{
				if ((unlocked[i] & ~persisted[i]) != 0)
				{
					results.Add(new Core.WaypointPageSnapshot(sceneName, i, unlocked[i]));
				}
			}
		}

		/// <summary>Drops everything.</summary>
		public void Clear()
		{
			unlocked = Array.Empty<ulong>();
			persisted = Array.Empty<ulong>();
		}

		private void EnsurePage(int page)
		{
			if (page < unlocked.Length)
			{
				return;
			}
			Array.Resize(ref unlocked, page + 1);
			Array.Resize(ref persisted, page + 1);
		}
	}
}
