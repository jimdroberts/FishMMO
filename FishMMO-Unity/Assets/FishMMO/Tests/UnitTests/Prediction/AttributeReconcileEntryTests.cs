using System;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Round-trip and diff coverage for <see cref="AttributeReconcileEntry"/>, the entry type
	/// that carries non-resource character attributes (Value + ExternalModifier) through the
	/// unified <see cref="CharacterReconcileData.Attributes"/> array.
	///
	/// The serializer mirrors <see cref="BuffReconcileEntry"/> / <see cref="CooldownReconcileEntry"/>
	/// index-delta compression. Any per-field omission in <see cref="AttributeReconcileEntry.Equals"/>
	/// would let a changed attribute skip the wire, silently desyncing strength / agility /
	/// resistance values across the reconcile pipeline.
	///
	/// Tests use the real production serializer — no in-test reimplementation.
	/// </summary>
	[TestFixture]
	public class AttributeReconcileEntryTests
	{
		[Test]
		public void Equals_DetectsEveryFieldDivergence()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Equals_DetectsEveryFieldDivergence),
					"AttributeReconcileEntry.Equals must detect divergence in every field (TemplateID, Value, ExternalModifier).")
					.GetAwaiter().GetResult();

				var baseline = new AttributeReconcileEntry { TemplateID = 7, Value = 100, ExternalModifier = 25 };
				AuthTestTrace.Log("AttributeReconcileEntryTests", "STEP",
					"baseline { TemplateID=7, Value=100, ExternalModifier=25 } — probing each field divergence.")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(baseline.Equals(baseline), "An entry must equal itself.");

				var diffTemplate = baseline; diffTemplate.TemplateID = 8;
				LogAssert.IsFalse(baseline.Equals(diffTemplate),
					"TemplateID divergence must be detected — otherwise a snapshot-position swap is silent.");

				var diffValue = baseline; diffValue.Value = 101;
				LogAssert.IsFalse(baseline.Equals(diffValue),
					"Value divergence must be detected — primary attribute desync field.");

				var diffMod = baseline; diffMod.ExternalModifier = 26;
				LogAssert.IsFalse(baseline.Equals(diffMod),
					"ExternalModifier divergence must be detected — buff/equip/region desync field.");

				AuthTestTrace.Log("AttributeReconcileEntryTests", "SUCCESS", nameof(Equals_DetectsEveryFieldDivergence)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AttributeReconcileEntryTests", "FAILURE", $"{nameof(Equals_DetectsEveryFieldDivergence)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(Equals_DetectsEveryFieldDivergence)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void EqualsHashCodeContract_Holds()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(EqualsHashCodeContract_Holds),
					"Two entries with identical fields must be Equal and must produce identical hash codes.")
					.GetAwaiter().GetResult();

				var a = new AttributeReconcileEntry { TemplateID = 11, Value = 50, ExternalModifier = 7 };
				var b = new AttributeReconcileEntry { TemplateID = 11, Value = 50, ExternalModifier = 7 };
				AuthTestTrace.Log("AttributeReconcileEntryTests", "STEP",
					$"a.GetHashCode()={a.GetHashCode()} b.GetHashCode()={b.GetHashCode()} a.Equals(b)={a.Equals(b)}")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(a.Equals(b), "Identical-field entries must compare equal.");
				LogAssert.AreEqual(a.GetHashCode(), b.GetHashCode(),
					"Equal entries must produce equal hash codes (Equals/GetHashCode contract).");

				AuthTestTrace.Log("AttributeReconcileEntryTests", "SUCCESS", nameof(EqualsHashCodeContract_Holds)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AttributeReconcileEntryTests", "FAILURE", $"{nameof(EqualsHashCodeContract_Holds)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(EqualsHashCodeContract_Holds)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void WriteRead_FullArray_RoundTrip_PreservesAllEntries()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(WriteRead_FullArray_RoundTrip_PreservesAllEntries),
					"The full-array write path (prev=null) must emit and round-trip every entry unchanged.")
					.GetAwaiter().GetResult();

				AttributeReconcileEntry[] next = new[]
				{
					new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 0 },
					new AttributeReconcileEntry { TemplateID = 3, Value = 25, ExternalModifier = 5 },
					new AttributeReconcileEntry { TemplateID = 9, Value = 99, ExternalModifier = -3 },
				};

				var writer = new Writer();
				bool wrote = AttributeReconcileEntry.WriteArrayDelta(writer, null, next, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("AttributeReconcileEntryTests", "STEP",
					$"WriteArrayDelta(prev=null, next.Length={next.Length}) wrote={wrote} bytes={writer.Position}")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(wrote, "Full-array path must emit when prev is null and next is non-empty.");

				var reader = new Reader(writer.GetArraySegment(), null);
				AttributeReconcileEntry[] result = AttributeReconcileEntry.ReadArrayDelta(reader, null);

				LogAssert.IsNotNull(result, "Full-array round-trip must return a non-null array.");
				LogAssert.AreEqual(next.Length, result.Length, "Full-array round-trip must preserve length.");
				for (int i = 0; i < next.Length; i++)
				{
					LogAssert.IsTrue(next[i].Equals(result[i]), $"Full-array round-trip: entry {i} must match.");
				}

				AuthTestTrace.Log("AttributeReconcileEntryTests", "SUCCESS", nameof(WriteRead_FullArray_RoundTrip_PreservesAllEntries)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AttributeReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_FullArray_RoundTrip_PreservesAllEntries)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_FullArray_RoundTrip_PreservesAllEntries)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void WriteRead_IndexDelta_OnlyChangedIndicesEmitted()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted),
					"When a single same-length entry changes, the serializer must use index-delta mode and emit only the changed index.")
					.GetAwaiter().GetResult();

				AttributeReconcileEntry[] prev = new[]
				{
					new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 0 },
					new AttributeReconcileEntry { TemplateID = 3, Value = 25, ExternalModifier = 5 },
					new AttributeReconcileEntry { TemplateID = 9, Value = 99, ExternalModifier = -3 },
				};

				// Only index 1 (template 3) changes — ExternalModifier shifted by buff application.
				AttributeReconcileEntry[] next = new[]
				{
					new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 0 },
					new AttributeReconcileEntry { TemplateID = 3, Value = 25, ExternalModifier = 12 },
					new AttributeReconcileEntry { TemplateID = 9, Value = 99, ExternalModifier = -3 },
				};

				var writer = new Writer();
				bool wrote = AttributeReconcileEntry.WriteArrayDelta(writer, prev, next, DeltaSerializerOption.Unset);
				LogAssert.IsTrue(wrote, "Index-delta path must emit when a single entry changes.");

				// Header is 16-bit packed; high bit set indicates index-delta mode.
				var seg = writer.GetArraySegment();
				ushort header = new Reader(seg, null).ReadUInt16();
				AuthTestTrace.Log("AttributeReconcileEntryTests", "STEP",
					$"header=0x{header:X4} indexDeltaMode={(header & 0x8000) != 0} changedCount={header & 0x7FFF}")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue((header & 0x8000) != 0,
					"Same-length arrays with changes must use index-delta mode (high header bit set).");
				LogAssert.AreEqual(1, header & 0x7FFF,
					"Exactly one changed index must be reported.");

				var reader = new Reader(seg, null);
				AttributeReconcileEntry[] result = AttributeReconcileEntry.ReadArrayDelta(reader, prev);

				LogAssert.IsNotNull(result, "Index-delta round-trip must return a non-null array.");
				LogAssert.AreEqual(next.Length, result.Length, "Length must be preserved.");
				for (int i = 0; i < next.Length; i++)
				{
					LogAssert.IsTrue(next[i].Equals(result[i]), $"Index-delta round-trip: entry {i} must match.");
				}

				AuthTestTrace.Log("AttributeReconcileEntryTests", "SUCCESS", nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AttributeReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void WriteRead_ReferenceEquals_FastPathEmitsZeroBytes()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes),
					"When prev and next are the same reference, the fast path must write nothing and return false.")
					.GetAwaiter().GetResult();

				AttributeReconcileEntry[] same = new[]
				{
					new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 0 },
					new AttributeReconcileEntry { TemplateID = 3, Value = 25, ExternalModifier = 5 },
				};

				var writer = new Writer();
				int startPos = writer.Position;
				bool wrote = AttributeReconcileEntry.WriteArrayDelta(writer, same, same, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("AttributeReconcileEntryTests", "STEP",
					$"ReferenceEquals fast path: wrote={wrote} startPos={startPos} endPos={writer.Position}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(wrote, "ReferenceEquals fast-path must return false (nothing written).");
				LogAssert.AreEqual(startPos, writer.Position,
					"ReferenceEquals fast-path must not advance the writer position.");

				AuthTestTrace.Log("AttributeReconcileEntryTests", "SUCCESS", nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AttributeReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void WriteRead_NullPrevAndNullNext_NoOp()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(WriteRead_NullPrevAndNullNext_NoOp),
					"When both prev and next are null, WriteArrayDelta must be a no-op and not advance the writer.")
					.GetAwaiter().GetResult();

				var writer = new Writer();
				int startPos = writer.Position;
				bool wrote = AttributeReconcileEntry.WriteArrayDelta(writer, null, null, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("AttributeReconcileEntryTests", "STEP",
					$"WriteArrayDelta(null,null): wrote={wrote} startPos={startPos} endPos={writer.Position}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(wrote, "Both arrays null must be a no-op.");
				LogAssert.AreEqual(startPos, writer.Position,
					"Both arrays null must not advance the writer position.");

				AuthTestTrace.Log("AttributeReconcileEntryTests", "SUCCESS", nameof(WriteRead_NullPrevAndNullNext_NoOp)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("AttributeReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_NullPrevAndNullNext_NoOp)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_NullPrevAndNullNext_NoOp)).GetAwaiter().GetResult();
			}
		}
	}
}