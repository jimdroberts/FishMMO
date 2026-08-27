using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for <see cref="PlotFoundation"/>'s authored state.
	/// </summary>
	/// <remarks>
	/// Covers what the component decides on its own. Registration and claiming are not exercised
	/// here: both need a live scene name and a server behaviour holding a database connection, so
	/// they are integration concerns rather than unit ones.
	/// </remarks>
	[TestFixture]
	public class PlotFoundationTests
	{
		private readonly List<GameObject> created = new List<GameObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (GameObject go in created)
			{
				if (go != null)
				{
					Object.DestroyImmediate(go);
				}
			}
			created.Clear();
		}

		/// <summary>
		/// Builds a foundation with an authored key and price.
		/// </summary>
		/// <remarks>
		/// No lifecycle is involved: EditMode does not run <c>Awake</c>, and it does not need to.
		/// The key is derived on demand precisely so that reading it does not depend on the object
		/// having been started.
		/// </remarks>
		private PlotFoundation CreateFoundation(string plotKey, long price = 0)
		{
			GameObject go = new GameObject("Foundation");
			created.Add(go);

			PlotFoundation foundation = go.AddComponent<PlotFoundation>();

			SetField(foundation, "plotKey", plotKey);
			SetField(foundation, "price", price);

			return foundation;
		}

		/// <summary>
		/// Sets one of the component's authored fields.
		/// </summary>
		private static void SetField(PlotFoundation foundation, string name, object value)
		{
			typeof(PlotFoundation)
				.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
				.SetValue(foundation, value);
		}

		/// <summary>
		/// Builds a foundation of a given footprint at a given position.
		/// </summary>
		private PlotFoundation CreateSizedFoundation(Vector3 position, float width, float depth, float height)
		{
			PlotFoundation foundation = CreateFoundation("plot");
			foundation.transform.position = position;

			SetField(foundation, "dimensions", new Vector2(width, depth));
			SetField(foundation, "height", height);

			return foundation;
		}

		/// <summary>
		/// The stored key has to match what registration wrote to the database, and registration
		/// canonicalises. A foundation keeping the authored casing would look up a row that is not
		/// there.
		/// </summary>
		[Test]
		public void PlotKey_IsCanonicalised()
		{
			Assert.AreEqual("riverside_01", CreateFoundation("  Riverside_01  ").PlotKey);
		}

		[Test]
		public void AnEmptyKey_LeavesTheFoundationUnusable()
		{
			Assert.IsEmpty(CreateFoundation("   ").PlotKey);
		}

		/// <summary>
		/// Rejected rather than truncated: a truncated key would silently collide with any other
		/// plot sharing its first 64 characters.
		/// </summary>
		[Test]
		public void AnOverlongKey_LeavesTheFoundationUnusable()
		{
			string key = new string('a', PlotIdentity.MaxPlotKeyLength + 1);

			Assert.IsEmpty(CreateFoundation(key).PlotKey);
		}

		[Test]
		public void AKeyExactlyAtTheLimit_IsAccepted()
		{
			string key = new string('a', PlotIdentity.MaxPlotKeyLength);

			Assert.AreEqual(key, CreateFoundation(key).PlotKey);
		}

		/// <summary>
		/// A plot is unclaimable until the server has matched it to a row, so it must not start out
		/// looking resolved.
		/// </summary>
		[Test]
		public void ANewFoundation_IsUnresolvedAndUnowned()
		{
			PlotFoundation foundation = CreateFoundation("plot");

			Assert.AreEqual(0, foundation.PlotID);
			Assert.IsFalse(foundation.IsResolved);
			Assert.AreEqual(PlotOwner.None, foundation.Owner);
		}

		[Test]
		public void ApplyResolvedState_RecordsTheRowAndItsOwner()
		{
			PlotFoundation foundation = CreateFoundation("plot");

			foundation.ApplyResolvedState(17, PlotOwner.ForCharacter(42));

			Assert.AreEqual(17, foundation.PlotID);
			Assert.IsTrue(foundation.IsResolved);
			Assert.AreEqual(PlotOwner.ForCharacter(42), foundation.Owner);
		}

		[Test]
		public void ApplyOwner_ChangesOwnershipWithoutDisturbingTheRow()
		{
			PlotFoundation foundation = CreateFoundation("plot");
			foundation.ApplyResolvedState(17, PlotOwner.None);

			foundation.ApplyOwner(PlotOwner.ForCharacter(42));

			Assert.AreEqual(17, foundation.PlotID);
			Assert.AreEqual(PlotOwner.ForCharacter(42), foundation.Owner);
		}

		/// <summary>
		/// A negative price would be a purchase that pays the buyer, so it is floored rather than
		/// trusted.
		/// </summary>
		[Test]
		public void ANegativePrice_ReadsAsFree()
		{
			Assert.AreEqual(0, CreateFoundation("plot", -100).Price);
		}

		[Test]
		public void APrice_IsReportedAsAuthored()
		{
			Assert.AreEqual(250, CreateFoundation("plot", 250).Price);
		}
		/// <summary>
		/// One Unity unit is one metre, so an authored footprint is reported in metres unchanged.
		/// </summary>
		[Test]
		public void Dimensions_AreReportedAsAuthored()
		{
			PlotFoundation foundation = CreateSizedFoundation(Vector3.zero, 24f, 16f, 10f);

			Assert.AreEqual(new Vector2(24f, 16f), foundation.Dimensions);
			Assert.AreEqual(10f, foundation.Height);
		}

		/// <summary>
		/// A zero or negative edge would make every point fall outside the plot, which reads as
		/// every placement being out of bounds rather than as the authoring mistake it is.
		/// </summary>
		[TestCase(0f, 0f, 0f)]
		[TestCase(-5f, -5f, -5f)]
		public void NonPositiveExtents_AreFlooredAtTheMinimum(float width, float depth, float height)
		{
			PlotFoundation foundation = CreateSizedFoundation(Vector3.zero, width, depth, height);

			Assert.AreEqual(PlotFoundation.MinimumExtent, foundation.Dimensions.x);
			Assert.AreEqual(PlotFoundation.MinimumExtent, foundation.Dimensions.y);
			Assert.AreEqual(PlotFoundation.MinimumExtent, foundation.Height);
		}

		/// <summary>
		/// The transform marks the ground at the centre of the plot, so the volume is centred
		/// horizontally on it and rests on top of it.
		/// </summary>
		[Test]
		public void Bounds_AreCentredOnTheFoundationAndRestOnIt()
		{
			PlotFoundation foundation = CreateSizedFoundation(new Vector3(10f, 5f, -20f), 20f, 10f, 8f);
			Bounds bounds = foundation.Bounds;

			Assert.AreEqual(new Vector3(10f, 9f, -20f), bounds.center);
			Assert.AreEqual(new Vector3(20f, 8f, 10f), bounds.size);
			Assert.AreEqual(5f, bounds.min.y, 0.0001f, "the plot should rest on the foundation, not straddle it");
		}

		[Test]
		public void Contains_AcceptsAPointInsideThePlot()
		{
			PlotFoundation foundation = CreateSizedFoundation(Vector3.zero, 20f, 20f, 10f);

			Assert.IsTrue(foundation.Contains(new Vector3(5f, 1f, -5f)));
		}

		/// <summary>
		/// The plot is a box, not an infinite column: a plot on a cliff must not own the sky above
		/// it, and one under a bridge must not own the bridge.
		/// </summary>
		[Test]
		public void Contains_RejectsAPointAboveThePlot()
		{
			PlotFoundation foundation = CreateSizedFoundation(Vector3.zero, 20f, 20f, 10f);

			Assert.IsFalse(foundation.Contains(new Vector3(0f, 50f, 0f)));
		}

		[Test]
		public void Contains_RejectsAPointBeyondTheEdge()
		{
			PlotFoundation foundation = CreateSizedFoundation(Vector3.zero, 20f, 20f, 10f);

			Assert.IsFalse(foundation.Contains(new Vector3(11f, 1f, 0f)));
		}

		/// <summary>
		/// Below the foundation is outside it, so a basement dug under a plot is not on the plot.
		/// </summary>
		[Test]
		public void Contains_RejectsAPointBelowTheFoundation()
		{
			PlotFoundation foundation = CreateSizedFoundation(new Vector3(0f, 10f, 0f), 20f, 20f, 10f);

			Assert.IsFalse(foundation.Contains(new Vector3(0f, 5f, 0f)));
		}
	}
}
