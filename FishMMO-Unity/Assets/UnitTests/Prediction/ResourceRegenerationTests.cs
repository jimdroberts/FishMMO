using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Coverage for the resource regeneration cadence and the consumption lockout — issue #151.
	/// </summary>
	/// <remarks>
	/// Two complaints, one fix each. Recovery arrived as a handful of large jumps starting up to
	/// five seconds after the bar emptied, which read as the regen system having frozen; and regen
	/// ran while a resource was being spent, so sprinting drew stamina at 5/s while regen handed
	/// back 4/s and the cost was very nearly refunded as it was paid.
	/// <para>
	/// These drive <c>Regenerate(tick)</c> directly on a real controller with real attributes, so
	/// the schedule arithmetic, the lockout and the rate conversion are all the shipping ones.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ResourceRegenerationTests
	{
		/// <summary>Ticks per second the fixture simulates at.</summary>
		private const int TickRate = 60;

		/// <summary>Stamina Regeneration's authored value: 20 per five seconds, i.e. 4/s.</summary>
		private const int StaminaRegenPerWindow = 20;

		private GameObject gameObject;
		private CharacterAttributeController controller;
		private CharacterAttributeTemplate staminaTemplate;
		private CharacterAttributeTemplate staminaRegenTemplate;

		[SetUp]
		public void SetUp()
		{
			staminaRegenTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaRegenTemplate.name = "RegenTestStaminaRegeneration";
			staminaRegenTemplate.InitialValue = StaminaRegenPerWindow;
			staminaRegenTemplate.AddToCache(staminaRegenTemplate.name);

			staminaTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			staminaTemplate.name = "RegenTestStamina";
			staminaTemplate.InitialValue = 100;
			staminaTemplate.AddToCache(staminaTemplate.name);

			gameObject = new GameObject("ResourceRegenerationTest");
			controller = gameObject.AddComponent<CharacterAttributeController>();
		}

		[TearDown]
		public void TearDown()
		{
			if (gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}

			foreach (CharacterAttributeTemplate template in new[] { staminaTemplate, staminaRegenTemplate })
			{
				if (template != null)
				{
					template.RemoveFromCache();
					UnityEngine.Object.DestroyImmediate(template);
				}
			}
		}

		private static T Field<T>(object target, string name)
		{
			FieldInfo field = target.GetType().GetField(name,
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"{target.GetType().Name} must declare '{name}'.");
			return (T)field.GetValue(target);
		}

		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name,
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"{target.GetType().Name} must declare '{name}'.");
			field.SetValue(target, value);
		}

		private static float Const(string name)
		{
			FieldInfo field = typeof(CharacterAttributeController).GetField(name,
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			LogAssert.IsNotNull(field, $"CharacterAttributeController must declare '{name}'.");
			return (float)field.GetValue(null);
		}

		[Test]
		public void ThePulseIsOneSecond_NotFive_SoRecoveryIsNotAHandfulOfJumps()
		{
			float pulseSeconds = Field<float>(controller, "regenTickRate");

			LogAssert.IsTrue(pulseSeconds <= 1.0f,
				$"regen pulses every {pulseSeconds}s; at 100 stamina that is "
				+ $"{100f / (StaminaRegenPerWindow / Const("REGEN_AUTHORING_WINDOW_SECONDS") * pulseSeconds):F0} "
				+ "visible steps, which is what read as frozen (issue #151)");
		}

		[Test]
		public void TheFinerPulse_DeliversTheSameAmountPerSecond_NotFiveTimesAsMuch()
		{
			/* The cadence changed, the rate must not have. A template value is an amount per
			 * REGEN_AUTHORING_WINDOW_SECONDS, so the per-pulse share has to shrink in step with the
			 * pulse or this becomes a five-fold buff to every resource in the game. */
			float window = Const("REGEN_AUTHORING_WINDOW_SECONDS");
			float pulseSeconds = Field<float>(controller, "regenTickRate");

			float perSecond = StaminaRegenPerWindow / window;
			float perPulse = perSecond * pulseSeconds;
			float perSecondDelivered = perPulse / pulseSeconds;

			LogAssert.IsTrue(Mathf.Abs(perSecondDelivered - 4.0f) < 0.001f,
				$"stamina must still recover at 4/s, but the new cadence delivers {perSecondDelivered}/s");
		}

		[Test]
		public void TheLockoutIsScopedToTheResourceThatWasSpent()
		{
			/* Spending stamina must not stop mana returning. The lockout is keyed by template ID
			 * precisely so a sprinting caster still gets mana back. */
			SetField(controller, "regenConsumptionLockoutTicks", (uint)TickRate);

			var consumed = Field<System.Collections.Generic.Dictionary<int, uint>>(
				controller, "lastConsumedResourceTicks");
			consumed[staminaTemplate.ID] = 1000u;

			MethodInfo isLocked = typeof(CharacterAttributeController).GetMethod(
				"IsWithinConsumptionLockout", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(isLocked, "the lockout predicate must exist");

			LogAssert.IsTrue((bool)isLocked.Invoke(controller, new object[] { staminaTemplate.ID, 1001u }),
				"a resource spent one tick ago is still locked out");
			LogAssert.IsFalse((bool)isLocked.Invoke(controller, new object[] { staminaRegenTemplate.ID, 1001u }),
				"a resource that was never spent must not be locked out by another one");
		}

		[Test]
		public void TheLockoutExpires_SoStandingStillRecovers()
		{
			SetField(controller, "regenConsumptionLockoutTicks", (uint)TickRate);

			var consumed = Field<System.Collections.Generic.Dictionary<int, uint>>(
				controller, "lastConsumedResourceTicks");
			consumed[staminaTemplate.ID] = 1000u;

			MethodInfo isLocked = typeof(CharacterAttributeController).GetMethod(
				"IsWithinConsumptionLockout", BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsTrue((bool)isLocked.Invoke(controller, new object[] { staminaTemplate.ID, 1000u + (uint)TickRate - 1u }),
				"still locked out one tick before the window closes");
			LogAssert.IsFalse((bool)isLocked.Invoke(controller, new object[] { staminaTemplate.ID, 1000u + (uint)TickRate }),
				"regen resumes exactly one second after the last spend");
		}

		/// <summary>
		/// Stands a real stamina resource up inside the controller and drives Regenerate directly.
		/// </summary>
		/// <returns>The live stamina attribute.</returns>
		private CharacterResourceAttribute ArmRealStamina()
		{
			/* Resource clamping reads Character.Flags, so the controller needs a character before
			 * any attribute can be constructed against it. Assigned through the property's backing
			 * field rather than InitializeOnce so the controller's own initialisation — which
			 * resolves templates off a live character — stays out of this fixture. */
			typeof(CharacterBehaviour)
				.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public)
				.SetValue(controller, new Harness.StubCharacter());

			CharacterResourceAttribute stamina = new CharacterResourceAttribute(
				controller, staminaTemplate.ID, initialValue: 100, currentValue: 100f, modifier: 0);
			CharacterAttribute regen = new CharacterAttribute(
				controller, staminaRegenTemplate.ID, StaminaRegenPerWindow, initialModifier: 0);

			controller.StaminaResourceTemplateID = staminaTemplate.ID;
			Field<System.Collections.Generic.Dictionary<int, CharacterResourceAttribute>>(
				controller, "resourceAttributes")[staminaTemplate.ID] = stamina;

			SetField(controller, "cachedStaminaRegen", regen);
			SetField(controller, "regenTickInterval", (uint)TickRate);
			SetField(controller, "regenConsumptionLockoutTicks", (uint)TickRate);

			return stamina;
		}

		[Test]
		public void Idle_StaminaRecoversAtFourPerSecond_InSmallSteps()
		{
			CharacterResourceAttribute stamina = ArmRealStamina();
			stamina.Consume(100f);
			LogAssert.IsTrue(stamina.CurrentValue <= 0f, "the bar starts empty");

			// Five seconds of standing still, one Regenerate per tick as OnReplicate does.
			for (uint tick = 1; tick <= TickRate * 5; tick++)
			{
				controller.Regenerate(tick);
			}

			/* Four pulses land inside the window: the first is scheduled a full interval after the
			 * first processed tick, so five seconds of ticks yields four seconds of recovery. */
			LogAssert.IsTrue(stamina.CurrentValue >= 15f && stamina.CurrentValue <= 21f,
				$"five seconds idle should return roughly 16 stamina, but returned {stamina.CurrentValue}");
		}

		[Test]
		public void WhileBeingSpent_StaminaDoesNotRegenerate()
		{
			CharacterResourceAttribute stamina = ArmRealStamina();
			stamina.Consume(50f);

			float atStart = stamina.CurrentValue;
			float spent = 0f;

			/* Sprinting: 5 stamina a second, drawn every tick, for five seconds. Regen must not
			 * refund any of it -- this is the case where the bar used to barely move. */
			const float sprintCostPerSecond = 5.0f;
			for (uint tick = 1; tick <= TickRate * 5; tick++)
			{
				float cost = sprintCostPerSecond / TickRate;
				stamina.Consume(cost);
				spent += cost;
				controller.Regenerate(tick);
			}

			float expected = atStart - spent;

			LogAssert.IsTrue(Mathf.Abs(stamina.CurrentValue - expected) < 0.001f,
				$"sprinting must cost the full {spent:F1} stamina, but the bar went from {atStart:F1} "
				+ $"to {stamina.CurrentValue:F1} instead of {expected:F1} -- regen refunded some of it");
		}

		[Test]
		public void AfterSpendingStops_RecoveryResumes()
		{
			CharacterResourceAttribute stamina = ArmRealStamina();
			stamina.Consume(50f);

			uint tick = 1;
			for (; tick <= TickRate * 2; tick++)
			{
				stamina.Consume(5.0f / TickRate);
				controller.Regenerate(tick);
			}

			float whenSprintStopped = stamina.CurrentValue;

			// Stand still for four seconds.
			for (uint end = tick + (uint)TickRate * 4u; tick <= end; tick++)
			{
				controller.Regenerate(tick);
			}

			LogAssert.IsTrue(stamina.CurrentValue > whenSprintStopped + 5f,
				$"standing still must recover stamina, but it went {whenSprintStopped:F1} -> {stamina.CurrentValue:F1}");
		}

		[Test]
		public void ARisingValue_IsNotMistakenForConsumption()
		{
			/* Regeneration itself raises the value. If a rise counted as a spend, the first pulse
			 * would arm the lockout and the resource would suppress its own recovery forever. */
			MethodInfo note = typeof(CharacterAttributeController).GetMethod(
				"NoteResourceConsumption", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(note, "the consumption sampler must exist");

			var observed = Field<System.Collections.Generic.Dictionary<int, float>>(
				controller, "lastObservedResourceValues");
			var consumed = Field<System.Collections.Generic.Dictionary<int, uint>>(
				controller, "lastConsumedResourceTicks");

			// A resource absent from the controller is skipped without recording anything.
			note.Invoke(controller, new object[] { 0, 10u });

			LogAssert.AreEqual(0, observed.Count, "an absent resource records no sample");
			LogAssert.AreEqual(0, consumed.Count, "an absent resource never arms the lockout");
		}
	}
}
