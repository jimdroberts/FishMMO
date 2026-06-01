using System;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Round-trip and diff coverage for <see cref="CooldownReconcileEntry"/>, the entry type
	/// that carries active ability cooldowns (AbilityID + StartTick + DurationTicks) through the
	/// <see cref="CharacterReconcileData.Cooldowns"/> array.
	///
	/// The serializer mirrors <see cref="BuffReconcileEntry"/> / <see cref="AttributeReconcileEntry"/>
	/// index-delta compression. Any per-field omission in <see cref="CooldownReconcileEntry.Equals"/>
	/// would let a changed cooldown skip the wire, silently desyncing ability availability across
	/// the reconcile pipeline — a client could fire an ability the server still considers on cooldown.
	///
	/// Tests use the real production serializer — no in-test reimplementation.
	/// </summary>
	[TestFixture]
	public class CooldownReconcileEntryTests
	{
		[Test]
		public void Equals_DetectsEveryFieldDivergence()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(Equals_DetectsEveryFieldDivergence),
					"CooldownReconcileEntry.Equals must detect divergence in AbilityID, StartTick and DurationTicks.")
					.GetAwaiter().GetResult();

				var baseline = new CooldownReconcileEntry { AbilityID = 7L, StartTick = 1_000u, DurationTicks = 30u };
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					"baseline { AbilityID=7, StartTick=1000, DurationTicks=30 } — probing each field divergence.")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(baseline.Equals(baseline), "An entry must equal itself.");

				var diffAbility = baseline; diffAbility.AbilityID = 8L;
				LogAssert.IsFalse(baseline.Equals(diffAbility),
					"AbilityID divergence must be detected — otherwise a snapshot-position swap is silent.");

				var diffStart = baseline; diffStart.StartTick = baseline.StartTick + 1u;
				LogAssert.IsFalse(baseline.Equals(diffStart),
					"StartTick divergence must be detected — primary cooldown-window desync field.");

				var diffDuration = baseline; diffDuration.DurationTicks = baseline.DurationTicks + 1u;
				LogAssert.IsFalse(baseline.Equals(diffDuration),
					"DurationTicks divergence must be detected — cooldown-length desync field.");

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(Equals_DetectsEveryFieldDivergence)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(Equals_DetectsEveryFieldDivergence)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
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
					"Two cooldown entries with identical fields must be Equal and produce identical hash codes.")
					.GetAwaiter().GetResult();

				var a = new CooldownReconcileEntry { AbilityID = 11L, StartTick = 5_000u, DurationTicks = 90u };
				var b = new CooldownReconcileEntry { AbilityID = 11L, StartTick = 5_000u, DurationTicks = 90u };
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"a.GetHashCode()={a.GetHashCode()} b.GetHashCode()={b.GetHashCode()} a.Equals(b)={a.Equals(b)}")
					.GetAwaiter().GetResult();

				LogAssert.IsTrue(a.Equals(b), "Identical-field entries must compare equal.");
				LogAssert.AreEqual(a.GetHashCode(), b.GetHashCode(),
					"Equal entries must produce equal hash codes (Equals/GetHashCode contract).");

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(EqualsHashCodeContract_Holds)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(EqualsHashCodeContract_Holds)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
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
					"The full-array write path (prev=null) must emit and round-trip every cooldown entry unchanged.")
					.GetAwaiter().GetResult();

				CooldownReconcileEntry[] next = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
					new CooldownReconcileEntry { AbilityID = 9L, StartTick = 999u, DurationTicks = 15u },
				};

				var writer = new Writer();
				bool wrote = CooldownReconcileEntry.WriteArrayDelta(writer, null, next, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"WriteArrayDelta(prev=null, next.Length={next.Length}) wrote={wrote} bytes={writer.Position}")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(wrote, "Full-array path must emit when prev is null and next is non-empty.");

				var reader = new Reader(writer.GetArraySegment(), null);
				CooldownReconcileEntry[] result = CooldownReconcileEntry.ReadArrayDelta(reader, null);

				LogAssert.IsNotNull(result, "Full-array round-trip must return a non-null array.");
				LogAssert.AreEqual(next.Length, result.Length, "Full-array round-trip must preserve length.");
				for (int i = 0; i < next.Length; i++)
				{
					LogAssert.IsTrue(next[i].Equals(result[i]), $"Full-array round-trip: entry {i} must match.");
				}

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(WriteRead_FullArray_RoundTrip_PreservesAllEntries)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_FullArray_RoundTrip_PreservesAllEntries)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
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
					"When a single same-length cooldown changes, the serializer must use index-delta mode and emit only that index.")
					.GetAwaiter().GetResult();

				CooldownReconcileEntry[] prev = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
					new CooldownReconcileEntry { AbilityID = 9L, StartTick = 999u, DurationTicks = 15u },
				};

				// Only index 1 (ability 3) changes — cooldown re-triggered at a later StartTick.
				CooldownReconcileEntry[] next = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 400u, DurationTicks = 60u },
					new CooldownReconcileEntry { AbilityID = 9L, StartTick = 999u, DurationTicks = 15u },
				};

				var writer = new Writer();
				bool wrote = CooldownReconcileEntry.WriteArrayDelta(writer, prev, next, DeltaSerializerOption.Unset);
				LogAssert.IsTrue(wrote, "Index-delta path must emit when a single entry changes.");

				// Header is 16-bit packed; high bit set indicates index-delta mode.
				var seg = writer.GetArraySegment();
				ushort header = new Reader(seg, null).ReadUInt16();
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"header=0x{header:X4} indexDeltaMode={(header & 0x8000) != 0} changedCount={header & 0x7FFF}")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue((header & 0x8000) != 0,
					"Same-length arrays with changes must use index-delta mode (high header bit set).");
				LogAssert.AreEqual(1, header & 0x7FFF,
					"Exactly one changed index must be reported.");

				var reader = new Reader(seg, null);
				CooldownReconcileEntry[] result = CooldownReconcileEntry.ReadArrayDelta(reader, prev);

				LogAssert.IsNotNull(result, "Index-delta round-trip must return a non-null array.");
				LogAssert.AreEqual(next.Length, result.Length, "Length must be preserved.");
				for (int i = 0; i < next.Length; i++)
				{
					LogAssert.IsTrue(next[i].Equals(result[i]), $"Index-delta round-trip: entry {i} must match.");
				}

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_IndexDelta_OnlyChangedIndicesEmitted)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void WriteRead_LengthChange_UsesFullArrayPath()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(WriteRead_LengthChange_UsesFullArrayPath),
					"When the array length changes (a cooldown expired), the serializer must fall back to the full-array path.")
					.GetAwaiter().GetResult();

				// A cooldown expiring removes an entry, changing the array length. The serializer
				// must abandon index-delta (which requires equal lengths) and send a full array.
				CooldownReconcileEntry[] prev = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
				};

				CooldownReconcileEntry[] next = new[]
				{
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
				};

				var writer = new Writer();
				bool wrote = CooldownReconcileEntry.WriteArrayDelta(writer, prev, next, DeltaSerializerOption.Unset);
				LogAssert.IsTrue(wrote, "A length change must emit a full array.");

				var seg = writer.GetArraySegment();
				ushort header = new Reader(seg, null).ReadUInt16();
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"prev.Length={prev.Length} next.Length={next.Length} header=0x{header:X4} indexDeltaMode={(header & 0x8000) != 0}")
					.GetAwaiter().GetResult();
				LogAssert.IsFalse((header & 0x8000) != 0,
					"A length change must use full-array mode (high header bit clear).");

				var reader = new Reader(seg, null);
				CooldownReconcileEntry[] result = CooldownReconcileEntry.ReadArrayDelta(reader, prev);

				LogAssert.IsNotNull(result, "Full-array round-trip must return a non-null array.");
				LogAssert.AreEqual(next.Length, result.Length, "Result length must match the new array.");
				LogAssert.IsTrue(next[0].Equals(result[0]), "Surviving cooldown must be preserved exactly.");

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(WriteRead_LengthChange_UsesFullArrayPath)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_LengthChange_UsesFullArrayPath)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_LengthChange_UsesFullArrayPath)).GetAwaiter().GetResult();
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

				CooldownReconcileEntry[] same = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
				};

				var writer = new Writer();
				int startPos = writer.Position;
				bool wrote = CooldownReconcileEntry.WriteArrayDelta(writer, same, same, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"ReferenceEquals fast path: wrote={wrote} startPos={startPos} endPos={writer.Position}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(wrote, "ReferenceEquals fast-path must return false (nothing written).");
				LogAssert.AreEqual(startPos, writer.Position,
					"ReferenceEquals fast-path must not advance the writer position.");

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_ReferenceEquals_FastPathEmitsZeroBytes)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void WriteRead_IdenticalContentDifferentArrays_NoChangeEmitsZeroBytes()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(WriteRead_IdenticalContentDifferentArrays_NoChangeEmitsZeroBytes),
					"Distinct arrays with identical content must diff to no change and rewind the writer.")
					.GetAwaiter().GetResult();

				// Distinct array instances with identical content must diff to "no change" and
				// rewind the writer — the index-delta path must not emit a header for zero changes.
				CooldownReconcileEntry[] prev = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
				};
				CooldownReconcileEntry[] next = new[]
				{
					new CooldownReconcileEntry { AbilityID = 1L, StartTick = 100u, DurationTicks = 30u },
					new CooldownReconcileEntry { AbilityID = 3L, StartTick = 250u, DurationTicks = 60u },
				};

				var writer = new Writer();
				int startPos = writer.Position;
				bool wrote = CooldownReconcileEntry.WriteArrayDelta(writer, prev, next, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"identical-content distinct arrays: wrote={wrote} startPos={startPos} endPos={writer.Position}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(wrote, "Identical-content arrays must diff to no change.");
				LogAssert.AreEqual(startPos, writer.Position,
					"A zero-change index-delta must rewind the writer to its starting position.");

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(WriteRead_IdenticalContentDifferentArrays_NoChangeEmitsZeroBytes)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_IdenticalContentDifferentArrays_NoChangeEmitsZeroBytes)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_IdenticalContentDifferentArrays_NoChangeEmitsZeroBytes)).GetAwaiter().GetResult();
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
				bool wrote = CooldownReconcileEntry.WriteArrayDelta(writer, null, null, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("CooldownReconcileEntryTests", "STEP",
					$"WriteArrayDelta(null,null): wrote={wrote} startPos={startPos} endPos={writer.Position}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(wrote, "Both arrays null must be a no-op.");
				LogAssert.AreEqual(startPos, writer.Position,
					"Both arrays null must not advance the writer position.");

				AuthTestTrace.Log("CooldownReconcileEntryTests", "SUCCESS", nameof(WriteRead_NullPrevAndNullNext_NoOp)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CooldownReconcileEntryTests", "FAILURE", $"{nameof(WriteRead_NullPrevAndNullNext_NoOp)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(WriteRead_NullPrevAndNullNext_NoOp)).GetAwaiter().GetResult();
			}
		}
	}
}
