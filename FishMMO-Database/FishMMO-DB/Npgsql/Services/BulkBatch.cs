using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Collapses a bulk batch to one row per key before it is written.
	/// </summary>
	internal static class BulkBatch
	{
		/// <summary>
		/// One row per key: the one with the highest version, the later of two equal ones. The
		/// surviving rows keep the position their key first appeared at.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An upsert cannot touch one row twice ("ON CONFLICT DO UPDATE command cannot affect row a
		/// second time"), and an UPDATE ... FROM matching one row twice is ambiguous, so a batch that
		/// names a key twice has to lose a row. The survivor is the one writing them one after
		/// another would have left: every write here is version-gated, so sequentially the highest
		/// version wins whatever order the rows came in. The services used to keep whichever came
		/// last in the list, so an older row that happened to follow a newer one replaced it.
		/// </para>
		/// <para>
		/// Callers count <see cref="BulkWriteResult.Supplied"/> BEFORE collapsing, so a dropped row
		/// shows as <see cref="BulkWriteResult.Filtered"/>, as that type documents. Counting after
		/// made a collision invisible: a caller that marks its state persisted when nothing was
		/// filtered then marked a row persisted that had never been written (issue #267).
		/// </para>
		/// </remarks>
		/// <param name="rows">The batch.</param>
		/// <param name="key">The identity two rows collide on.</param>
		/// <param name="version">A row's version.</param>
		/// <returns><paramref name="rows"/> itself when nothing collided; otherwise a new list.</returns>
		internal static List<T> KeepNewest<T, TKey>(List<T> rows, Func<T, TKey> key, Func<T, long> version)
		{
			if (rows.Count < 2)
			{
				return rows;
			}

			var positionOf = new Dictionary<TKey, int>(rows.Count);
			var kept = new List<T>(rows.Count);
			foreach (T row in rows)
			{
				TKey k = key(row);
				if (positionOf.TryGetValue(k, out int at))
				{
					if (version(row) >= version(kept[at]))
					{
						kept[at] = row;
					}
				}
				else
				{
					positionOf[k] = kept.Count;
					kept.Add(row);
				}
			}

			return kept.Count == rows.Count ? rows : kept;
		}
	}
}
