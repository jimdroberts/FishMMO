using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client naming system releases what it acquires per name request. Split from EquipmentVisualLifetimeTests when
	/// the equipment visual controller moved to the Appearance module.
	/// </summary>
	/// <remarks>
	/// <para>Source scans, because every defect pinned here was invisible to behaviour: the
	/// picture was right and the memory was wrong. They came out of the hunt for a
	/// client that grew about a megabyte a minute in a world scene and were verified by reading
	/// the Addressables 3.1 source, not by a profiler — so the pins say exactly what the code
	/// must keep doing, and a profiler run in the client is what confirms the rate.</para>
	/// <list type="bullet">
	/// <item>The server answers a name request over its budget with nothing; the client re-sends a
	/// pending request instead of appending callbacks to it forever, and releases one the server
	/// answered "no such entity".</item>
	/// </list>
	/// </remarks>
	[TestFixture]
	public class NameRequestLifetimeTests
	{
		private const string NamingPath = "Assets/FishMMO/Client/ClientNamingSystem.cs";

		[Test]
		public void APendingNameRequestIsSentAgainInsteadOfWaitingForever()
		{
			/* The retry rule itself moved into PendingNameRequests, where NamingBatchTests pins it by
			 * behaviour; this pins that SetName and the per-frame flush go through it. */
			string code = CodeOnly(Read(NamingPath));
			string setName = MethodBody(code, "public static void SetName(NamingSystemType type, long id, Action<string> action)");
			LogAssert.IsTrue(setName.Contains("new PendingNameRequests(NameRequestRetrySeconds)"), "pending requests re-send after the retry window");
			LogAssert.IsTrue(setName.Contains("requests.Request(id, action,"), "SetName queues the ask, chaining its callback onto any already waiting");
			LogAssert.AreEqual(0, CountOccurrences(setName, "Client.Broadcast("), "SetName sends nothing itself: a frame's asks leave as one batch");

			string flush = MethodBody(code, "private static void FlushNameRequests()");
			LogAssert.IsTrue(flush.Contains("TakeBatch(now, NamingRequestBatchBroadcast.MaxIDs, nameBatchScratch)"), "the flush stamps what it sends, a bounded batch at a time");
			LogAssert.AreEqual(1, CountOccurrences(flush, "new NamingRequestBatchBroadcast()"), "one request per type per frame");

			string answer = MethodBody(code, "private static void ApplyNameAnswer(NamingSystemType type, long id, string name, double now)");
			LogAssert.IsTrue(answer.Contains("requests?.ResolveMissing(id, now)"), "a not-found answer releases what was waiting instead of holding it for the session");
		}

		// ── helpers ───────────────────────────────────────────────────────────

		private static string Read(string path)
		{
			LogAssert.IsTrue(ProjectFiles.Exists(path), path + " exists");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int CountOccurrences(string text, string needle)
		{
			int count = 0;
			for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>Brace-matches the body following the first occurrence of <paramref name="signature"/>.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "found: " + signature);
			int open = source.IndexOf('{', start);
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{') ++depth;
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail("unterminated body for " + signature);
			return string.Empty;
		}

		/// <summary>Strips block comments and comment lines so prose cannot trip a code scan.</summary>
		private static string CodeOnly(string source)
		{
			while (true)
			{
				int open = source.IndexOf("/*", StringComparison.Ordinal);
				if (open < 0)
				{
					break;
				}

				int close = source.IndexOf("*/", open + 2, StringComparison.Ordinal);
				source = close < 0 ? source.Substring(0, open) : source.Remove(open, close - open + 2);
			}

			StringBuilder kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				kept.Append(line).Append('\n');
			}

			return kept.ToString();
		}
	}
}
