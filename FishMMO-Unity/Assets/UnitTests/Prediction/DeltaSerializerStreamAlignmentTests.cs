using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using KinematicCharacterController;
using UnityEngine;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Stream-alignment coverage for the prediction delta serializers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These tests guard one invariant that the per-type round-trip tests cannot see, because it
	/// only shows up when a field is embedded in a larger payload: <b>a delta writer must write
	/// bytes if and only if it returns true</b>. The return value is what
	/// <see cref="CharacterReconcileDataDeltaSerializer"/> uses to decide whether to set that
	/// field's bit in the delta bitmask, and the reader only consumes a field when its bit is
	/// set. A writer that emits a header and then returns false leaves those bytes in the stream
	/// with no bit claiming them, and every subsequent field in the payload reads from the wrong
	/// offset — a silent reconcile corruption rather than an exception.
	/// </para>
	/// <para>
	/// The second invariant covered here is that a writer which declines to write must hand back
	/// the bytes it speculatively reserved, <see cref="Writer.Length"/> included.
	/// <c>Writer.Length</c> only ever grows (every write does <c>Length = Max(Length, Position)</c>)
	/// and <c>GetArraySegment</c> sends <c>0..Length</c>, so rewinding <c>Position</c> alone leaves
	/// the placeholder in the transmitted segment as trailing garbage.
	/// </para>
	/// <para>
	/// Tests use the real production serializers — no in-test reimplementation.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class DeltaSerializerStreamAlignmentTests
	{
		/// <summary>
		/// Runs the serializer registrations that <see cref="RuntimeInitializeOnLoadMethod"/> performs
		/// in a player but not in an EditMode test run.
		/// </summary>
		/// <remarks>
		/// Each serializer class registers itself from a private
		/// <c>[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]</c> hook.
		/// That attribute is a player / play-mode callback, so under EditMode
		/// <c>GenericDeltaWriter&lt;T&gt;.Write</c> is still null and <c>Writer.WriteDelta</c> would
		/// return false without writing anything — which looks exactly like "nothing changed" and
		/// would quietly pass a test that asserts nothing. Invoking the real registration methods
		/// keeps these tests exercising production registration order: the full serializer must be
		/// registered before the delta one, or <c>GenericWriter.SetWrite</c> clears the delta hook.
		/// </remarks>
		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};

			foreach (Type serializerType in serializerTypes)
			{
				MethodInfo register = serializerType.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register,
					$"{serializerType.Name} must expose a RegisterSerializers hook; without it the delta " +
					"serializer is never installed and these tests would assert against a no-op writer.");
				register.Invoke(null, null);
			}
		}

		/// <summary>
		/// Wraps a test body in the fixture's trace logging so each case stays a single block.
		/// </summary>
		private static void Run(string name, string purpose, Action body)
		{
			try
			{
				AuthTestTrace.LogTestStart(name, purpose).GetAwaiter().GetResult();
				body();
				AuthTestTrace.Log(nameof(DeltaSerializerStreamAlignmentTests), "SUCCESS", name).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log(nameof(DeltaSerializerStreamAlignmentTests), "FAILURE",
					$"{name}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(name).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Asserts the write-iff-true contract for one array-delta writer invocation.
		/// </summary>
		private static void AssertWroteIffReturnedTrue(string label, Func<Writer, bool> write)
		{
			Writer writer = new Writer();

			// Put a byte in front so a rewind to zero is distinguishable from a correct rewind.
			writer.WriteUInt8Unpacked(0xAB);
			int positionBefore = writer.Position;
			int lengthBefore = writer.Length;

			bool wrote = write(writer);

			AuthTestTrace.Log(nameof(DeltaSerializerStreamAlignmentTests), "STEP",
				$"{label}: returned={wrote} position {positionBefore}->{writer.Position} length {lengthBefore}->{writer.Length}")
				.GetAwaiter().GetResult();

			if (wrote)
			{
				LogAssert.IsTrue(writer.Position > positionBefore,
					$"{label}: returned true but advanced no bytes — the caller will set a bit the reader cannot satisfy.");
			}
			else
			{
				LogAssert.AreEqual(positionBefore, writer.Position,
					$"{label}: returned false but left Position advanced — those bytes have no bit claiming them, " +
					"so every field after this one reads from the wrong offset.");
				LogAssert.AreEqual(lengthBefore, writer.Length,
					$"{label}: returned false but left Length advanced — GetArraySegment sends 0..Length, " +
					"so the reserved placeholder ships as trailing garbage.");
			}
		}

		[Test]
		public void EquipmentArrayDelta_UnchangedEntries_WritesNothingAndReturnsFalse()
		{
			Run(nameof(EquipmentArrayDelta_UnchangedEntries_WritesNothingAndReturnsFalse),
				"An unchanged equipment array must not emit a header while reporting that it wrote nothing.",
				() =>
				{
					EquipmentReconcileEntry[] prev =
					{
						new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
						new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, InstanceID = 901 },
					};
					// Distinct instance with identical contents — ReferenceEquals must not be what saves us.
					EquipmentReconcileEntry[] next = (EquipmentReconcileEntry[])prev.Clone();

					AssertWroteIffReturnedTrue("Equipment/unchanged",
						w => EquipmentReconcileEntry.WriteArrayDelta(w, prev, next, DeltaSerializerOption.Unset));
				});
		}

		[Test]
		public void EquipmentArrayDelta_BothSidesEmpty_WritesNothingAndReturnsFalse()
		{
			Run(nameof(EquipmentArrayDelta_BothSidesEmpty_WritesNothingAndReturnsFalse),
				"Two empty-but-distinct equipment arrays must not emit a header while reporting that they wrote nothing.",
				() =>
				{
					EquipmentReconcileEntry[] prev = Array.Empty<EquipmentReconcileEntry>();
					EquipmentReconcileEntry[] next = new EquipmentReconcileEntry[0];

					AssertWroteIffReturnedTrue("Equipment/both-empty",
						w => EquipmentReconcileEntry.WriteArrayDelta(w, prev, next, DeltaSerializerOption.Unset));
				});
		}

		[Test]
		public void AllArrayDeltaWriters_UnchangedInput_HonourWriteIffTrueContract()
		{
			Run(nameof(AllArrayDeltaWriters_UnchangedInput_HonourWriteIffTrueContract),
				"Every reconcile array writer must obey write-iff-true, so none of them can desync the payload.",
				() =>
				{
					AttributeReconcileEntry[] attrPrev =
					{
						new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 2 },
					};
					AssertWroteIffReturnedTrue("Attribute/unchanged",
						w => AttributeReconcileEntry.WriteArrayDelta(w, attrPrev,
							(AttributeReconcileEntry[])attrPrev.Clone(), DeltaSerializerOption.Unset));

					BuffReconcileEntry[] buffPrev =
					{
						new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 },
					};
					AssertWroteIffReturnedTrue("Buff/unchanged",
						w => BuffReconcileEntry.WriteArrayDelta(w, buffPrev,
							(BuffReconcileEntry[])buffPrev.Clone(), DeltaSerializerOption.Unset));

					CooldownReconcileEntry[] cdPrev =
					{
						new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 },
					};
					AssertWroteIffReturnedTrue("Cooldown/unchanged",
						w => CooldownReconcileEntry.WriteArrayDelta(w, cdPrev,
							(CooldownReconcileEntry[])cdPrev.Clone(), DeltaSerializerOption.Unset));
				});
		}

		[Test]
		public void ReconcileDelta_UnchangedEquipment_KeepsLaterFieldsAligned()
		{
			Run(nameof(ReconcileDelta_UnchangedEquipment_KeepsLaterFieldsAligned),
				"A reconcile whose equipment is unchanged but whose other fields moved must round-trip exactly. " +
				"Equipment is the last field in the delta bitmask, so a stray header from it corrupts whatever " +
				"the transport packs next.",
				() =>
				{
					CharacterReconcileData prev = MakeReconcileData();
					prev.Equipment = new[]
					{
						new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
					};

					CharacterReconcileData next = prev;
					// Equipment deliberately a distinct array with identical contents.
					next.Equipment = (EquipmentReconcileEntry[])prev.Equipment.Clone();
					next.AbilityID = prev.AbilityID + 3;
					next.RemainingTicks = prev.RemainingTicks + 11;
					next.PackedFlagsAndSlot = prev.PackedFlagsAndSlot ^ 0x5A;
				next.Sequence = unchecked((byte)(prev.Sequence + 1)); // delta chain continuity

					Writer writer = new Writer();
					bool wrote = writer.WriteDelta(prev, next, DeltaSerializerOption.Unset);
					LogAssert.IsTrue(wrote, "Changed reconcile fields must produce a delta.");

					// A sentinel immediately after the delta stands in for whatever the transport
					// packs next. If the delta over-writes or under-reads, this is what catches it.
					const int Sentinel = 0x5EED;
					writer.WriteInt32(Sentinel);

					Reader reader = new Reader(writer.GetArraySegment(), null);
					CharacterReconcileData result = reader.ReadDelta(prev);

					LogAssert.AreEqual(next.AbilityID, result.AbilityID, "AbilityID must survive the delta.");
					LogAssert.AreEqual(next.RemainingTicks, result.RemainingTicks, "RemainingTicks must survive the delta.");
					LogAssert.AreEqual(next.PackedFlagsAndSlot, result.PackedFlagsAndSlot, "PackedFlagsAndSlot must survive the delta.");
					LogAssert.IsNotNull(result.Equipment, "Unchanged equipment must carry forward from prev.");
					LogAssert.AreEqual(1, result.Equipment.Length, "Unchanged equipment must carry forward intact.");

					int trailing = reader.ReadInt32();
					AuthTestTrace.Log(nameof(DeltaSerializerStreamAlignmentTests), "STEP",
						$"sentinel written=0x{Sentinel:X4} read=0x{trailing:X4} remaining={reader.Remaining}")
						.GetAwaiter().GetResult();
					LogAssert.AreEqual(Sentinel, trailing,
						"The reader must land exactly where the writer finished; a mismatch here is the " +
						"stream desync this fixture exists to catch.");
					LogAssert.AreEqual(0, reader.Remaining, "The delta must not leave unread bytes behind.");
				});
		}

		[Test]
		public void MotorStateDelta_BooleanFields_RoundTripByValue()
		{
			Run(nameof(MotorStateDelta_BooleanFields_RoundTripByValue),
				"Motor-state and grounding booleans must round-trip by value and leave the stream aligned. " +
				"FishNet's WriteDeltaBoolean emits a byte while ReadDeltaBoolean consumes none and returns " +
				"the inverse of the previous value, so any use of that pair both desyncs and mis-answers.",
				() =>
				{
					KinematicCharacterMotorState prev = default;
					prev.Position = new Vector3(1f, 2f, 3f);
					prev.Rotation = Quaternion.identity;
					prev.MustUnground = false;
					prev.IsCrouching = true;
					prev.JumpRequested = false;
					prev.LastMovementIterationFoundAnyGround = true;
					prev.GroundingStatus = default;
					prev.GroundingStatus.FoundAnyGround = true;
					prev.GroundingStatus.IsStableOnGround = false;
					prev.GroundingStatus.SnappingPrevented = true;
					prev.GroundingStatus.GroundNormal = Vector3.up;

					KinematicCharacterMotorState next = prev;
					// Flip every boolean, including the nested grounding report.
					next.MustUnground = true;
					next.IsCrouching = false;
					next.JumpRequested = true;
					next.LastMovementIterationFoundAnyGround = false;
					next.GroundingStatus.FoundAnyGround = false;
					next.GroundingStatus.IsStableOnGround = true;
					next.GroundingStatus.SnappingPrevented = false;

					Writer writer = new Writer();
					bool wrote = writer.WriteDelta(prev, next, DeltaSerializerOption.Unset);
					LogAssert.IsTrue(wrote, "Flipped booleans must produce a delta.");

					const int Sentinel = 0x1234;
					writer.WriteInt32(Sentinel);

					Reader reader = new Reader(writer.GetArraySegment(), null);
					KinematicCharacterMotorState result = reader.ReadDelta(prev);

					LogAssert.AreEqual(next.MustUnground, result.MustUnground, "MustUnground must round-trip by value.");
					LogAssert.AreEqual(next.IsCrouching, result.IsCrouching, "IsCrouching must round-trip by value.");
					LogAssert.AreEqual(next.JumpRequested, result.JumpRequested, "JumpRequested must round-trip by value.");
					LogAssert.AreEqual(next.LastMovementIterationFoundAnyGround, result.LastMovementIterationFoundAnyGround,
						"LastMovementIterationFoundAnyGround must round-trip by value.");
					LogAssert.AreEqual(next.GroundingStatus.FoundAnyGround, result.GroundingStatus.FoundAnyGround,
						"Nested FoundAnyGround must round-trip by value.");
					LogAssert.AreEqual(next.GroundingStatus.IsStableOnGround, result.GroundingStatus.IsStableOnGround,
						"Nested IsStableOnGround must round-trip by value.");
					LogAssert.AreEqual(next.GroundingStatus.SnappingPrevented, result.GroundingStatus.SnappingPrevented,
						"Nested SnappingPrevented must round-trip by value.");

					LogAssert.AreEqual(Sentinel, reader.ReadInt32(),
						"Boolean deltas must consume exactly the bytes they wrote.");
					LogAssert.AreEqual(0, reader.Remaining, "The motor-state delta must not leave unread bytes behind.");
				});
		}

		[Test]
		public void MotorStateDelta_UnchangedBooleans_AreNotInverted()
		{
			Run(nameof(MotorStateDelta_UnchangedBooleans_AreNotInverted),
				"A forced full serialize must reproduce boolean values as they are, not inverted. " +
				"ReadDeltaBoolean's `return !valueA` returned the wrong answer whenever the writer emitted " +
				"a value equal to the previous one, which is exactly what a full serialize does.",
				() =>
				{
					KinematicCharacterMotorState prev = default;
					prev.Rotation = Quaternion.identity;
					prev.IsCrouching = true;
					prev.JumpRequested = false;
					prev.GroundingStatus = default;
					prev.GroundingStatus.IsStableOnGround = true;

					// Identical state, forced full serialize.
					KinematicCharacterMotorState next = prev;

					Writer writer = new Writer();
					bool wrote = writer.WriteDelta(prev, next, DeltaSerializerOption.FullSerialize);
					LogAssert.IsTrue(wrote, "A forced serialize must always emit.");

					Reader reader = new Reader(writer.GetArraySegment(), null);
					KinematicCharacterMotorState result = reader.ReadDelta(prev);

					LogAssert.AreEqual(true, result.IsCrouching,
						"IsCrouching was true on both sides and must read back true, not inverted.");
					LogAssert.AreEqual(false, result.JumpRequested,
						"JumpRequested was false on both sides and must read back false, not inverted.");
					LogAssert.AreEqual(true, result.GroundingStatus.IsStableOnGround,
						"Nested IsStableOnGround was true on both sides and must read back true, not inverted.");
					LogAssert.AreEqual(0, reader.Remaining, "A forced serialize must not leave unread bytes behind.");
				});
		}

		[Test]
		public void ReconcileFull_RoundTrip_PreservesEveryFieldAndConsumesExactly()
		{
			Run(nameof(ReconcileFull_RoundTrip_PreservesEveryFieldAndConsumesExactly),
				"The full reconcile serializer is the live path — FishNet gates both delta reconcile and " +
				"delta replicate behind #if DO_NOT_USE — so its write/read ordering must match exactly.",
				() =>
				{
					CharacterReconcileData value = MakeReconcileData();
					value.Cooldowns = new[]
					{
						new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 },
					};
					value.Buffs = new[]
					{
						new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 2, TickCount = 4, CumulativeTickMultiplier = 1 },
					};
					value.Equipment = new[]
					{
						new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
					};
					value.Attributes = new[]
					{
						new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 2 },
					};

					Writer writer = new Writer();
					writer.WriteCharacterReconcileData(value);

					const int Sentinel = 0x0FF1;
					writer.WriteInt32(Sentinel);

					Reader reader = new Reader(writer.GetArraySegment(), null);
					CharacterReconcileData result = reader.ReadCharacterReconcileData();

					LogAssert.AreEqual(value.AbilityID, result.AbilityID, "AbilityID must survive the full round-trip.");
					LogAssert.AreEqual(value.RemainingTicks, result.RemainingTicks, "RemainingTicks must survive the full round-trip.");
					LogAssert.AreEqual(value.Seed, result.Seed, "Seed must survive the full round-trip.");
					LogAssert.AreEqual(value.PackedFlagsAndSlot, result.PackedFlagsAndSlot, "PackedFlagsAndSlot must survive the full round-trip.");
					LogAssert.AreEqual(value.RngS0, result.RngS0, "RngS0 must survive the full round-trip.");
					LogAssert.AreEqual(value.RngS3, result.RngS3, "RngS3 must survive the full round-trip.");
					LogAssert.AreEqual(1, result.Cooldowns.Length, "Cooldowns must survive the full round-trip.");
					LogAssert.AreEqual(1, result.Buffs.Length, "Buffs must survive the full round-trip.");
					LogAssert.AreEqual(1, result.Equipment.Length, "Equipment must survive the full round-trip.");
					LogAssert.AreEqual(1, result.Attributes.Length, "Attributes must survive the full round-trip.");
					LogAssert.IsTrue(value.Equipment[0].Equals(result.Equipment[0]), "The equipment entry must match field for field.");
					LogAssert.IsTrue(value.Attributes[0].Equals(result.Attributes[0]), "The attribute entry must match field for field.");

					LogAssert.AreEqual(Sentinel, reader.ReadInt32(),
						"The full reconcile reader must land exactly where the writer finished.");
					LogAssert.AreEqual(0, reader.Remaining, "The full reconcile must not leave unread bytes behind.");
				});
		}

		[Test]
		public void ReplicateDelta_RoundTrip_ConsumesExactly()
		{
			Run(nameof(ReplicateDelta_RoundTrip_ConsumesExactly),
				"CharacterReplicateData is the input payload; its delta must consume exactly what it wrote.",
				() =>
				{
					CharacterReplicateData prev = default;
					prev.AimDirection = Vector3.forward;

					CharacterReplicateData next = prev;
					/* Quantised, exactly as KCCPlayer.PopulateInput does. Move axes are carried as a
					 * signed byte now, so -0.5 is not on the representable set and asserting on the
					 * raw value would be asserting against a number the wire never agreed to. */
					next.MoveAxisForward = MoveAxisCompression.Quantize(1f);
					next.MoveAxisRight = MoveAxisCompression.Quantize(-0.5f);
					next.MoveFlags = 7;
					next.ActivationFlags = 3;
					next.QueuedAbilityID = 1234;

					Writer writer = new Writer();
					bool wrote = writer.WriteDelta(prev, next, DeltaSerializerOption.Unset);
					LogAssert.IsTrue(wrote, "Changed replicate fields must produce a delta.");

					const int Sentinel = 0x2BAD;
					writer.WriteInt32(Sentinel);

					Reader reader = new Reader(writer.GetArraySegment(), null);
					CharacterReplicateData result = reader.ReadDelta(prev);

					LogAssert.AreEqual(next.MoveAxisForward, result.MoveAxisForward, "MoveAxisForward must survive the delta.");
					LogAssert.AreEqual(next.MoveAxisRight, result.MoveAxisRight, "MoveAxisRight must survive the delta.");
					LogAssert.AreEqual(next.MoveFlags, result.MoveFlags, "MoveFlags must survive the delta.");
					LogAssert.AreEqual(next.ActivationFlags, result.ActivationFlags, "ActivationFlags must survive the delta.");
					LogAssert.AreEqual(next.QueuedAbilityID, result.QueuedAbilityID, "QueuedAbilityID must survive the delta.");

					LogAssert.AreEqual(Sentinel, reader.ReadInt32(),
						"The replicate delta reader must land exactly where the writer finished.");
					LogAssert.AreEqual(0, reader.Remaining, "The replicate delta must not leave unread bytes behind.");
				});
		}

		[Test]
		public void ReconcileDelta_FullSerialize_EmitsEveryFieldAndRoundTrips()
		{
			Run(nameof(ReconcileDelta_FullSerialize_EmitsEveryFieldAndRoundTrips),
				"FullSerialize must still force every field out and round-trip exactly, while RootSerialize " +
				"only forces the flags word. Splitting forceWrite into fullSerialize/mustEmit is what recovers " +
				"the compression, so the FullSerialize contract needs pinning down separately.",
				() =>
				{
					CharacterReconcileData prev = MakeReconcileData();
					prev.Attributes = new[] { new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 2 } };
					prev.Equipment = new[] { new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 } };

					// Identical next: under Unset this writes nothing at all.
					CharacterReconcileData next = prev;
					next.Attributes = (AttributeReconcileEntry[])prev.Attributes.Clone();
					next.Equipment = (EquipmentReconcileEntry[])prev.Equipment.Clone();

					Writer unsetWriter = new Writer();
					bool unsetWrote = unsetWriter.WriteDelta(prev, next, DeltaSerializerOption.Unset);

					Writer rootWriter = new Writer();
					bool rootWrote = rootWriter.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize);

					Writer fullWriter = new Writer();
					bool fullWrote = fullWriter.WriteDelta(prev, next, DeltaSerializerOption.FullSerialize);

					TestContext.WriteLine(
						$"MEASURE unchanged snapshot: Unset={unsetWriter.Length}B(wrote={unsetWrote}) " +
						$"RootSerialize={rootWriter.Length}B(wrote={rootWrote}) FullSerialize={fullWriter.Length}B(wrote={fullWrote})");

					LogAssert.IsFalse(unsetWrote, "An unchanged snapshot under Unset must write nothing.");
					LogAssert.IsTrue(rootWrote, "RootSerialize must emit so the reader stays aligned.");
					LogAssert.IsTrue(fullWrote, "FullSerialize must emit.");
					LogAssert.IsTrue(fullWriter.Length > rootWriter.Length,
						$"FullSerialize must carry every field ({fullWriter.Length}B) where RootSerialize carries " +
						$"only the flags word for an unchanged snapshot ({rootWriter.Length}B). If these match, " +
						"RootSerialize is still being treated as a full serialize and the compression is lost.");

					/* Round-trip from the SAME baseline the writer used. FishNet's scalar delta
					 * primitives write valueB - valueA (Writer.WriteDifference8_16_32), so the wire
					 * format is relative to prev even under FullSerialize — the option controls
					 * whether a field is emitted, not whether the payload is self-contained. Both
					 * peers must therefore hold the same previous snapshot; that is a property of
					 * FishNet's design, not something these serializers can fix. */
					Reader reader = new Reader(fullWriter.GetArraySegment(), null);
					CharacterReconcileData result = reader.ReadDelta(prev);

					LogAssert.AreEqual(next.AbilityID, result.AbilityID, "FullSerialize must round-trip AbilityID.");
					LogAssert.AreEqual(next.Seed, result.Seed, "FullSerialize must round-trip Seed.");
					LogAssert.AreEqual(next.PackedFlagsAndSlot, result.PackedFlagsAndSlot, "FullSerialize must round-trip PackedFlagsAndSlot.");
					LogAssert.AreEqual(next.RngS0, result.RngS0, "FullSerialize must round-trip the RNG state.");
					LogAssert.AreEqual(next.ResourceState.MaxHealth, result.ResourceState.MaxHealth, "FullSerialize must round-trip resource state.");
					LogAssert.AreEqual(1, result.Attributes.Length, "FullSerialize must round-trip the attribute array.");
					LogAssert.IsTrue(next.Attributes[0].Equals(result.Attributes[0]), "The attribute entry must match field for field.");
					LogAssert.AreEqual(1, result.Equipment.Length, "FullSerialize must round-trip the equipment array.");
					LogAssert.IsTrue(next.Equipment[0].Equals(result.Equipment[0]), "The equipment entry must match field for field.");
					LogAssert.AreEqual(0, reader.Remaining, "FullSerialize must not leave unread bytes behind.");
				});
		}

		[Test]
		public void ReconcileDelta_TypicalWalkingTick_MeasuresAgainstFullSerialize()
		{
			Run(nameof(ReconcileDelta_TypicalWalkingTick_MeasuresAgainstFullSerialize),
				"Quantifies what the delta serializers are worth on a typical tick, and what the option " +
				"FishNet actually passes does to that number.",
				() =>
				{
					// A walking player: motor state moves, everything else is steady.
					CharacterReconcileData prev = MakeReconcileData();
					prev.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };
					prev.Buffs = new[] { new BuffReconcileEntry { TemplateID = 3, ExpiryTick = 500, NextTickTick = 20, Stacks = 1, TickCount = 4, CumulativeTickMultiplier = 1 } };
					prev.Equipment = new[]
					{
						new EquipmentReconcileEntry { TemplateID = 5, Slot = 1, Seed = 77, InstanceID = 900 },
						new EquipmentReconcileEntry { TemplateID = 6, Slot = 2, Seed = 78, InstanceID = 901 },
					};
					prev.Attributes = new[]
					{
						new AttributeReconcileEntry { TemplateID = 1, Value = 10, ExternalModifier = 2 },
						new AttributeReconcileEntry { TemplateID = 2, Value = 30, ExternalModifier = 0 },
						new AttributeReconcileEntry { TemplateID = 3, Value = 45, ExternalModifier = 5 },
					};

					CharacterReconcileData next = prev;
					next.MotorState.Position = prev.MotorState.Position + new Vector3(0.12f, 0f, 0.04f);
					next.MotorState.BaseVelocity = new Vector3(3.5f, 0f, 1.2f);
					next.ResourceState.Health = prev.ResourceState.Health - 1f;
					// Distinct array instances with identical contents — the steady-state shape.
					next.Cooldowns = (CooldownReconcileEntry[])prev.Cooldowns.Clone();
					next.Buffs = (BuffReconcileEntry[])prev.Buffs.Clone();
					next.Equipment = (EquipmentReconcileEntry[])prev.Equipment.Clone();
					next.Attributes = (AttributeReconcileEntry[])prev.Attributes.Clone();

					Writer fullWriter = new Writer();
					fullWriter.WriteCharacterReconcileData(next);
					int fullBytes = fullWriter.Length;

					Writer deltaWriter = new Writer();
					deltaWriter.WriteDelta(prev, next, DeltaSerializerOption.Unset);
					int deltaBytes = deltaWriter.Length;

					Writer rootWriter = new Writer();
					rootWriter.WriteDelta(prev, next, DeltaSerializerOption.RootSerialize);
					int rootBytes = rootWriter.Length;

					// TestContext so the measurement lands in the NUnit results XML, where it can be
					// read back without re-running the suite.
					TestContext.WriteLine(
						$"MEASURE typical walking tick: full={fullBytes}B delta(Unset)={deltaBytes}B delta(RootSerialize)={rootBytes}B");

					LogAssert.IsTrue(deltaBytes < fullBytes,
						$"The delta serializers must beat the full serializer on a steady-state tick " +
						$"(delta={deltaBytes}B vs full={fullBytes}B), or they are not worth enabling.");

					/* RootSerialize is what FishNet passes for every reconcile that is not a periodic
					 * full resend, and for every replicate entry after the first. The composite
					 * writers treat any non-Unset option as "write every field", so this number is
					 * the one that would actually go on the wire — see the note on the class. */
					TestContext.WriteLine($"MEASURE RootSerialize vs full: {rootBytes - fullBytes:+#;-#;0}B");
				});
		}

		/// <summary>
		/// A reconcile snapshot with every scalar field set to something distinguishable.
		/// </summary>
		private static CharacterReconcileData MakeReconcileData()
		{
			CharacterReconcileData data = default;
			data.MotorState = default;
			data.MotorState.Rotation = Quaternion.identity;
			data.MotorState.Position = new Vector3(10f, 20f, 30f);
			data.AbilityID = 77;
			data.RemainingTicks = 12;
			data.Seed = 4242;
			data.PackedFlagsAndSlot = 0x1234;
			data.ResourceState = default;
			data.ResourceState.MaxHealth = 100;
			data.ResourceState.Health = 55f;
			data.RngS0 = 0xDEADBEEF;
			data.RngS1 = 0x12345678;
			data.RngS2 = 0x0BADF00D;
			data.RngS3 = 0xFEEDFACE;
			return data;
		}
	}
}
