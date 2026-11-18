using System.Collections.Generic;
using System.Reflection;
using FishMMO.Server.Implementation;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the decision that stopped the connection token key refresh from reporting every
	/// poll as a Warning.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The refresh runs on a timer and logged its success at Warning each time, so three server
	/// roles produced better than five hundred Warnings across three hours to say that nothing had
	/// changed. That was most of what the server logs contained. A level used for the routine case
	/// cannot also mean "look at this", which is the same defect as the missing-health report in
	/// #157 and the body region report in #158.
	/// </para>
	/// <para>
	/// The signal that was worth keeping is a key set that actually changed, because that is a
	/// rotation and it explains why tokens which verified a moment ago stop doing so. These tests
	/// exist to hold that line exactly: silence when nothing moved, and a report when it did.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ConnectionTokenKeyRefreshTests
	{
		/// <summary>Calls the private comparison the refresh uses to choose its log level.</summary>
		private static bool KeySetChanged(
			Dictionary<string, byte[]> previous,
			Dictionary<string, byte[]> current)
		{
			MethodInfo method = typeof(BaseServerAuthenticator).GetMethod(
				"KeySetChanged",
				BindingFlags.Static | BindingFlags.NonPublic);

			LogAssert.IsNotNull(method, "the refresh must still decide whether the key set changed.");
			return (bool)method.Invoke(null, new object[] { previous, current });
		}

		private static Dictionary<string, byte[]> Keys(params string[] ids)
		{
			Dictionary<string, byte[]> keys = new Dictionary<string, byte[]>();
			for (int i = 0; i < ids.Length; i++)
			{
				keys[ids[i]] = new byte[32];
			}
			return keys;
		}

		[Test]
		public void ReloadingTheSameKeys_IsNotAChange()
		{
			/* The case that produced the flood. It is the normal outcome of every poll, so it has
			 * to be the quiet one or nothing else in the log can be seen. */
			LogAssert.IsFalse(KeySetChanged(Keys("key-a"), Keys("key-a")),
				"a refresh that loaded the same key it already held has nothing to report");
		}

		[Test]
		public void TheFirstLoad_IsAChange()
		{
			/* Nothing was held before it, and the server going from no keys to holding them is
			 * worth one line -- it is the difference between able and unable to verify a token. */
			LogAssert.IsTrue(KeySetChanged(null, Keys("key-a")),
				"the first successful load must be reported");
		}

		[Test]
		public void ARotatedKey_IsAChange()
		{
			/* The signal the old code buried. A rotation is exactly what explains tokens that
			 * verified a moment ago and now do not. */
			LogAssert.IsTrue(KeySetChanged(Keys("key-a"), Keys("key-b")),
				"a different key id is a rotation and must be visible");
		}

		[Test]
		public void AnAddedKey_IsAChange()
		{
			// Overlapping validity during a rotation: the new key appears before the old retires.
			LogAssert.IsTrue(KeySetChanged(Keys("key-a"), Keys("key-a", "key-b")),
				"an additional active key changes what can verify");
		}

		[Test]
		public void ARetiredKey_IsAChange()
		{
			LogAssert.IsTrue(KeySetChanged(Keys("key-a", "key-b"), Keys("key-a")),
				"a key dropping out changes what can verify");
		}

		[Test]
		public void TheSameKeysInADifferentOrder_AreNotAChange()
		{
			/* Guards against comparing sequences rather than sets. The database returns rows in no
			 * guaranteed order, so an order-sensitive comparison would call every second poll a
			 * rotation and reintroduce the flood in a form that looks like a real signal -- which
			 * would be worse than the original noise. */
			LogAssert.IsFalse(KeySetChanged(Keys("key-a", "key-b"), Keys("key-b", "key-a")),
				"key order is not meaningful and must not read as a rotation");
		}

		[Test]
		public void ASwappedKeySet_OfTheSameSize_IsAChange()
		{
			/* Guards against comparing counts alone, which is the cheap implementation and would
			 * miss the most important case: a full rotation that happens to keep the same number
			 * of active keys. */
			LogAssert.IsTrue(KeySetChanged(Keys("key-a", "key-b"), Keys("key-c", "key-d")),
				"an entirely different key set is a rotation, however many keys it holds");
		}
	}
}
