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

			typeof(PlotFoundation)
				.GetField("plotKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
				.SetValue(foundation, plotKey);
			typeof(PlotFoundation)
				.GetField("price", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
				.SetValue(foundation, price);

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
	}
}
