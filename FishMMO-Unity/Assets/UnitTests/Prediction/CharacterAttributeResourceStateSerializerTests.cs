using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Delta serializer round-trip coverage for <see cref="CharacterAttributeResourceState"/>.
	/// The struct rides every <see cref="CharacterReconcileData"/> snapshot, so any
	/// mismatch between the write and read paths silently corrupts client HP / MP / stamina
	/// every tick.
	///
	/// Tests use the real production
	/// <see cref="CharacterAttributeResourceStateSerializer"/> via the generic delta
	/// registration — no in-test reimplementation.
	/// </summary>
	[TestFixture]
	public class CharacterAttributeResourceStateSerializerTests
	{
		/// <summary>
		/// <see cref="CharacterAttributeResourceStateSerializer.RegisterDeltaSerializers"/> is
		/// decorated with <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c>, which only
		/// fires in PlayMode. EditMode tests never load a scene, so the delta delegates would
		/// be null and the Write/Read calls below would NullReferenceException.
		/// Invoke the private registration method via reflection once per fixture.
		/// </summary>
		[OneTimeSetUp]
		public void EnsureDeltaSerializersRegistered()
		{
			System.Type t = typeof(CharacterAttributeResourceStateSerializer);
			System.Reflection.MethodInfo register = t.GetMethod(
				"RegisterDeltaSerializers",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			LogAssert.IsNotNull(register,
				"CharacterAttributeResourceStateSerializer.RegisterDeltaSerializers must exist for EditMode test bootstrap.");
			register.Invoke(null, null);
		}

		[Test]
		public void RegularSerializer_RoundTrip_PreservesAllFields()
		{
			var src = new CharacterAttributeResourceState
			{
				RegenTickAccum = 17u,
				Health = 123.5f,
				MaxHealth = 200,
				Mana = 80.25f,
				MaxMana = 150,
				Stamina = 95.0f,
				MaxStamina = 110,
			};

			var writer = new Writer();
			writer.WriteCharacterAttributeResourceState(src);
			var reader = new Reader(writer.GetArraySegment(), null);
			CharacterAttributeResourceState dst = reader.ReadCharacterAttributeResourceState();

			LogAssert.AreEqual(src.RegenTickAccum, dst.RegenTickAccum, "RegenTickAccum must round-trip.");
			LogAssert.AreEqual(src.Health,         dst.Health,         "Health must round-trip.");
			LogAssert.AreEqual(src.MaxHealth,      dst.MaxHealth,      "MaxHealth must round-trip.");
			LogAssert.AreEqual(src.Mana,           dst.Mana,           "Mana must round-trip.");
			LogAssert.AreEqual(src.MaxMana,        dst.MaxMana,        "MaxMana must round-trip.");
			LogAssert.AreEqual(src.Stamina,        dst.Stamina,        "Stamina must round-trip.");
			LogAssert.AreEqual(src.MaxStamina,     dst.MaxStamina,     "MaxStamina must round-trip.");
		}

		[Test]
		public void DeltaSerializer_NoChanges_EmitsZeroBytes_AndReadReturnsPrev()
		{
			var prev = new CharacterAttributeResourceState
			{
				RegenTickAccum = 5u, Health = 100f, MaxHealth = 100, Mana = 50f, MaxMana = 50, Stamina = 25f, MaxStamina = 25
			};

			var writer = new Writer();
			int startPos = writer.Position;
			bool wrote = GenericDeltaWriter<CharacterAttributeResourceState>.Write(writer, prev, prev, DeltaSerializerOption.Unset);

			LogAssert.IsFalse(wrote, "Identical prev/next must produce no wire output.");
			LogAssert.AreEqual(startPos, writer.Position,
				"Writer position must not advance when there is nothing to send.");
		}

		[Test]
		public void DeltaSerializer_HealthRegen_RoundTrip()
		{
			var prev = new CharacterAttributeResourceState
			{
				RegenTickAccum = 0u, Health = 80f, MaxHealth = 100, Mana = 50f, MaxMana = 50, Stamina = 25f, MaxStamina = 25
			};
			var next = prev;
			next.RegenTickAccum = 1u;
			next.Health = 85f;

			var writer = new Writer();
			bool wrote = GenericDeltaWriter<CharacterAttributeResourceState>.Write(writer, prev, next, DeltaSerializerOption.Unset);
			LogAssert.IsTrue(wrote, "A non-trivial delta (regen accum + health) must emit output.");

			var reader = new Reader(writer.GetArraySegment(), null);
			CharacterAttributeResourceState restored = GenericDeltaReader<CharacterAttributeResourceState>.Read(reader, prev);

			LogAssert.AreEqual(next.RegenTickAccum, restored.RegenTickAccum, "RegenTickAccum delta must reconstruct exactly.");
			LogAssert.AreEqual(next.Health,         restored.Health,         "Health delta must reconstruct exactly.");
			LogAssert.AreEqual(prev.MaxHealth,      restored.MaxHealth,      "Unchanged MaxHealth must be carried forward from prev.");
			LogAssert.AreEqual(prev.Mana,           restored.Mana,           "Unchanged Mana must be carried forward from prev.");
			LogAssert.AreEqual(prev.MaxMana,        restored.MaxMana,        "Unchanged MaxMana must be carried forward from prev.");
			LogAssert.AreEqual(prev.Stamina,        restored.Stamina,        "Unchanged Stamina must be carried forward from prev.");
			LogAssert.AreEqual(prev.MaxStamina,     restored.MaxStamina,     "Unchanged MaxStamina must be carried forward from prev.");
		}

		[Test]
		public void DeltaSerializer_ForceWrite_EmitsAllFields_EvenWhenUnchanged()
		{
			var v = new CharacterAttributeResourceState
			{
				RegenTickAccum = 3u, Health = 70f, MaxHealth = 90, Mana = 40f, MaxMana = 45, Stamina = 20f, MaxStamina = 30
			};

			var writer = new Writer();
			// Any DeltaSerializerOption value other than Unset is treated as a forceWrite by the serializer.
			DeltaSerializerOption forceOption = System.Enum.GetValues(typeof(DeltaSerializerOption)) is System.Array arr && arr.Length > 1
				? (DeltaSerializerOption)arr.GetValue(1)
				: DeltaSerializerOption.Unset;
			LogAssert.IsTrue(forceOption != DeltaSerializerOption.Unset,
				"DeltaSerializerOption enum must expose at least one non-Unset value for the force path.");

			bool wrote = GenericDeltaWriter<CharacterAttributeResourceState>.Write(writer, v, v, forceOption);
			LogAssert.IsTrue(wrote, "Force-write path must always emit, even when prev == next.");

			var reader = new Reader(writer.GetArraySegment(), null);
			CharacterAttributeResourceState restored = GenericDeltaReader<CharacterAttributeResourceState>.Read(reader, v);

			LogAssert.AreEqual(v.RegenTickAccum, restored.RegenTickAccum, "Force-write RegenTickAccum must round-trip.");
			LogAssert.AreEqual(v.Health,         restored.Health,         "Force-write Health must round-trip.");
			LogAssert.AreEqual(v.MaxHealth,      restored.MaxHealth,      "Force-write MaxHealth must round-trip.");
			LogAssert.AreEqual(v.Mana,           restored.Mana,           "Force-write Mana must round-trip.");
			LogAssert.AreEqual(v.MaxMana,        restored.MaxMana,        "Force-write MaxMana must round-trip.");
			LogAssert.AreEqual(v.Stamina,        restored.Stamina,        "Force-write Stamina must round-trip.");
			LogAssert.AreEqual(v.MaxStamina,     restored.MaxStamina,     "Force-write MaxStamina must round-trip.");
		}
	}
}