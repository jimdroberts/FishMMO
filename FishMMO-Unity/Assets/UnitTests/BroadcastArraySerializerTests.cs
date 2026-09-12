using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every broadcast sent as an array can actually be written.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A struct marked <c>[UseGlobalCustomSerializer]</c> tells FishNet its wire format is hand
	/// written. Codegen then declines to synthesise a serializer for an ARRAY of that struct as
	/// well, and says nothing at build time — the build is clean and the weaver logs no warning.
	/// </para>
	/// <para>
	/// The gap surfaces only when a broadcast carrying the array is sent, as "Write method not
	/// found for X[]", by which point the send has already failed. Reported as being unable to buy
	/// from a merchant: the server took payment and granted the item, then could not tell the
	/// client, so the purchase was refunded and the player saw a refusal. The same gap covered the
	/// bank, learning an ability, and achievement updates.
	/// </para>
	/// <para>
	/// Structs WITHOUT the attribute are fine — codegen covers element and array both — so this
	/// checks the pairing rather than every array in the codebase.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BroadcastArraySerializerTests
	{
		private static string NetworkRoot =>
			Path.Combine(Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Network");

		private static string ScriptsRoot =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts");

		/// <summary>Source of every script under Assets/Scripts, line endings normalised.</summary>
		private static string AllScriptSource()
		{
			StringBuilder builder = new StringBuilder();

			foreach (string file in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
			{
				builder.AppendLine(File.ReadAllText(file));
			}

			return builder.ToString();
		}

		/// <summary>Every broadcast type that appears as an array field in the network layer.</summary>
		/// <remarks>
		/// Read off the declaration rather than a type list, so a new array field is covered the
		/// moment it is written.
		/// </remarks>
		private static List<string> ArrayElementTypes()
		{
			SortedSet<string> types = new SortedSet<string>(StringComparer.Ordinal);
			const string marker = "Broadcast[] ";

			foreach (string file in Directory.GetFiles(NetworkRoot, "*.cs", SearchOption.AllDirectories))
			{
				foreach (string line in File.ReadAllLines(file))
				{
					string trimmed = line.Trim();
					if (!trimmed.StartsWith("public ", StringComparison.Ordinal))
					{
						continue;
					}

					int at = trimmed.IndexOf(marker, StringComparison.Ordinal);
					if (at < 0)
					{
						continue;
					}

					string name = trimmed.Substring("public ".Length,
						at + "Broadcast".Length - "public ".Length);
					types.Add(name);
				}
			}

			return new List<string>(types);
		}

		[Test]
		public void EveryCustomSerializedBroadcastSentAsAnArrayHasAnArraySerializer()
		{
			string source = AllScriptSource();
			List<string> elementTypes = ArrayElementTypes();

			LogAssert.IsTrue(elementTypes.Count > 0, "there must be broadcast arrays to check");

			List<string> missing = new List<string>();

			foreach (string type in elementTypes)
			{
				/* Only the hand-written ones. Without the attribute codegen covers the array too, and
				 * demanding a serializer for those would be asking for code nobody needs. */
				if (!DeclaresCustomSerializer(source, type))
				{
					continue;
				}

				if (!source.Contains("this Writer writer, " + type + "[]") ||
					!source.Contains(type + "[] Read" + type + "Array(this Reader reader)"))
				{
					missing.Add(type);
				}
			}

			LogAssert.IsTrue(missing.Count == 0,
				"these broadcasts are sent as arrays and carry [UseGlobalCustomSerializer], so codegen " +
				"will not write the array serializer and the send fails at runtime: " +
				string.Join(", ", missing));
		}

		/// <summary>True when the struct is declared with the global custom serializer attribute.</summary>
		private static bool DeclaresCustomSerializer(string source, string type)
		{
			int at = source.IndexOf("struct " + type, StringComparison.Ordinal);
			while (at >= 0)
			{
				/* The attribute sits immediately above the declaration, with only a doc comment or
				 * blank lines between. A short window keeps another struct's attribute out of it. */
				int from = Math.Max(0, at - 400);
				if (source.Substring(from, at - from).Contains("[UseGlobalCustomSerializer]"))
				{
					return true;
				}

				at = source.IndexOf("struct " + type, at + 1, StringComparison.Ordinal);
			}

			return false;
		}

		[Test]
		public void AnArraySerializerRoundTripsNullAsNull()
		{
			/* Length -1 for null, so an absent array does not come back as an empty one. A caller
			 * that distinguishes "no update" from "update with nothing in it" needs that. */
			string source = AllScriptSource();
			const string signature = "public static void Write";
			int at = source.IndexOf(signature, StringComparison.Ordinal);
			int checked_ = 0;

			while (at >= 0)
			{
				int open = source.IndexOf('{', at);
				int head = open > at ? open - at : 0;

				if (head > 0 && source.Substring(at, head).Contains("Array(this Writer writer"))
				{
					string body = source.Substring(open, Math.Min(600, source.Length - open));
					LogAssert.IsTrue(body.Contains("WriteInt32(-1)"),
						"an array serializer must write -1 for a null array: " + source.Substring(at, head).Trim());
					checked_++;
				}

				at = source.IndexOf(signature, at + 1, StringComparison.Ordinal);
			}

			LogAssert.IsTrue(checked_ > 0, "there must be array serializers to check");
		}
	}
}
