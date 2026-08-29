using System;
using System.Reflection;
using FishMMO.Shared;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the observer buff strip's delta wire format and the receiver-side merge.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The strip used to be resent whole to every observer on every structural change: a six-buff
	/// character cost ~100 B per observer per event, and with a 40-character visibility budget an
	/// AoE fight spent most of a scene's reliable traffic restating buffs nobody's client had
	/// forgotten. It now sends the entries that changed plus the template ids that left.
	/// </para>
	/// <para>
	/// The delta is <b>structural</b>, never difference-encoded: each entry carries its template id
	/// and absolute values, so a message is applicable whatever state the receiver was in. That is
	/// what lets ONE serialized message fan out to an observer set that changes continuously, with
	/// a single per-character baseline instead of one per observer. These tests fail if anyone
	/// reintroduces an index- or difference-encoded form.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObservedBuffDeltaTests
	{
		private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

		// ── Wire format ──────────────────────────────────────────────────────────────

		/// <summary>
		/// One entry must cost exactly seven bytes: an unpacked id, a stack byte, and deciseconds.
		/// </summary>
		[Test]
		public void ObservedBuffEntry_WireForm_IsSevenBytes()
		{
			Writer writer = new Writer();
			new ObservedBuffEntry
			{
				TemplateID = unchecked((int)0xDEADBEEF),
				Stacks = 3,
				RemainingSeconds = 12.4f,
				TotalSeconds = 30f,
			}.WriteTo(writer);

			TestContext.WriteLine($"MEASURE ObservedBuffEntry: {writer.Length} B (was 16 B as four packed fields)");

			LogAssert.AreEqual(7, writer.Length,
				"An observed buff entry must be 4 B of template id + 1 B of stacks + 2 B of deciseconds. " +
				"This entry is multiplied by every visible buff and every observer, so a field widening " +
				"here is paid thousands of times per second in a crowd.");
		}

		/// <summary>
		/// A full-range template id must not use FishNet's packed form.
		/// </summary>
		/// <remarks>
		/// Template ids are a deterministic 32-bit hash (<c>CachedScriptableObject.AddToCache</c>),
		/// so they occupy the whole range and <c>WriteInt32</c>'s signed-packed encoding spends
		/// FIVE bytes on one — worse than not packing at all.
		/// </remarks>
		[Test]
		public void ObservedBuffEntry_TemplateID_IsUnpacked_BecauseIdsAreHashes()
		{
			Writer packed = new Writer();
			packed.WriteInt32(unchecked((int)0xDEADBEEF));

			Writer entry = new Writer();
			new ObservedBuffEntry { TemplateID = unchecked((int)0xDEADBEEF) }.WriteTo(entry);

			LogAssert.IsTrue(packed.Length > 4,
				"Sanity: FishNet's packed int is expected to cost more than four bytes for a full-range " +
				"value. If this ever stops being true the unpacked write below is no longer the better choice.");
			LogAssert.AreEqual(7, entry.Length,
				$"A hash-valued template id costs {packed.Length} B packed. The entry must write it unpacked.");
		}

		/// <summary>Remaining duration must survive a round trip within the wire's resolution.</summary>
		[Test]
		public void ObservedBuffEntry_RemainingSeconds_RoundTripsAtDecisecondResolution()
		{
			float[] cases = { 0f, 0.05f, 1f, 12.34f, 99.9f, 6553.4f };

			for (int i = 0; i < cases.Length; ++i)
			{
				float decoded = ObservedBuffEntry.DecodeSeconds(ObservedBuffEntry.EncodeSeconds(cases[i]));
				LogAssert.IsTrue(Mathf.Abs(decoded - cases[i]) <= 0.05f,
					$"{cases[i]}s decoded as {decoded}s; deciseconds must round trip within half a tenth.");
			}
		}

		/// <summary>A duration past the encodable range must clamp rather than wrap.</summary>
		/// <remarks>
		/// A wrap would turn a two-hour buff into a bar that shows a few seconds left and empties,
		/// which reads as a bug rather than as the clamp it is.
		/// </remarks>
		[Test]
		public void ObservedBuffEntry_RemainingSeconds_ClampsInsteadOfWrapping()
		{
			LogAssert.AreEqual(ushort.MaxValue, ObservedBuffEntry.EncodeSeconds(ObservedBuffEntry.MaxEncodableSeconds + 1000f),
				"A duration past the encodable range must saturate.");
			LogAssert.AreEqual((ushort)0, ObservedBuffEntry.EncodeSeconds(-5f),
				"A negative duration must encode as zero, not wrap to the top of the range.");
		}

		/// <summary>A stack count past a byte must clamp rather than truncate to a small value.</summary>
		[Test]
		public void ObservedBuffEntry_Stacks_ClampInsteadOfTruncating()
		{
			Writer writer = new Writer();
			new ObservedBuffEntry { TemplateID = 1, Stacks = 300 }.WriteTo(writer);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			ObservedBuffEntry decoded = ObservedBuffEntry.ReadFrom(reader);

			LogAssert.AreEqual(ObservedBuffEntry.MaxEncodableStacks, decoded.Stacks,
				"300 stacks must clamp to 255, not truncate to 44. A truncated stack count is a wrong " +
				"number displayed with total confidence.");
			LogAssert.AreEqual(0, reader.Remaining, "The entry must consume its bytes exactly.");
		}

		// ── Broadcast round trip ─────────────────────────────────────────────────────

		/// <summary>A delta broadcast must round trip its entries and its removals.</summary>
		[Test]
		public void CharacterBuffsBroadcast_Delta_RoundTrips()
		{
			CharacterBuffsBroadcast sent = new CharacterBuffsBroadcast
			{
				CharacterObjectID = 77,
				IsFullSet = false,
				Buffs = new[]
				{
					new ObservedBuffEntry { TemplateID = 111, Stacks = 1, RemainingSeconds = 5f },
				},
				Removed = new[] { 222, 333 },
			};

			Writer writer = new Writer();
			writer.WriteCharacterBuffsBroadcast(sent);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterBuffsBroadcast read = reader.ReadCharacterBuffsBroadcast();

			LogAssert.AreEqual(0, reader.Remaining, "The broadcast must consume its bytes exactly.");
			LogAssert.IsFalse(read.IsFullSet, "The delta flag must survive the round trip.");
			LogAssert.AreEqual(77, read.CharacterObjectID, "The character id must survive.");
			LogAssert.AreEqual(1, read.Buffs.Length, "The changed entry must survive.");
			LogAssert.AreEqual(111, read.Buffs[0].TemplateID, "The changed entry's template must survive.");
			LogAssert.AreEqual(2, read.Removed.Length, "Both removals must survive.");
			LogAssert.AreEqual(222, read.Removed[0], "The first removal must survive.");
			LogAssert.AreEqual(333, read.Removed[1], "The second removal must survive.");
		}

		/// <summary>
		/// A full set must not carry a removals block at all.
		/// </summary>
		/// <remarks>
		/// The reader decides whether to expect that block from <c>IsFullSet</c>, which it has
		/// already read. If the writer ever emits a count the reader does not consume — or the
		/// reverse — the stream desynchronises at the NEXT message, not this one.
		/// </remarks>
		[Test]
		public void CharacterBuffsBroadcast_FullSet_OmitsTheRemovalsBlock()
		{
			ObservedBuffEntry[] entries =
			{
				new ObservedBuffEntry { TemplateID = 111, Stacks = 0, RemainingSeconds = 5f },
			};

			Writer full = new Writer();
			full.WriteCharacterBuffsBroadcast(new CharacterBuffsBroadcast
			{
				CharacterObjectID = 1,
				IsFullSet = true,
				Buffs = entries,
				// Deliberately non-empty: a full set states the whole strip, so this must be ignored.
				Removed = new[] { 999 },
			});

			Writer delta = new Writer();
			delta.WriteCharacterBuffsBroadcast(new CharacterBuffsBroadcast
			{
				CharacterObjectID = 1,
				IsFullSet = false,
				Buffs = entries,
				Removed = System.Array.Empty<int>(),
			});

			LogAssert.AreEqual(delta.Length - 2, full.Length,
				"A full set must omit the two-byte removal count that a delta always writes.");

			Reader reader = new Reader(full.GetArraySegment(), null);
			CharacterBuffsBroadcast read = reader.ReadCharacterBuffsBroadcast();

			LogAssert.AreEqual(0, reader.Remaining,
				"A full set must be consumed exactly. A reader that looked for removals here would " +
				"run off the end of the message and corrupt whatever was batched after it.");
			LogAssert.IsTrue(read.IsFullSet, "The full-set flag must survive.");
			LogAssert.AreEqual(0, read.Removed.Length, "A full set must arrive with no removals.");
		}

		/// <summary>An empty delta must round trip without being mistaken for a full set.</summary>
		[Test]
		public void CharacterBuffsBroadcast_NullArrays_RoundTripAsEmptyDelta()
		{
			Writer writer = new Writer();
			writer.WriteCharacterBuffsBroadcast(new CharacterBuffsBroadcast
			{
				CharacterObjectID = 5,
				IsFullSet = false,
				Buffs = null,
				Removed = null,
			});

			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterBuffsBroadcast read = reader.ReadCharacterBuffsBroadcast();

			LogAssert.AreEqual(0, reader.Remaining, "The message must be consumed exactly.");
			LogAssert.IsNotNull(read.Buffs, "A null array must read back as empty, never as null.");
			LogAssert.IsNotNull(read.Removed, "A null array must read back as empty, never as null.");
			LogAssert.IsFalse(read.IsFullSet,
				"An empty DELTA means 'nothing changed'. Reading it as a full set would mean 'the strip " +
				"is empty' and would silently clear every buff the receiver is showing.");
		}

		// ── Structural equality ──────────────────────────────────────────────────────

		/// <summary>
		/// Remaining duration must not count as a structural difference.
		/// </summary>
		/// <remarks>
		/// It moves on every buff on every tick. Counting it would mark the whole strip changed
		/// every tick and collapse the delta straight back into the full resend it replaced.
		/// </remarks>
		[Test]
		public void StructurallyEquals_IgnoresRemainingSeconds_ButNotStacks()
		{
			ObservedBuffEntry a = new ObservedBuffEntry { TemplateID = 1, Stacks = 2, RemainingSeconds = 30f };
			ObservedBuffEntry aged = new ObservedBuffEntry { TemplateID = 1, Stacks = 2, RemainingSeconds = 4f };
			ObservedBuffEntry restacked = new ObservedBuffEntry { TemplateID = 1, Stacks = 3, RemainingSeconds = 30f };

			LogAssert.IsTrue(a.StructurallyEquals(aged),
				"A buff whose duration merely advanced is the SAME buff. Treating it as changed would " +
				"put every visible buff in every delta, every tick.");
			LogAssert.IsFalse(a.StructurallyEquals(restacked),
				"A stack change is structural and must be sent.");
		}

		// ── Receiver merge ───────────────────────────────────────────────────────────

		/// <summary>A delta must add, replace, and remove without disturbing untouched entries.</summary>
		[Test]
		public void MergeObservedBuffs_AddsReplacesAndRemoves()
		{
			GameObject go = new GameObject("BuffDeltaMerge");
			try
			{
				BuffController controller = go.AddComponent<BuffController>();
				SetObservedBuffs(controller,
					new ObservedBuffEntry { TemplateID = 1, Stacks = 0, RemainingSeconds = 10f },
					new ObservedBuffEntry { TemplateID = 2, Stacks = 0, RemainingSeconds = 20f });

				Merge(controller,
					changed: new[]
					{
						new ObservedBuffEntry { TemplateID = 2, Stacks = 4, RemainingSeconds = 25f },
						new ObservedBuffEntry { TemplateID = 3, Stacks = 0, RemainingSeconds = 30f },
					},
					removed: new[] { 1 });

				LogAssert.AreEqual(2, controller.ObservedBuffs.Count,
					"Template 1 removed, 2 restacked, 3 added — two entries should remain.");
				LogAssert.IsFalse(HasTemplate(controller, 1), "A removed template must leave the strip.");

				LogAssert.AreEqual(4, FindTemplate(controller, 2).Stacks,
					"A changed entry must REPLACE the held one, not sit alongside it.");
				LogAssert.AreEqual(1, CountTemplate(controller, 2),
					"A changed entry must appear exactly once. Two copies of one template would draw two " +
					"icons and spawn a second FX instance for the same buff.");
				LogAssert.IsTrue(HasTemplate(controller, 3), "An added template must join the strip.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// An entry the delta did not mention must not have its countdown restart.
		/// </summary>
		/// <remarks>
		/// Consumers draw a bar as <c>RemainingSeconds - (now - ObservedBuffsReceivedTime)</c>, and
		/// applying a merge resets that receipt time. Without ageing the retained entries, an
		/// unrelated buff landing on a character would visibly push every other bar on it back UP.
		/// </remarks>
		[Test]
		public void MergeObservedBuffs_AgesRetainedEntries_SoBarsDoNotJumpBack()
		{
			GameObject go = new GameObject("BuffDeltaAge");
			try
			{
				BuffController controller = go.AddComponent<BuffController>();
				SetObservedBuffs(controller,
					new ObservedBuffEntry { TemplateID = 1, Stacks = 0, RemainingSeconds = 30f });

				// Pretend the strip arrived four seconds ago.
				SetProperty(controller, "ObservedBuffsReceivedTime", Time.unscaledTime - 4f);

				Merge(controller,
					changed: new[] { new ObservedBuffEntry { TemplateID = 2, Stacks = 0, RemainingSeconds = 9f } },
					removed: System.Array.Empty<int>());

				float retained = FindTemplate(controller, 1).RemainingSeconds;
				LogAssert.IsTrue(Mathf.Abs(retained - 26f) < 0.5f,
					$"A retained entry must be aged by the elapsed time (30s - 4s = 26s), was {retained}s. " +
					"Left at 30s its bar would jump back up every time another buff landed.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>Ageing must not turn a finite buff into a permanent one.</summary>
		/// <remarks>
		/// Zero remaining means PERMANENT in this entry, not "one tick left". A finite buff aged
		/// down into zero would stop counting down and display as permanent — the exact bug the
		/// observed list exists to avoid.
		/// </remarks>
		[Test]
		public void MergeObservedBuffs_AgeingNeverProducesAPermanentBuff()
		{
			GameObject go = new GameObject("BuffDeltaAgeFloor");
			try
			{
				BuffController controller = go.AddComponent<BuffController>();
				SetObservedBuffs(controller,
					new ObservedBuffEntry { TemplateID = 1, Stacks = 0, RemainingSeconds = 2f },
					new ObservedBuffEntry { TemplateID = 9, Stacks = 0, RemainingSeconds = 0f });

				SetProperty(controller, "ObservedBuffsReceivedTime", Time.unscaledTime - 60f);

				Merge(controller,
					changed: new[] { new ObservedBuffEntry { TemplateID = 2, Stacks = 0, RemainingSeconds = 9f } },
					removed: System.Array.Empty<int>());

				LogAssert.IsTrue(FindTemplate(controller, 1).RemainingSeconds > 0f,
					"A finite buff aged past its end must floor above zero, because zero is read as permanent.");
				LogAssert.AreEqual(0f, FindTemplate(controller, 9).RemainingSeconds,
					"A permanent buff (0) must stay 0 rather than being aged into a finite value.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static void Merge(BuffController controller, ObservedBuffEntry[] changed, int[] removed)
		{
			MethodInfo merge = typeof(BuffController).GetMethod("MergeObservedBuffs", PrivateInstanceFlags);
			LogAssert.IsNotNull(merge, "BuffController.MergeObservedBuffs is the delta receive path; it must exist.");
			merge.Invoke(controller, new object[] { changed, removed });
		}

		private static void SetObservedBuffs(BuffController controller, params ObservedBuffEntry[] entries)
		{
			FieldInfo field = typeof(BuffController).GetField("observedBuffs", PrivateInstanceFlags);
			LogAssert.IsNotNull(field, "BuffController.observedBuffs backs the observed strip.");
			field.SetValue(controller, entries);
		}

		private static void SetProperty(BuffController controller, string name, float value)
		{
			PropertyInfo property = typeof(BuffController).GetProperty(name);
			LogAssert.IsNotNull(property, $"BuffController.{name} must exist.");
			property.SetValue(controller, value);
		}

		private static bool HasTemplate(BuffController controller, int templateID)
		{
			return CountTemplate(controller, templateID) > 0;
		}

		private static int CountTemplate(BuffController controller, int templateID)
		{
			int count = 0;
			for (int i = 0; i < controller.ObservedBuffs.Count; ++i)
			{
				if (controller.ObservedBuffs[i].TemplateID == templateID)
				{
					++count;
				}
			}
			return count;
		}

		private static ObservedBuffEntry FindTemplate(BuffController controller, int templateID)
		{
			for (int i = 0; i < controller.ObservedBuffs.Count; ++i)
			{
				if (controller.ObservedBuffs[i].TemplateID == templateID)
				{
					return controller.ObservedBuffs[i];
				}
			}
			throw new InvalidOperationException($"Template {templateID} is not in the observed strip.");
		}
	}
}
