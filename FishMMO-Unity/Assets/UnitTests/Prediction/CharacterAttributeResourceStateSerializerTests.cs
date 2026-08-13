using System;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Serializing;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
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
		/// <see cref="CharacterAttributeResourceStateSerializer.RegisterSerializers"/> is
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
				"RegisterSerializers",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			LogAssert.IsNotNull(register,
				"CharacterAttributeResourceStateSerializer.RegisterSerializers must exist for EditMode test bootstrap.");
			register.Invoke(null, null);
		}

		[Test]
		public void RegularSerializer_RoundTrip_PreservesAllFields()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(RegularSerializer_RoundTrip_PreservesAllFields),
					"The non-delta Write/Read pair must round-trip every CharacterAttributeResourceState field exactly.")
					.GetAwaiter().GetResult();

				var src = new CharacterAttributeResourceState
				{
					NextRegenTick = 17u,
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
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "STEP",
					$"wrote {writer.Position} bytes | src(H={src.Health},MP={src.Mana},ST={src.Stamina}) -> dst(H={dst.Health},MP={dst.Mana},ST={dst.Stamina})")
					.GetAwaiter().GetResult();

				LogAssert.AreEqual(src.NextRegenTick, dst.NextRegenTick, "NextRegenTick must round-trip.");
				LogAssert.AreEqual(src.Health,         dst.Health,         "Health must round-trip.");
				LogAssert.AreEqual(src.MaxHealth,      dst.MaxHealth,      "MaxHealth must round-trip.");
				LogAssert.AreEqual(src.Mana,           dst.Mana,           "Mana must round-trip.");
				LogAssert.AreEqual(src.MaxMana,        dst.MaxMana,        "MaxMana must round-trip.");
				LogAssert.AreEqual(src.Stamina,        dst.Stamina,        "Stamina must round-trip.");
				LogAssert.AreEqual(src.MaxStamina,     dst.MaxStamina,     "MaxStamina must round-trip.");

				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "SUCCESS", nameof(RegularSerializer_RoundTrip_PreservesAllFields)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "FAILURE", $"{nameof(RegularSerializer_RoundTrip_PreservesAllFields)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(RegularSerializer_RoundTrip_PreservesAllFields)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void DeltaSerializer_NoChanges_EmitsZeroBytes_AndReadReturnsPrev()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(DeltaSerializer_NoChanges_EmitsZeroBytes_AndReadReturnsPrev),
					"An identical prev/next delta must emit no bytes and leave the writer position unchanged.")
					.GetAwaiter().GetResult();

				var prev = new CharacterAttributeResourceState
				{
					NextRegenTick = 5u, Health = 100f, MaxHealth = 100, Mana = 50f, MaxMana = 50, Stamina = 25f, MaxStamina = 25
				};

				var writer = new Writer();
				int startPos = writer.Position;
				bool wrote = GenericDeltaWriter<CharacterAttributeResourceState>.Write(writer, prev, prev, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "STEP",
					$"no-change delta: wrote={wrote} startPos={startPos} endPos={writer.Position}")
					.GetAwaiter().GetResult();

				LogAssert.IsFalse(wrote, "Identical prev/next must produce no wire output.");
				LogAssert.AreEqual(startPos, writer.Position,
					"Writer position must not advance when there is nothing to send.");

				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "SUCCESS", nameof(DeltaSerializer_NoChanges_EmitsZeroBytes_AndReadReturnsPrev)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "FAILURE", $"{nameof(DeltaSerializer_NoChanges_EmitsZeroBytes_AndReadReturnsPrev)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(DeltaSerializer_NoChanges_EmitsZeroBytes_AndReadReturnsPrev)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void DeltaSerializer_HealthRegen_RoundTrip()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(DeltaSerializer_HealthRegen_RoundTrip),
					"A health+regen delta must emit and reconstruct changed fields while carrying unchanged fields forward from prev.")
					.GetAwaiter().GetResult();

				var prev = new CharacterAttributeResourceState
				{
					NextRegenTick = 0u, Health = 80f, MaxHealth = 100, Mana = 50f, MaxMana = 50, Stamina = 25f, MaxStamina = 25
				};
				var next = prev;
				next.NextRegenTick = 1u;
				next.Health = 85f;

				var writer = new Writer();
				bool wrote = GenericDeltaWriter<CharacterAttributeResourceState>.Write(writer, prev, next, DeltaSerializerOption.Unset);
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "STEP",
					$"delta wrote={wrote} bytes={writer.Position} | changed Health {prev.Health}->{next.Health}, RegenTick {prev.NextRegenTick}->{next.NextRegenTick}")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(wrote, "A non-trivial delta (regen accum + health) must emit output.");

				var reader = new Reader(writer.GetArraySegment(), null);
				CharacterAttributeResourceState restored = GenericDeltaReader<CharacterAttributeResourceState>.Read(reader, prev);

				LogAssert.AreEqual(next.NextRegenTick, restored.NextRegenTick, "NextRegenTick delta must reconstruct exactly.");
				LogAssert.AreEqual(next.Health,         restored.Health,         "Health delta must reconstruct exactly.");
				LogAssert.AreEqual(prev.MaxHealth,      restored.MaxHealth,      "Unchanged MaxHealth must be carried forward from prev.");
				LogAssert.AreEqual(prev.Mana,           restored.Mana,           "Unchanged Mana must be carried forward from prev.");
				LogAssert.AreEqual(prev.MaxMana,        restored.MaxMana,        "Unchanged MaxMana must be carried forward from prev.");
				LogAssert.AreEqual(prev.Stamina,        restored.Stamina,        "Unchanged Stamina must be carried forward from prev.");
				LogAssert.AreEqual(prev.MaxStamina,     restored.MaxStamina,     "Unchanged MaxStamina must be carried forward from prev.");

				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "SUCCESS", nameof(DeltaSerializer_HealthRegen_RoundTrip)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "FAILURE", $"{nameof(DeltaSerializer_HealthRegen_RoundTrip)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(DeltaSerializer_HealthRegen_RoundTrip)).GetAwaiter().GetResult();
			}
		}

		[Test]
		public void DeltaSerializer_ForceWrite_EmitsAllFields_EvenWhenUnchanged()
		{
			try
			{
				AuthTestTrace.LogTestStart(nameof(DeltaSerializer_ForceWrite_EmitsAllFields_EvenWhenUnchanged),
					"The force-write path must emit and round-trip all fields even when prev == next.")
					.GetAwaiter().GetResult();

				var v = new CharacterAttributeResourceState
				{
					NextRegenTick = 3u, Health = 70f, MaxHealth = 90, Mana = 40f, MaxMana = 45, Stamina = 20f, MaxStamina = 30
				};

				var writer = new Writer();
				// Any DeltaSerializerOption value other than Unset is treated as a forceWrite by the serializer.
				DeltaSerializerOption forceOption = System.Enum.GetValues(typeof(DeltaSerializerOption)) is System.Array arr && arr.Length > 1
					? (DeltaSerializerOption)arr.GetValue(1)
					: DeltaSerializerOption.Unset;
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "STEP",
					$"Resolved forceOption={forceOption} (non-Unset required for force path).")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(forceOption != DeltaSerializerOption.Unset,
					"DeltaSerializerOption enum must expose at least one non-Unset value for the force path.");

				bool wrote = GenericDeltaWriter<CharacterAttributeResourceState>.Write(writer, v, v, forceOption);
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "STEP",
					$"force-write wrote={wrote} bytes={writer.Position}")
					.GetAwaiter().GetResult();
				LogAssert.IsTrue(wrote, "Force-write path must always emit, even when prev == next.");

				var reader = new Reader(writer.GetArraySegment(), null);
				CharacterAttributeResourceState restored = GenericDeltaReader<CharacterAttributeResourceState>.Read(reader, v);

				LogAssert.AreEqual(v.NextRegenTick, restored.NextRegenTick, "Force-write NextRegenTick must round-trip.");
				LogAssert.AreEqual(v.Health,         restored.Health,         "Force-write Health must round-trip.");
				LogAssert.AreEqual(v.MaxHealth,      restored.MaxHealth,      "Force-write MaxHealth must round-trip.");
				LogAssert.AreEqual(v.Mana,           restored.Mana,           "Force-write Mana must round-trip.");
				LogAssert.AreEqual(v.MaxMana,        restored.MaxMana,        "Force-write MaxMana must round-trip.");
				LogAssert.AreEqual(v.Stamina,        restored.Stamina,        "Force-write Stamina must round-trip.");
				LogAssert.AreEqual(v.MaxStamina,     restored.MaxStamina,     "Force-write MaxStamina must round-trip.");

				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "SUCCESS", nameof(DeltaSerializer_ForceWrite_EmitsAllFields_EvenWhenUnchanged)).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("CharacterAttributeResourceStateSerializerTests", "FAILURE", $"{nameof(DeltaSerializer_ForceWrite_EmitsAllFields_EvenWhenUnchanged)}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(nameof(DeltaSerializer_ForceWrite_EmitsAllFields_EvenWhenUnchanged)).GetAwaiter().GetResult();
			}
		}
	}
}