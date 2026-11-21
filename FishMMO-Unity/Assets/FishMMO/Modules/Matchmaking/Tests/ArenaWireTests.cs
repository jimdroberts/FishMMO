using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The arena messages on the wire (moved from KnowledgeSyncWireTests with the arena broadcasts).</summary>
	[TestFixture]
	public class ArenaWireTests
	{
		/// <summary>
		/// The pedestal at the end of an arena match keeps the seats it was sent.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The same defect as issue #265, found by the check above rather than reported: an array of
		/// an element with a hand-written serializer, inside a struct codegen does generate for. The
		/// server sends this to every participant the moment a match ends, with the placements
		/// ordered for the podium, and the field is the entire payload of that screen.
		/// </para>
		/// <para>
		/// Round-tripped rather than reasoned about, because "codegen will not synthesise it" is a
		/// claim about a build step. If FishNet can write it, this passes and the check above is
		/// wrong about the mechanism; if it cannot, the seats are lost here, in front of us.
		/// </para>
		/// </remarks>
		[Test]
		public void TheArenaResultsReachThePedestal()
		{
			ArenaResultsBroadcast received = FishMMO.UnitTests.Harness.WireRoundTrip.RoundTrip(new ArenaResultsBroadcast
			{
				ArenaTemplateID = 4242,
				Format = 3,
				WinnerTeam = 0,
				TeamScores = new[] { 3, 1 },
				Placements = new[]
				{
					new ArenaMemberEntry
					{
						CharacterID = 11, Team = 0, Kills = 3, Deaths = 1, Score = 300,
						Present = true, Ready = true, Reconnecting = false,
					},
					new ArenaMemberEntry
					{
						CharacterID = 22, Team = 1, Kills = 1, Deaths = 3, Score = 100,
						Present = true, Ready = false, Reconnecting = true,
					},
				},
				RankDelta = 12,
				Ranked = true,
				RatingDelta = 14,
				NewRating = 1514,
				PlacementGamesRemaining = 0,
			});

			/* The scalar fields are the control: they arrive even when the array does not, which is
			 * what makes the loss silent. A match that ends and shows an empty podium looks like a
			 * match nobody placed in, not like a message that failed to write. */
			LogAssert.AreEqual(4242, received.ArenaTemplateID, "the match must still name its template");
			LogAssert.AreEqual(0, received.WinnerTeam, "the winning team must still arrive");
			LogAssert.AreEqual(12, received.RankDelta, "the rank change must still arrive");
			LogAssert.AreEqual(14, received.RatingDelta, "the rating change must still arrive");
			LogAssert.AreEqual(1514, received.NewRating, "the new rating must still arrive");

			LogAssert.IsNotNull(received.Placements, "the podium must arrive carrying seats, not null");
			LogAssert.AreEqual(2, received.Placements.Length, "every seat must survive");

			LogAssert.AreEqual(11L, received.Placements[0].CharacterID, "the first seat must be the winner");
			LogAssert.AreEqual(300, received.Placements[0].Score, "the first seat must keep its score");
			LogAssert.AreEqual(22L, received.Placements[1].CharacterID, "the second seat must survive");
			LogAssert.IsTrue(received.Placements[1].Reconnecting,
				"the packed flags must survive; they share one byte by design");
		}
	}
}
