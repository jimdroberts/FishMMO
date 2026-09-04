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
		/// <summary>Rating used for banding and balancing: the season rating for ranked, the PvP Rank attribute otherwise.</summary>
		public readonly int Rating;

		public ArenaCandidate(long rowID, long characterID, long groupID, int rating = 0)
		{
			RowID = rowID;
			CharacterID = characterID;
			GroupID = groupID;
			Rating = rating;
		}
	}

	/// <summary>How a match is composed beyond seat counts.</summary>
	public readonly struct ArenaComposeOptions
	{
		/// <summary>
		/// Only candidates whose rating is within this many points of the longest-waiting candidate
		/// are considered. 0 means no band.
		/// </summary>
		public readonly int RatingBand;
		/// <summary>Assign units to teams by rating so team totals come out as even as first-come order allows.</summary>
		public readonly bool Balance;

		public static readonly ArenaComposeOptions None = new ArenaComposeOptions(0, false);

		public ArenaComposeOptions(int ratingBand, bool balance)
		{
			RatingBand = ratingBand;
			Balance = balance;
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
	/// <b>Banding and balancing</b> are opt-in through <see cref="ArenaComposeOptions"/>. A rating
	/// band keeps the match within reach of whoever has waited longest — the caller widens it as
	/// they wait, so a lonely high rating eventually finds a game. Balancing assigns units, largest
	/// and highest-rated first, to whichever team currently has the lower total rating and room,
	/// which keeps the two sides' averages close without abandoning the fairness of first-come
	/// eligibility: who plays is still decided by the queue, only which side they play on moves.
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
			return TryCompose(candidates, teamCount, teamSize, ArenaComposeOptions.None, out seats);
		}

		/// <summary>
		/// Tries to fill <paramref name="teamCount"/> teams of <paramref name="teamSize"/> from the
		/// candidates, in order, within a rating band and balanced when asked.
		/// </summary>
		public static bool TryCompose(IReadOnlyList<ArenaCandidate> candidates, int teamCount, int teamSize, ArenaComposeOptions options, out List<ArenaSeat> seats)
		{
			seats = null;
			if (candidates == null || teamCount < 2 || teamSize < 1 || candidates.Count < teamCount * teamSize)
			{
				return false;
			}

			/* The band is anchored on whoever has waited longest. Anybody outside it is left in
			 * the queue for a later pump with a wider band; a group is measured by its average. */
			IReadOnlyList<ArenaCandidate> eligible = candidates;
			if (options.RatingBand > 0)
			{
				int anchor = candidates[0].Rating;
				var inBand = new List<ArenaCandidate>(candidates.Count);
				var groupAverage = GroupAverages(candidates);
				foreach (ArenaCandidate c in candidates)
				{
					int rating = c.GroupID > 0 && groupAverage.TryGetValue(c.GroupID, out int avg) ? avg : c.Rating;
					if (Math.Abs(rating - anchor) <= options.RatingBand)
					{
						inBand.Add(c);
					}
				}
				eligible = inBand;
				if (eligible.Count < teamCount * teamSize)
				{
					return false;
				}
			}

			// Units in first-arrival order: a group is one unit positioned at its earliest member.
			var units = new List<List<ArenaCandidate>>();
			var unitByGroup = new Dictionary<long, List<ArenaCandidate>>();
			foreach (ArenaCandidate candidate in eligible)
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

			int needed = teamCount * teamSize;
			int seated;

			if (!options.Balance)
			{
				seated = 0;
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

					if (seated == needed)
					{
						break;
					}
				}
			}
			else
			{
				/* Who plays: the first units in queue order that fit and fill the match, exactly as
				 * above. Which side: those units re-sorted by size then rating, each placed on the
				 * team with the lower total rating that still has room. Size first, because a
				 * pre-made unit is the hardest thing to fit and must be placed while room remains. */
				var chosen = new List<List<ArenaCandidate>>();
				seated = 0;
				var probe = new int[teamCount];
				foreach (List<ArenaCandidate> unit in units)
				{
					if (unit.Count > teamSize)
					{
						continue;
					}
					// Provisional fit check with first-fit, so we never pick more than can be seated.
					bool fits = false;
					for (int t = 0; t < teamCount; ++t)
					{
						if (probe[t] + unit.Count <= teamSize)
						{
							probe[t] += unit.Count;
							fits = true;
							break;
						}
					}
					if (!fits)
					{
						continue;
					}
					chosen.Add(unit);
					seated += unit.Count;
					if (seated == needed)
					{
						break;
					}
				}

				if (seated != needed)
				{
					return false;
				}

				chosen.Sort((a, b) =>
				{
					int c = b.Count.CompareTo(a.Count);
					return c != 0 ? c : UnitRating(b).CompareTo(UnitRating(a));
				});

				var totals = new int[teamCount];
				foreach (List<ArenaCandidate> unit in chosen)
				{
					int best = -1;
					for (int t = 0; t < teamCount; ++t)
					{
						if (teams[t].Count + unit.Count > teamSize)
						{
							continue;
						}
						if (best < 0 || totals[t] < totals[best] || (totals[t] == totals[best] && teams[t].Count < teams[best].Count))
						{
							best = t;
						}
					}
					if (best < 0)
					{
						/* The size-sorted placement can, rarely, fail where first-fit succeeded
						 * (three pairs into two teams of three). Fall back to the order that was
						 * proven to fit. */
						return TryCompose(eligible, teamCount, teamSize, new ArenaComposeOptions(0, false), out seats);
					}
					teams[best].AddRange(unit);
					foreach (ArenaCandidate c in unit)
					{
						totals[best] += c.Rating;
					}
				}
			}

			if (seated != needed)
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

		/// <summary>Average rating of a unit, so a group is compared as one.</summary>
		private static int UnitRating(List<ArenaCandidate> unit)
		{
			if (unit == null || unit.Count == 0)
			{
				return 0;
			}
			long sum = 0;
			foreach (ArenaCandidate c in unit)
			{
				sum += c.Rating;
			}
			return (int)(sum / unit.Count);
		}

		private static Dictionary<long, int> GroupAverages(IReadOnlyList<ArenaCandidate> candidates)
		{
			var sums = new Dictionary<long, (long sum, int count)>();
			foreach (ArenaCandidate c in candidates)
			{
				if (c.GroupID <= 0)
				{
					continue;
				}
				sums.TryGetValue(c.GroupID, out var acc);
				sums[c.GroupID] = (acc.sum + c.Rating, acc.count + 1);
			}
			var result = new Dictionary<long, int>(sums.Count);
			foreach (var kvp in sums)
			{
				result[kvp.Key] = (int)(kvp.Value.sum / Math.Max(1, kvp.Value.count));
			}
			return result;
		}
	}
}
