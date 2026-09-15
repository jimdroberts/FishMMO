using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every hand-written broadcast array reader bounds the length it reads off the wire
	/// before allocating an array from it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A hand-written array reader has the same shape everywhere in the network layer: read an
	/// <c>int</c> length, treat a negative as the sender's null sentinel, then
	/// <c>new T[length]</c>. The length comes straight off the stream, so with nothing between the
	/// read and the allocation a single wrong <c>int</c> becomes an allocation of up to
	/// <see cref="int.MaxValue"/> elements — a multi-gigabyte request that fails long before the
	/// reader discovers there were never that many entries behind it.
	/// </para>
	/// <para>
	/// These are server → client paths, so the honest threat is not a hostile client: a client
	/// cannot send itself an inventory sync. It is a mangled or truncated stream — a reader that has
	/// lost alignment and is interpreting the middle of some other field as a length prefix. That is
	/// a corruption-hardening concern rather than a security one, and the bounds are commented that
	/// way deliberately, because a comment that overstates its threat is the kind a future reader
	/// stops believing.
	/// </para>
	/// <para>
	/// <c>ArenaBroadcastSerializers.ReadArenaMemberEntryArray</c> established the pattern and the
	/// reasoning for it; seven readers added later (inventory, bank, known abilities, known ability
	/// events, crafted abilities, achievements, factions) shipped without it. This fixture exists so
	/// the eighth is not written the same way. It reads source rather than exercising the readers,
	/// for the same reason <c>BroadcastArraySerializerTests</c> does: the failure is a MISSING guard,
	/// and there is no round trip that demonstrates an absence.
	/// </para>
	/// <para>
	/// Reading source has the matching limitation — it can only see that a bound was written, not
	/// that it is the right number. Whether a bound truncates legitimate data is a question about
	/// game content and is answered in the remarks on each constant, not here. The one thing this
	/// fixture does enforce about the value is that it is a NAMED constant, because that is where
	/// those remarks can live.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BroadcastArrayLengthBoundTests
	{
		private static string NetworkRoot =>
			Path.Combine(Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Network");

		/// <summary>One hand-written array reader: where it lives and what its body says.</summary>
		private struct ArrayReader
		{
			/// <summary>File name alone — enough to identify it in a failure message.</summary>
			public string File;
			/// <summary>The reader's method name, e.g. <c>ReadFactionUpdateBroadcastArray</c>.</summary>
			public string Method;
			/// <summary>The method body, braces included, line endings normalised.</summary>
			public string Body;
			/// <summary>Full source of the declaring file, line endings normalised.</summary>
			public string Source;
		}

		/// <summary>
		/// Every <c>Read*Array(this Reader reader)</c> under the network layer, with its body.
		/// </summary>
		/// <remarks>
		/// Discovered from source rather than listed, so a reader written next week is covered
		/// without anyone remembering to add it here — which is the only way a guard-presence test
		/// keeps earning its place.
		/// </remarks>
		private static List<ArrayReader> ArrayReaders()
		{
			List<ArrayReader> readers = new List<ArrayReader>();

			/* The tree is CRLF on Windows. Normalising once here means the patterns below never have
			 * to spell out an optional carriage return, and a failure message never carries a stray
			 * \r into the test log. */
			foreach (string file in Directory.GetFiles(NetworkRoot, "*.cs", SearchOption.AllDirectories))
			{
				string source = File.ReadAllText(file).Replace("\r\n", "\n");

				foreach (Match match in Regex.Matches(source,
					"\\b(Read[A-Za-z0-9_]*Array)\\s*\\(\\s*this\\s+Reader\\s+\\w+\\s*\\)"))
				{
					string body = MethodBody(source, match.Index + match.Length);
					if (body == null)
					{
						continue;
					}

					readers.Add(new ArrayReader()
					{
						File = Path.GetFileName(file),
						Method = match.Groups[1].Value,
						Body = body,
						Source = source,
					});
				}
			}

			return readers;
		}

		/// <summary>
		/// Returns the brace-matched body that follows <paramref name="from"/>, or null.
		/// </summary>
		/// <remarks>
		/// Brace matched rather than taken as a fixed window of characters. A fixed window is what
		/// makes a source-reading test quietly wrong later: these bodies grew by a guard clause each
		/// time one was hardened, and a window sized to today's shortest reader would start reading
		/// past the end of the longest one — or, worse, find a neighbouring method's bound and call
		/// this one guarded. Strings and comments inside these bodies contain no braces, so a plain
		/// depth count is sufficient and a full C# parse is not.
		/// </remarks>
		private static string MethodBody(string source, int from)
		{
			int open = source.IndexOf('{', from);
			if (open < 0)
			{
				return null;
			}

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					--depth;
					if (depth == 0)
					{
						return source.Substring(open, i - open + 1);
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Every identifier a length or count in this body is tested as being greater than.
		/// </summary>
		/// <remarks>
		/// Matches the comparison rather than a specific spelling of the guard, because the existing
		/// readers do not agree on one: some write <c>if (length &gt; MaxFactions) return empty;</c>
		/// and <c>AbilityAddBroadcast</c>'s element reader folds both directions into
		/// <c>if (count &lt; 0 || count &gt; MAX_EVENTS)</c>. A test that insisted on one shape would
		/// be enforcing a house style it invented rather than the property that matters.
		/// </remarks>
		private static List<string> UpperBoundsIn(string body)
		{
			List<string> bounds = new List<string>();

			foreach (Match match in Regex.Matches(body,
				"\\b(?:length|count|size)\\s*>=?\\s*([A-Za-z_][A-Za-z0-9_]*)\\b"))
			{
				bounds.Add(match.Groups[1].Value);
			}

			return bounds;
		}

		[Test]
		public void EveryBroadcastArrayReaderBoundsTheLengthItAllocatesFrom()
		{
			List<ArrayReader> readers = ArrayReaders();

			LogAssert.IsTrue(readers.Count > 0,
				"no hand-written array readers were found under " + NetworkRoot +
				" — the discovery pattern has drifted from the code, which would make this fixture " +
				"pass by finding nothing");

			List<string> unbounded = new List<string>();

			foreach (ArrayReader reader in readers)
			{
				/* Only readers that actually allocate from the length. A reader that returns a
				 * pooled or fixed buffer has nothing to bound, and demanding a guard from it would
				 * be asking for dead code. */
				if (!Regex.IsMatch(reader.Body, "\\bnew\\s+[A-Za-z_][A-Za-z0-9_.<>]*\\s*\\[\\s*(?:length|count|size)\\s*\\]"))
				{
					continue;
				}

				if (UpperBoundsIn(reader.Body).Count == 0)
				{
					unbounded.Add(reader.File + "." + reader.Method);
				}
			}

			LogAssert.IsTrue(unbounded.Count == 0,
				"these readers allocate an array straight from a length read off the wire with no " +
				"upper bound, so one corrupt int becomes an allocation of up to int.MaxValue " +
				"elements — add a bound in the shape ArenaBroadcastSerializers.ReadArenaMemberEntryArray " +
				"uses, sized from what the game can actually produce: " + string.Join(", ", unbounded));
		}

		[Test]
		public void EveryUpperBoundIsANamedConstantAndNotAMagicNumber()
		{
			/* A literal in the guard bounds the allocation just as well, so this is about the
			 * REASONING rather than the behaviour. Every one of these numbers is a judgement call
			 * about content — how many bank slots exist, how many achievements a patch might add —
			 * and the number chosen is only defensible with that judgement written beside it. A
			 * declared constant has somewhere for the remarks to live and somewhere a future reader
			 * looking at a truncation bug will think to look; a bare 4096 in an if statement has
			 * neither, and is indistinguishable from a number picked because it looked round. */
			List<ArrayReader> readers = ArrayReaders();
			List<string> problems = new List<string>();

			foreach (ArrayReader reader in readers)
			{
				if (!Regex.IsMatch(reader.Body, "\\bnew\\s+[A-Za-z_][A-Za-z0-9_.<>]*\\s*\\[\\s*(?:length|count|size)\\s*\\]"))
				{
					continue;
				}

				string where = reader.File + "." + reader.Method;

				if (Regex.IsMatch(reader.Body, "\\b(?:length|count|size)\\s*>=?\\s*[0-9]"))
				{
					problems.Add(where + " (bounded by a literal)");
					continue;
				}

				foreach (string bound in UpperBoundsIn(reader.Body))
				{
					if (!Regex.IsMatch(reader.Source, "\\bconst\\s+int\\s+" + Regex.Escape(bound) + "\\b"))
					{
						problems.Add(where + " (bound '" + bound + "' is not a const int in this file)");
					}
				}
			}

			LogAssert.IsTrue(problems.Count == 0,
				"an array length bound must be a named const int declared alongside the reader, so " +
				"the reasoning for the number has a home and a truncation bug has somewhere to lead: " +
				string.Join(", ", problems));
		}

		[Test]
		public void AnOverLongLengthReadsBackAsEmptyRatherThanNull()
		{
			/* The writers use length -1 to mean a null array, so null already means "the sender had
			 * nothing here". Returning null when the length is merely too large would collapse a
			 * corrupt stream into that same answer, and a caller that treats null as "no update" —
			 * which is exactly why the sentinel exists — would apply nothing and log nothing. Empty
			 * keeps the two apart: the sender's -1 still round-trips to null, and a rejected length
			 * is a present-but-empty update. */
			List<ArrayReader> readers = ArrayReaders();
			List<string> problems = new List<string>();

			foreach (ArrayReader reader in readers)
			{
				if (UpperBoundsIn(reader.Body).Count == 0)
				{
					// Covered by the guard-presence test above; nothing to say twice.
					continue;
				}

				/* The null sentinel has to still be there. A reader that dropped it while gaining a
				 * bound would round-trip an absent array as an empty one, which is the regression
				 * this pairs with. */
				if (!Regex.IsMatch(reader.Body, "\\b(?:length|count|size)\\s*<\\s*0"))
				{
					problems.Add(reader.File + "." + reader.Method + " (no negative-length null sentinel)");
				}

				if (!reader.Body.Contains("Array.Empty<"))
				{
					problems.Add(reader.File + "." + reader.Method + " (over-long length does not return an empty array)");
				}
			}

			LogAssert.IsTrue(problems.Count == 0,
				"a bounded array reader must keep returning null for the sender's -1 and return an " +
				"empty array for a length it refuses, so 'the sender sent nothing' and 'the stream " +
				"was corrupt' stay distinguishable: " + string.Join(", ", problems));
		}
	}
}
