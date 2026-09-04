using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that an ability never spends a hit on the character that cast it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>AbilityObjectSweep.Accept</c> excludes the ability object and its children, which is the
	/// correct rule for the object itself — but the caster is a separate body, and a melee ability
	/// spawns INSIDE it. The caster's capsule was in the overlap on the first tick and was accepted
	/// ahead of anything else: 17 of 27 swept hits in one play session resolved to the caster.
	/// </para>
	/// <para>
	/// Each of those claimed a slot in the per-object dedupe and spent a hit from <c>HitCount</c>,
	/// so an ability permitted a single strike was often used up on the player who cast it before
	/// it reached what they aimed at. That is what made melee damage appear intermittent — the same
	/// swing landing on one target and doing nothing to the next — and what let a target with more
	/// health survive indefinitely.
	/// </para>
	/// <para>
	/// It also produced the misleading signature that sent this investigation down two wrong paths:
	/// a damage trigger reporting <c>target Elf(Clone)</c> for a player attacking a monster, which
	/// reads as broken target selection rather than a hit that genuinely landed on the caster.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilitySelfHitTests
	{
		private const string ObjectPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObject.cs";

		private const string SweepPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/AbilityObjectSweep.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>The body of a named method, bounded by the next member's signature.</summary>
		/// <remarks>
		/// Bounded by a signature alone, with no embedded newline: a bound spanning a line break
		/// depends on whether the file is stored CRLF or LF, which git rewrites on checkout.
		/// </remarks>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void ASweptHitOnTheCasterIsDropped()
		{
			string body = MethodBody(ReadSource(ObjectPath),
				"private bool DispatchSweptHit", "private bool ApplyHit");

			LogAssert.IsTrue(body.Contains("Caster"),
				"the dispatch must compare the resolved character against the caster");
		}

		[Test]
		public void TheCasterIsDroppedBeforeTheHitIsSpent()
		{
			/* Order is the whole point. ApplyHit is what claims the dedupe slot and decrements
			 * HitCount, so a caster filtered AFTER it would still consume the ability. */
			string body = MethodBody(ReadSource(ObjectPath),
				"private bool DispatchSweptHit", "private bool ApplyHit");

			int casterCheck = body.IndexOf("Caster", StringComparison.Ordinal);
			int applyHit = body.IndexOf("ApplyHit(", StringComparison.Ordinal);

			LogAssert.IsTrue(casterCheck >= 0 && applyHit > casterCheck,
				"the caster must be dropped before ApplyHit, or the hit is spent on them anyway");
		}

		[Test]
		public void TheSweepKeepsItsOwnGeometricRule()
		{
			/* The sweep excludes the ability object and its children and knows nothing about who
			 * cast it. Keeping it that way is deliberate: it has one rule, expressed in geometry,
			 * and the caster is a question about identity that belongs where the body has been
			 * resolved to a character. */
			string body = MethodBody(ReadSource(SweepPath),
				"private static bool Accept", "/// <summary>");

			LogAssert.IsTrue(body.Contains("IsChildOf"),
				"the sweep must still exclude the ability object and its own children");
		}
	}
}
