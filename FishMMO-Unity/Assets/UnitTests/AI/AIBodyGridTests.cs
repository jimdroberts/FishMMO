using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the per-scene body grid separation reads its neighbours from, in place of a physics
	/// overlap per NPC per brain tick.
	/// </summary>
	/// <remarks>
	/// What has to hold for the swap to be invisible: every body the old sphere could have
	/// weighed is returned (horizontal centre distance under the radius, across cell borders and
	/// for a radius larger than a cell), the asker is never its own neighbour, and a body on
	/// another level is not a neighbour.
	/// </remarks>
	[TestFixture]
	public class AIBodyGridTests
	{
		private readonly List<Vector3> positions = new List<Vector3>();
		private readonly List<uint> keys = new List<uint>();

		[SetUp]
		public void SetUp()
		{
			positions.Clear();
			keys.Clear();
		}

		[Test]
		public void FindsBodiesInsideTheRadius_AndNotOutside()
		{
			AIBodyGrid grid = new AIBodyGrid(2f);
			grid.Add(new Vector3(0.5f, 0f, 0f), 1u);
			grid.Add(new Vector3(1.4f, 0f, 0f), 2u);
			grid.Add(new Vector3(3f, 0f, 0f), 3u);

			int found = grid.Query(Vector3.zero, 1.5f, 3f, 99u, positions, keys);

			Assert.AreEqual(2, found);
			CollectionAssert.AreEquivalent(new[] { 1u, 2u }, keys);
		}

		[Test]
		public void NeverReturnsTheAsker()
		{
			AIBodyGrid grid = new AIBodyGrid();
			grid.Add(Vector3.zero, 7u);
			grid.Add(new Vector3(0.3f, 0f, 0f), 8u);

			grid.Query(Vector3.zero, 1f, 3f, 7u, positions, keys);

			CollectionAssert.AreEqual(new[] { 8u }, keys, "the asking NPC is excluded by its key, never returned as its own neighbour");
		}

		[Test]
		public void FindsNeighboursAcrossCellBorders_InEveryDirection()
		{
			AIBodyGrid grid = new AIBodyGrid(2f);
			// Centre sits just inside a cell corner; each neighbour is in a different adjacent cell.
			Vector3 centre = new Vector3(1.95f, 0f, 1.95f);
			grid.Add(centre + new Vector3(0.1f, 0f, 0f), 1u);
			grid.Add(centre + new Vector3(0f, 0f, 0.1f), 2u);
			grid.Add(centre + new Vector3(0.1f, 0f, 0.1f), 3u);
			grid.Add(centre + new Vector3(-0.1f, 0f, -0.1f), 4u);
			grid.Add(new Vector3(-2.5f, 0f, -2.5f), 5u);

			grid.Query(centre, 0.5f, 3f, 99u, positions, keys);

			CollectionAssert.AreEquivalent(new[] { 1u, 2u, 3u, 4u }, keys);
		}

		[Test]
		public void ARadiusWiderThanACell_ReadsEnoughCells()
		{
			AIBodyGrid grid = new AIBodyGrid(1f);
			grid.Add(new Vector3(3.5f, 0f, 0f), 1u);
			grid.Add(new Vector3(0f, 0f, -3.5f), 2u);
			grid.Add(new Vector3(4.5f, 0f, 0f), 3u);

			grid.Query(Vector3.zero, 4f, 3f, 99u, positions, keys);

			CollectionAssert.AreEquivalent(new[] { 1u, 2u }, keys);
		}

		[Test]
		public void ABodyOnAnotherLevel_IsNotANeighbour()
		{
			AIBodyGrid grid = new AIBodyGrid();
			grid.Add(new Vector3(0.2f, 6f, 0f), 1u);
			grid.Add(new Vector3(0.2f, 1.5f, 0f), 2u);

			grid.Query(Vector3.zero, 1f, 3f, 99u, positions, keys);

			CollectionAssert.AreEqual(new[] { 2u }, keys,
				"an NPC on a bridge overhead must not push the one below; one on the next stair must");
		}

		[Test]
		public void ClearEmptiesIt_AndItGrowsPastItsInitialCapacity()
		{
			AIBodyGrid grid = new AIBodyGrid();
			for (uint i = 0; i < 100; ++i)
			{
				grid.Add(new Vector3(0.001f * i, 0f, 0f), i);
			}
			Assert.AreEqual(100, grid.Count);
			Assert.AreEqual(99, grid.Query(Vector3.zero, 1f, 3f, 0u, positions, keys));

			grid.Clear();
			positions.Clear();
			keys.Clear();

			Assert.AreEqual(0, grid.Count);
			Assert.AreEqual(0, grid.Query(Vector3.zero, 1f, 3f, 0u, positions, keys));
		}

		[Test]
		public void NegativeCoordinates_AreBucketedCorrectly()
		{
			AIBodyGrid grid = new AIBodyGrid(2f);
			grid.Add(new Vector3(-0.1f, 0f, -0.1f), 1u);
			grid.Add(new Vector3(0.1f, 0f, 0.1f), 2u);

			grid.Query(new Vector3(0f, 0f, 0f), 0.5f, 3f, 99u, positions, keys);

			CollectionAssert.AreEquivalent(new[] { 1u, 2u }, keys, "bodies either side of the origin are one step apart, not in one cell by truncation");
		}
	}
}
