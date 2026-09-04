using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One waiter as the composer sees it: who, which pre-made group they queued with, and where
	/// they stand in line.
	/// </summary>
	public readonly struct ArenaCandidate
	{
		/// <summary>Queue row id, so the caller can mark exactly the rows that were taken.</summary>
		public readonly long RowID;
		/// <summary>The character.</summary>
		public readonly long CharacterID;
		/// <summary>Pre-made group this character queued with, or 0 when they queued alone.</summary>
		public readonly long GroupID;

		public ArenaCandidate(long rowID, long characterID, long groupID)
		{
			RowID = rowID;
			CharacterID = characterID;
			GroupID = groupID;
		}
	}

	/// <summary>
	/// One seat in a composed match.
	/// </summary>
	public readonly struct ArenaSeat
	{
		public readonly long RowID;
		public readonly long CharacterID;
		public readonly int Team;

		public ArenaSeat(long rowID, long characterID, int team)
		{
			RowID = rowID;
			CharacterID = characterID;
			Team = team;
		}
	}

	/// <summary>
	/// Decides which waiters fill which team of an arena match. Pure, so it is testable without a
	/// database and so the transaction that applies it has nothing to decide.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Pre-made groups are kept together and only ever fill a team of their own size or
	/// larger.</b> A group of three queued for 3v3 is one team; a group of three queued for 4v4
	/// is three of a team's four seats and one solo player takes the fourth. A group larger than
	/// a team can never be seated and is skipped — it should have been refused at queue time, and
	/// the composer does not paper over that by splitting it.
	/// </para>
	/// <para>
	/// <b>First come, first served.</b> Candidates arrive in queue order. Teams are filled in that
	/// order with a first-fit packing: each unit (a group, or a solo) goes on the lowest-numbered
	/// team it fits on. Two groups that could complete a match but were behind a solo who fits
	/// anywhere still get seated, because the solo takes one seat and leaves the rest. This is
	/// not an optimal bin packing and does not try to be; the queue is expected to be short and
	/// fairness by wait time matters more than squeezing one extra match out of an unlucky
	/// arrangement.
	/// </para>
	/// <para>
	/// A match is composed only when <em>every</em> team is exactly full. Nothing partial is ever
	/// returned, because a match with uneven teams is not a match.
	/// </para>
	/// </remarks>
	public static class ArenaMatchComposer
	{
		/// <summary>
		/// Tries to fill <paramref name="teamCount"/> teams of <paramref name="teamSize"/> from the
		/// candidates, in order.
		/// </summary>
		/// <param name="candidates">Eligible waiters in queue order (oldest first). Members of one group must all be present; a group missing members is treated as the members present.</param>
		/// <param name="teamCount">Teams to fill. At least 2.</param>
		/// <param name="teamSize">Seats per team. At least 1.</param>
		/// <param name="seats">Receives one seat per player when a match was composed.</param>
		/// <returns>True when every team is exactly full.</returns>
		public static bool TryCompose(IReadOnlyList<ArenaCandidate> candidates, int teamCount, int teamSize, out List<ArenaSeat> seats)
		{
			seats = null;
			if (candidates == null || teamCount < 2 || teamSize < 1 || candidates.Count < teamCount * teamSize)
			{
				return false;
			}

			// Units in first-arrival order: a group is one unit positioned at its earliest member.
			var units = new List<List<ArenaCandidate>>();
			var unitByGroup = new Dictionary<long, List<ArenaCandidate>>();
			foreach (ArenaCandidate candidate in candidates)
			{
				if (candidate.GroupID > 0)
				{
					if (!unitByGroup.TryGetValue(candidate.GroupID, out List<ArenaCandidate> unit))
					{
						unit = new List<ArenaCandidate>();
						unitByGroup[candidate.GroupID] = unit;
						units.Add(unit);
					}
					unit.Add(candidate);
				}
				else
				{
					units.Add(new List<ArenaCandidate>(1) { candidate });
				}
			}

			var teams = new List<List<ArenaCandidate>>(teamCount);
			for (int i = 0; i < teamCount; ++i)
			{
				teams.Add(new List<ArenaCandidate>(teamSize));
			}

			int seated = 0;
			foreach (List<ArenaCandidate> unit in units)
			{
				if (unit.Count > teamSize)
				{
					// Cannot be seated in this format; leave them for a format that fits.
					continue;
				}

				for (int t = 0; t < teamCount; ++t)
				{
					if (teams[t].Count + unit.Count <= teamSize)
					{
						teams[t].AddRange(unit);
						seated += unit.Count;
						break;
					}
				}

				if (seated == teamCount * teamSize)
				{
					break;
				}
			}

			if (seated != teamCount * teamSize)
			{
				return false;
			}

			seats = new List<ArenaSeat>(seated);
			for (int t = 0; t < teamCount; ++t)
			{
				foreach (ArenaCandidate member in teams[t])
				{
					seats.Add(new ArenaSeat(member.RowID, member.CharacterID, t));
				}
			}
			return true;
		}
	}
}
