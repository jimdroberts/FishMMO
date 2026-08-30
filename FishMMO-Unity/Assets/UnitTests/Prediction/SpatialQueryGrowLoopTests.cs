using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every non-allocating spatial query in the project must re-run itself until its buffer stops
	/// coming back full.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this is a correctness rule and not a tuning one.</b> A non-allocating physics query
	/// returns at most <c>buffer.Length</c> results and says nothing about how many it discarded,
	/// and the ones it discarded were chosen by the broadphase — an order that is neither
	/// reproducible between runs nor agreed between peers. Every ranking, cap, dedupe or
	/// "best candidate" decision applied afterwards is therefore ordering an arbitrary subset the
	/// moment the crowd outgrows the buffer. That failure has no symptom: the ability simply picks
	/// different victims, or the NPC simply never notices half its attackers.
	/// </para>
	/// <para>
	/// <b>Asserted on the source, deliberately.</b> Reproducing it behaviourally needs a populated
	/// <c>PhysicsScene</c> with more colliders than the buffer holds, which an EditMode test cannot
	/// build. The rule is a call-site shape, so the call site is what is checked — the same approach
	/// <c>PositionConditionTests.AllCharactersSelector_GathersUnderARewindScope</c> takes.
	/// </para>
	/// <para>
	/// This test exists because a comment in <c>BaseAIState.SweepForEnemies</c> asserted the sweep
	/// there "was the last spatial query in the project without the grow loop" while
	/// <c>HealerAttackingState</c>'s ally scan still read a fixed twenty-entry buffer. A claim about
	/// a project-wide invariant belongs in a test rather than in a comment nobody re-checks.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class SpatialQueryGrowLoopTests
	{
		/// <summary>
		/// Files holding a buffered spatial query that must grow, relative to the project root.
		/// </summary>
		/// <remarks>
		/// Listed rather than discovered so that adding a query to a new file is a deliberate act:
		/// a sweep over every <c>.cs</c> would silently pass for a file it had not been taught about.
		/// </remarks>
		private static readonly string[] BufferedQuerySites =
		{
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/AreaTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/ConeTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/ChainTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/RandomTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/NearestTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/FurthestTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/ECA/Target/LineTargetSelector.cs",
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/LagCompensatedQuery.cs",
			"Assets/Scripts/Shared/Implementation/Entity/NPC/AI/BaseAIState.cs",
			"Assets/Scripts/Shared/Implementation/Entity/NPC/AI/States/HealerAttackingState.cs",
		};

		/// <summary>
		/// Every listed call site re-queries through <c>TargetOrdering.TryGrowQueryBuffer</c>.
		/// </summary>
		/// <remarks>
		/// <c>ApplyThreatAction</c> and <c>AbilityObjectSweep</c> are deliberately absent: both grow
		/// their buffers with their own inline doubling loop against a local maximum, which is the
		/// same guarantee reached a different way. What must never appear is a buffered query with
		/// no growth at all, which is what this asserts.
		/// </remarks>
		[Test]
		public void EveryBufferedSpatialQuery_GrowsUntilTheBufferIsNotFull()
		{
			List<string> offenders = new List<string>();

			for (int i = 0; i < BufferedQuerySites.Length; ++i)
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), BufferedQuerySites[i]);
				LogAssert.IsTrue(File.Exists(path), $"Spatial query site not found at {path}.");

				/* The CALL, not the identifier. Matching the bare name passes on a file that only
				 * mentions the helper in a doc comment — which is exactly the state
				 * HealerAttackingState was left in, and exactly the failure this test exists to
				 * catch. Every real call site passes its buffer by reference, because the helper
				 * replaces the array rather than resizing it. */
				string source = File.ReadAllText(path);
				if (!Regex.IsMatch(source, @"TryGrowQueryBuffer\s*\(\s*ref\s"))
				{
					offenders.Add(BufferedQuerySites[i]);
				}
			}

			LogAssert.IsTrue(offenders.Count == 0,
				"These spatial queries read a fixed buffer and never re-query when it comes back full, " +
				"so the broadphase — not the code — chooses which candidates survive: " +
				string.Join(", ", offenders) + ". Wrap the query in the loop from " +
				"TargetOrdering.TryGrowQueryBuffer.");
		}

		/// <summary>
		/// The healer's ally scan in particular, which is the site this test was written for.
		/// </summary>
		/// <remarks>
		/// Called out separately because its failure is the least visible of the set. The scan keeps
		/// a single best candidate — the most wounded ally — so a truncated buffer does not produce a
		/// partial result that anyone could notice; it produces a healer that confidently heals the
		/// wrong ally, or nobody, in exactly the fights big enough to need a healer.
		/// </remarks>
		[Test]
		public void HealerAllyScan_UsesAGrowableBuffer()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/NPC/AI/States/HealerAttackingState.cs");
			LogAssert.IsTrue(File.Exists(path), $"HealerAttackingState.cs not found at {path}.");

			string source = File.ReadAllText(path);

			LogAssert.IsTrue(source.Contains("TryGrowQueryBuffer(ref allyHits"),
				"HealerAttackingState.FindMostInjuredAlly must re-run its OverlapSphere through " +
				"TargetOrdering.TryGrowQueryBuffer. A fixed buffer lets the broadphase decide which " +
				"allies the healer can see at all.");

			/* readonly would defeat the growth: TryGrowQueryBuffer REPLACES the array rather than
			 * resizing it, so the field has to be assignable. Matched on the declaration so a future
			 * edit that re-adds readonly fails here rather than at compile time in an unrelated file. */
			LogAssert.IsFalse(
				Regex.IsMatch(source, @"static\s+readonly\s+Collider\[\]\s+allyHits"),
				"allyHits must not be readonly: TryGrowQueryBuffer replaces the array rather than " +
				"resizing it in place.");
		}
	}
}
