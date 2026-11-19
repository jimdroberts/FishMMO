using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The biome art authoring's marks for the entries of a template LIST (2026-10-16, phase I5b): one mark per entry, keyed by
	/// the entry's identity (a point-of-interest kind, a kind's variant, a prefab, a resource's name…), so a list a developer
	/// has edited still takes the entries a later spec adds, while each entry somebody edited, made or deleted stays as left.
	/// Pure: the authoring reads and writes the marks on the template (BiomeTemplate.EntryMarks / SetEntryMarks).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why per entry.</b> Until then a list carried one mark for the whole of it (BiomeTemplate.AuthoredMarks: a hash of the
	/// list the tool wrote): any hand edit, even one weight, made the whole list somebody's, and every kind, variant or crop
	/// the spec gained afterwards never reached that biome. Now each entry carries its own: the print of the entry the tool
	/// wrote (<see cref="Merge{T}"/>'s <c>print</c>), or <see cref="Removed"/> once somebody has deleted an entry the tool wrote.
	/// The list's own field mark becomes <see cref="PerEntry"/>, which says the list has been authored (an empty list is then
	/// a decision, not a gap) and that its entries carry their marks.
	/// </para>
	/// <para>
	/// <b>The rules</b>, per identity (only the first entry with an identity is the entry; a later one is somebody's and is
	/// never touched):
	/// an entry equal to the spec's is the tool's (marked, nothing written); an entry still equal to its mark (untouched since
	/// written) is brought up to the spec's, or removed when the spec no longer has it; an entry that differs from its mark,
	/// or has none, was edited or made by hand and is kept; an entry the spec has and the list lacks is added, unless the tool
	/// wrote it once and somebody deleted it (its mark then turns to <see cref="Removed"/>, a tombstone kept for good, so the
	/// deletion is remembered). An entry added goes in after the nearest entry before it in the spec's order, so a list
	/// authored from empty is the spec's list, in its order.
	/// </para>
	/// <para>
	/// <b>Migration</b> (<see cref="Migrate{T}"/>): a list still holding a whole-list mark becomes per-entry. Untouched since
	/// written (it still hashes to the mark): every entry is the tool's. Edited, but the mark is the hash of today's spec list
	/// (the spec has not changed since): what was written is known exactly, so each entry the tool wrote is marked with what
	/// it wrote (a hand edit then shows as a mismatch) and each one missing was deleted by hand. Edited, and the spec has
	/// changed since: only entries equal to today's spec's are the tool's; the others are taken as hand-made; and a spec
	/// entry the list lacks is taken as deleted by hand, since nothing tells a deletion from an addition (the list stays as
	/// the developer left it, as the whole-list mark kept it).
	/// </para>
	/// </remarks>
	public static class BiomeEntryMarks
	{
		/// <summary>A list field's own mark once its entries carry theirs: the list has been authored, entry by entry.</summary>
		public const string PerEntry = "<entries>";

		/// <summary>An entry's mark once somebody deleted the entry the tool wrote: it is not added again.</summary>
		public const string Removed = "<removed>";

		/// <summary>What the merge did, or found and left, for one entry.</summary>
		public enum Step : byte
		{
			/// <summary>A spec entry the list lacked was added.</summary>
			Added,
			/// <summary>The tool's untouched entry was brought up to the spec's.</summary>
			Updated,
			/// <summary>The tool's untouched entry, which the spec no longer has, was removed.</summary>
			Pruned,
			/// <summary>An entry equal to the spec's was marked as the tool's (nothing written).</summary>
			Marked,
			/// <summary>An entry the tool wrote is gone from the list: deleted by hand, now remembered.</summary>
			Tombstoned,
			/// <summary>An entry edited or made by hand differs from the spec's: kept.</summary>
			KeptEdited,
			/// <summary>A spec entry somebody deleted: not added again.</summary>
			KeptDeleted,
		}

		/// <summary>One entry's step: its identity, what it was (the list's print or mark) and what the spec says.</summary>
		public readonly struct Change
		{
			public readonly Step Step;
			public readonly string Id;
			public readonly string Was;
			public readonly string Now;

			public Change(Step step, string id, string was, string now)
			{
				Step = step;
				Id = id;
				Was = was;
				Now = now;
			}

			public override string ToString() => $"{Step} {Id}: {Was ?? "-"} → {Now ?? "-"}";
		}

		/// <summary>Every step the merge took or found, in order.</summary>
		public sealed class Outcome
		{
			public readonly List<Change> Changes = new List<Change>();

			/// <summary>True when the list or its marks were written (anything but a kept entry).</summary>
			public bool Wrote => Changes.Exists(c => c.Step != Step.KeptEdited && c.Step != Step.KeptDeleted);

			/// <summary>True when a hand edit or deletion kept the list from being the spec's.</summary>
			public bool Kept => Changes.Exists(c => c.Step == Step.KeptEdited || c.Step == Step.KeptDeleted);

			/// <summary>The identities of every step of a kind, in order (tests, reports).</summary>
			public List<string> Ids(Step step)
			{
				var ids = new List<string>();
				foreach (Change change in Changes)
				{
					if (change.Step == step)
					{
						ids.Add(change.Id);
					}
				}
				return ids;
			}
		}

		/// <summary>
		/// Brings a template list up to the spec's under the per-entry contract (see the class remarks), in place: the list
		/// <paramref name="list"/> and the marks <paramref name="marks"/> (entry identity → its mark) are both updated.
		/// </summary>
		/// <param name="target">What the spec writes, in its order (a later entry with an identity already given is ignored).</param>
		/// <param name="id">An entry's identity; null for an entry that has none (never touched).</param>
		/// <param name="print">Everything about an entry the tool writes: equal prints, equal entries.</param>
		public static Outcome Merge<T>(List<T> list, IReadOnlyList<T> target, Func<T, string> id, Func<T, string> print, Dictionary<string, string> marks)
		{
			var outcome = new Outcome();
			if (list == null || marks == null || id == null || print == null)
			{
				return outcome;
			}

			// The spec's entries by identity, first of each, in order.
			var spec = new List<(string Id, T Entry, string Print)>();
			var specAt = new Dictionary<string, int>(StringComparer.Ordinal);
			if (target != null)
			{
				foreach (T entry in target)
				{
					string key = id(entry);
					if (key != null && !specAt.ContainsKey(key))
					{
						specAt[key] = spec.Count;
						spec.Add((key, entry, print(entry)));
					}
				}
			}

			// 1. An entry the tool wrote that is gone: deleted by hand. Remembered, so it is not added again.
			Dictionary<string, int> firstAt = FirstOf(list, id);
			var keys = new List<string>(marks.Keys);
			keys.Sort(StringComparer.Ordinal);
			foreach (string key in keys)
			{
				string mark = marks[key];
				if (mark != Removed && !firstAt.ContainsKey(key))
				{
					marks[key] = Removed;
					outcome.Changes.Add(new Change(Step.Tombstoned, key, mark, null));
				}
			}

			// 2. The entries the list has, in its order.
			var prune = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				string key = id(list[i]);
				if (key == null || firstAt[key] != i)
				{
					continue;
				}
				string now = print(list[i]);
				marks.TryGetValue(key, out string mark);
				bool inSpec = specAt.TryGetValue(key, out int s);
				if (inSpec && now == spec[s].Print)
				{
					if (mark != now)
					{
						marks[key] = now;
						outcome.Changes.Add(new Change(Step.Marked, key, now, now));
					}
					continue;
				}
				if (mark != null && mark != Removed && mark == now)
				{
					if (inSpec)
					{
						list[i] = spec[s].Entry;
						marks[key] = spec[s].Print;
						outcome.Changes.Add(new Change(Step.Updated, key, now, spec[s].Print));
					}
					else
					{
						prune.Add(i);
						marks.Remove(key);
						outcome.Changes.Add(new Change(Step.Pruned, key, now, null));
					}
					continue;
				}
				if (inSpec)
				{
					outcome.Changes.Add(new Change(Step.KeptEdited, key, now, spec[s].Print));
				}
			}
			for (int p = prune.Count - 1; p >= 0; p--)
			{
				list.RemoveAt(prune[p]);
			}

			// 3. The spec's entries the list lacks: added, unless deleted by hand.
			firstAt = FirstOf(list, id);
			for (int j = 0; j < spec.Count; j++)
			{
				(string key, T entry, string now) = spec[j];
				if (firstAt.ContainsKey(key))
				{
					continue;
				}
				if (marks.TryGetValue(key, out string mark) && mark == Removed)
				{
					outcome.Changes.Add(new Change(Step.KeptDeleted, key, null, now));
					continue;
				}
				int at = list.Count;
				bool placed = false;
				for (int before = j - 1; before >= 0 && !placed; before--)
				{
					if (firstAt.TryGetValue(spec[before].Id, out int there))
					{
						at = there + 1;
						placed = true;
					}
				}
				for (int after = j + 1; after < spec.Count && !placed; after++)
				{
					if (firstAt.TryGetValue(spec[after].Id, out int there))
					{
						at = there;
						placed = true;
					}
				}
				list.Insert(at, entry);
				firstAt = FirstOf(list, id);
				marks[key] = now;
				outcome.Changes.Add(new Change(Step.Added, key, null, now));
			}
			return outcome;
		}

		/// <summary>
		/// The per-entry marks a list held under one whole-list mark converts to (see the class remarks).
		/// </summary>
		/// <param name="current">The list as it is now.</param>
		/// <param name="target">What the spec writes now.</param>
		/// <param name="wholeMark">The whole-list mark the authoring wrote.</param>
		/// <param name="wholeMarkOf">How that mark was made from a list.</param>
		/// <param name="written">
		/// What the tool wrote, entry by entry (identity and print), where the old mark tells it (a mark that is the value itself);
		/// null where it does not (a hash: known only when it is today's spec list's).
		/// </param>
		public static Dictionary<string, string> Migrate<T>(IReadOnlyList<T> current, IReadOnlyList<T> target, Func<T, string> id, Func<T, string> print,
			string wholeMark, Func<IReadOnlyList<T>, string> wholeMarkOf, IReadOnlyList<(string Id, string Print)> written = null)
		{
			var marks = new Dictionary<string, string>(StringComparer.Ordinal);
			var now = new Dictionary<string, string>(StringComparer.Ordinal);
			if (current != null)
			{
				foreach (T entry in current)
				{
					string key = id(entry);
					if (key != null && !now.ContainsKey(key))
					{
						now[key] = print(entry);
					}
				}
			}
			var spec = new List<(string Id, string Print)>();
			var specIds = new HashSet<string>(StringComparer.Ordinal);
			if (target != null)
			{
				foreach (T entry in target)
				{
					string key = id(entry);
					if (key != null && specIds.Add(key))
					{
						spec.Add((key, print(entry)));
					}
				}
			}

			// Untouched since written: every entry is the tool's.
			if (wholeMarkOf(current ?? Array.Empty<T>()) == wholeMark)
			{
				foreach (KeyValuePair<string, string> entry in now)
				{
					marks[entry.Key] = entry.Value;
				}
				return marks;
			}

			// Edited, but what was written is known: each entry the tool wrote is marked with it, or deleted by hand.
			if (written == null && wholeMarkOf(target ?? Array.Empty<T>()) == wholeMark)
			{
				written = spec;
			}
			if (written != null)
			{
				foreach ((string key, string wrote) in written)
				{
					if (key != null && !marks.ContainsKey(key))
					{
						marks[key] = now.ContainsKey(key) ? wrote : Removed;
					}
				}
				return marks;
			}

			// Edited, and the spec has changed since: only entries equal to its are the tool's; the rest are hand-made, and a spec
			// entry the list lacks is taken as deleted by hand.
			foreach ((string key, string wrote) in spec)
			{
				if (!now.TryGetValue(key, out string held))
				{
					marks[key] = Removed;
				}
				else if (held == wrote)
				{
					marks[key] = wrote;
				}
			}
			return marks;
		}

		private static Dictionary<string, int> FirstOf<T>(List<T> list, Func<T, string> id)
		{
			var firstAt = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int i = 0; i < list.Count; i++)
			{
				string key = id(list[i]);
				if (key != null && !firstAt.ContainsKey(key))
				{
					firstAt[key] = i;
				}
			}
			return firstAt;
		}
	}
}
