using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Pure, engine-free bookkeeping for which characters a <see cref="Region"/> considers
	/// "inside". Separates the two truths a region has to reconcile:
	/// <list type="bullet">
	///   <item><description><b>Raw</b> presence: what the underlying NetworkCollider has told us via
	///   Enter/Exit callbacks. This is updated on every callback, including during prediction
	///   reconcile replay and while a character is teleporting.</description></item>
	///   <item><description><b>Effective</b> membership: characters for which the region has actually
	///   raised an Enter (and no Exit yet). Gameplay/visual triggers key off this set.</description></item>
	/// </list>
	/// Callbacks that arrive while <c>suppressed</c> (reconciling or teleporting) update raw
	/// presence only; <see cref="Flush"/> later diffs raw against effective and yields the
	/// Enter/Exit decisions that were deferred, each exactly once. Because
	/// <see cref="Flush"/> is a pure diff it also repairs any drift between the two sets
	/// (e.g. a child region releasing a character back to its parent).
	/// </summary>
	/// <typeparam name="T">Character key type. Must be a reference type with stable hash/equality.</typeparam>
	public sealed class RegionMembership<T> where T : class
	{
		private readonly HashSet<T> raw = new HashSet<T>();
		private readonly HashSet<T> effective = new HashSet<T>();
		private readonly List<T> scratch = new List<T>();

		/// <summary>Number of characters the collider currently reports as overlapping.</summary>
		public int RawCount => raw.Count;

		/// <summary>Number of characters for which an Enter has been raised and no Exit yet.</summary>
		public int EffectiveCount => effective.Count;

		/// <summary>True when the collider currently reports <paramref name="character"/> overlapping.</summary>
		public bool IsRawInside(T character) => character != null && raw.Contains(character);

		/// <summary>True when an Enter has been raised for <paramref name="character"/> and no Exit yet.</summary>
		public bool IsInside(T character) => character != null && effective.Contains(character);

		/// <summary>
		/// Records a collider Enter callback.
		/// </summary>
		/// <param name="character">The character that entered the collider.</param>
		/// <param name="suppressed">True while reconciling or teleporting: record only, do not fire.</param>
		/// <param name="canEnter">When false the character is physically inside but a child region
		/// owns it; raw presence is recorded but no Enter fires (the parent stays "exited").</param>
		/// <returns>True when the caller should raise an Enter now.</returns>
		public bool RecordEnter(T character, bool suppressed, bool canEnter = true)
		{
			if (character == null)
			{
				return false;
			}
			raw.Add(character);
			if (suppressed || !canEnter)
			{
				return false;
			}
			return effective.Add(character);
		}

		/// <summary>
		/// Records a collider Exit callback.
		/// </summary>
		/// <param name="character">The character that left the collider.</param>
		/// <param name="suppressed">True while reconciling or teleporting: record only, do not fire.</param>
		/// <returns>True when the caller should raise an Exit now (an Enter had been raised earlier).</returns>
		public bool RecordExit(T character, bool suppressed)
		{
			if (character == null)
			{
				return false;
			}
			raw.Remove(character);
			if (suppressed)
			{
				return false;
			}
			return effective.Remove(character);
		}

		/// <summary>
		/// Decides whether a collider Stay callback should be forwarded. Stay only fires for
		/// characters that are effectively inside and never while suppressed, so replay ticks
		/// produce no Stay events and a parent does not "stay" while a child owns the character.
		/// </summary>
		public bool ShouldStay(T character, bool suppressed)
		{
			if (character == null || suppressed)
			{
				return false;
			}
			return effective.Contains(character);
		}

		/// <summary>
		/// Logically exits a character without touching raw presence. Used when a child region
		/// takes ownership of a character standing inside this (parent) region.
		/// </summary>
		/// <returns>True when an Enter had previously fired, so the caller must raise the paired Exit.
		/// False when no Enter ever fired (unpaired exits are never produced).</returns>
		public bool ForceExit(T character)
		{
			return character != null && effective.Remove(character);
		}

		/// <summary>
		/// Logically enters a character that is still physically inside (raw) but not effectively
		/// inside. Used when a child region releases a character back to this (parent) region.
		/// </summary>
		/// <returns>True when the caller should raise an Enter now.</returns>
		public bool TryEnter(T character, bool suppressed, bool canEnter = true)
		{
			if (character == null || suppressed || !canEnter || !raw.Contains(character))
			{
				return false;
			}
			return effective.Add(character);
		}

		/// <summary>
		/// Forgets a character entirely (both sets) without producing events. Use for destroyed
		/// characters where there is nobody left to notify.
		/// </summary>
		public void Forget(T character)
		{
			if (character == null)
			{
				return;
			}
			raw.Remove(character);
			effective.Remove(character);
		}

		/// <summary>
		/// Diffs raw presence against effective membership and reports the Enter/Exit events
		/// that must be raised to bring them into agreement. Call once per non-reconciling tick.
		/// Characters for which <paramref name="isSuppressed"/> returns true (still teleporting)
		/// are skipped and will be reconsidered on a later flush. Characters for which
		/// <paramref name="isDestroyed"/> returns true are forgotten silently.
		/// </summary>
		/// <param name="isSuppressed">Per-character defer predicate; null means never deferred.</param>
		/// <param name="isDestroyed">Per-character prune predicate; null means never pruned.</param>
		/// <param name="canEnter">Per-character predicate; false blocks an Enter (child owns the character). Null means always allowed.</param>
		/// <param name="enters">Receives characters to raise Enter for (appended).</param>
		/// <param name="exits">Receives characters to raise Exit for (appended).</param>
		public void Flush(
			Func<T, bool> isSuppressed,
			Func<T, bool> isDestroyed,
			Func<T, bool> canEnter,
			List<T> enters,
			List<T> exits)
		{
			// Prune destroyed characters first so neither branch reports them.
			if (isDestroyed != null)
			{
				scratch.Clear();
				foreach (T c in raw)
				{
					if (isDestroyed(c)) scratch.Add(c);
				}
				foreach (T c in effective)
				{
					if (isDestroyed(c) && !scratch.Contains(c)) scratch.Add(c);
				}
				for (int i = 0; i < scratch.Count; ++i)
				{
					Forget(scratch[i]);
				}
			}

			// Exits: effectively inside but the collider no longer reports them.
			scratch.Clear();
			foreach (T c in effective)
			{
				if (raw.Contains(c))
				{
					continue;
				}
				if (isSuppressed != null && isSuppressed(c))
				{
					continue;
				}
				scratch.Add(c);
			}
			for (int i = 0; i < scratch.Count; ++i)
			{
				effective.Remove(scratch[i]);
				exits?.Add(scratch[i]);
			}

			// Enters: collider reports them but no Enter was raised (deferred or blocked by a child).
			scratch.Clear();
			foreach (T c in raw)
			{
				if (effective.Contains(c))
				{
					continue;
				}
				if (isSuppressed != null && isSuppressed(c))
				{
					continue;
				}
				if (canEnter != null && !canEnter(c))
				{
					continue;
				}
				scratch.Add(c);
			}
			for (int i = 0; i < scratch.Count; ++i)
			{
				effective.Add(scratch[i]);
				enters?.Add(scratch[i]);
			}
			scratch.Clear();
		}

		/// <summary>
		/// Empties effective membership and reports every character that must receive a final Exit.
		/// Raw presence is cleared as well. Use when the region is disabled or destroyed.
		/// </summary>
		/// <param name="isDestroyed">Characters matching this predicate are dropped without an Exit.</param>
		/// <param name="exits">Receives characters to raise Exit for (appended).</param>
		public void Clear(Func<T, bool> isDestroyed, List<T> exits)
		{
			foreach (T c in effective)
			{
				if (isDestroyed != null && isDestroyed(c))
				{
					continue;
				}
				exits?.Add(c);
			}
			effective.Clear();
			raw.Clear();
		}
	}
}
