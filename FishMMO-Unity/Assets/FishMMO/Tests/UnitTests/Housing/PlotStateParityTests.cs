using System;
using FishMMO.Database.Data;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The stored plot state column and <see cref="PlotState"/> must stay numerically identical.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>PlotEntity.State</c> and <c>PlotData.State</c> are plain integers because the database
	/// assembly cannot reference the Unity shared assembly. Every reader turns one back into a
	/// <see cref="PlotState"/>, and every writer casts the other way — correct exactly as long as
	/// the numbers mean what this build thinks they mean.
	/// </para>
	/// <para>
	/// <b>What breaks if they drift.</b> State decides who may enter a plot. A shift of one would
	/// make every occupied house read as a building site — shutting out the friends its owner
	/// invited — and every building site read as an abandoned lot, which admits nobody and is
	/// claimable by anybody. Nothing would log an error; the first report would be a player saying
	/// somebody else bought the plot they were halfway through building on.
	/// </para>
	/// <para>
	/// The values are also persisted, so a renumbering reinterprets every row already written.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlotStateParityTests
	{
		[Test]
		public void ClaimDefault_MatchesTheStateClaimingProduces()
		{
			/* IPlotService.TryClaimAsync defaults claimedState to 1 rather than naming the shared
			 * enum it cannot see. If OnClaimed ever stopped being Building, every caller that took
			 * the default would silently write the wrong state. */
			Assert.AreEqual(1, (int)PlotStateExtensions.OnClaimed(),
				"IPlotService.TryClaimAsync defaults its claimed state to 1; that must still be Building.");
		}

		[Test]
		public void ReleaseDefault_MatchesTheStateReleasingProduces()
		{
			Assert.AreEqual(0, (int)PlotStateExtensions.OnReleased(),
				"IPlotService.ReleaseAsync defaults its released state to 0; that must still be Empty.");
		}

		[Test]
		public void TheRegisteredDefault_IsEmpty()
		{
			/* Registration inserts state 0 explicitly and the column defaults to 0, which is also
			 * what every row written before the column existed reads as. All three have to be the
			 * unclaimed lot. */
			Assert.AreEqual(0, (int)PlotState.Empty,
				"Plot rows are registered and defaulted at 0; that must still be Empty.");
		}

		[Test]
		public void EveryStoredStateRoundTripsThroughPlotData()
		{
			// The DTO carries the column verbatim; readers go through FromStored.
			foreach (PlotState state in Enum.GetValues(typeof(PlotState)))
			{
				PlotData row = new PlotData(1, 1, "scene", "key", 0, 0, null, null, null, (int)state);

				Assert.AreEqual(state, PlotStateExtensions.FromStored(row.State),
					$"PlotState.{state} did not survive the trip through the data transfer object.");
			}
		}

		[Test]
		public void NoTwoStatesShareAValue()
		{
			Array values = Enum.GetValues(typeof(PlotState));
			var seen = new System.Collections.Generic.HashSet<int>();

			foreach (PlotState state in values)
			{
				Assert.IsTrue(seen.Add((int)state),
					$"PlotState.{state} shares its stored value with another state, so the two are indistinguishable in a row.");
			}
		}
	}
}
