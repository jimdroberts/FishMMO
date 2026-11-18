using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins how the two ground-speed attributes combine, and what each stance is worth once they do.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The report behind this (issue #279, "Sprint does not stack with Movement Speed Buffs") is a
	/// control-flow defect, not an arithmetic one. <c>UpdateGroundMovement</c> resolved sprint and
	/// the Move Speed attribute as an <c>if / else if</c> pair, so holding sprint made the Move
	/// Speed branch unreachable: <c>Minor Increase Move Speed</c> was worth +30% walking and exactly
	/// 0% sprinting, and <c>Mock Slow Debuff</c> did not slow a sprinting character at all. The two
	/// are separate attributes with separate ledgers and both have to be read.
	/// </para>
	/// <para>
	/// These tests assert on speed RATIOS rather than on the constants, because the constants are
	/// tunable and the relationship between the numbers is the contract. A test that hard-coded
	/// 6.0 m/s would fail the day someone retunes sprint and would say nothing about whether the
	/// buff still applies.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class KCCGroundSpeedTests
	{
		/// <summary>
		/// The stock sprint/run ratio, derived from the constants rather than authored a second time.
		/// Sprint Speed is authored as a percentage (100 = this ratio).
		/// </summary>
		private const float StockSprintRatio = Constants.Character.SprintSpeed / Constants.Character.RunSpeed;

		private GameObject gameObject;
		private KCCController controller;
		private CharacterAttributeController attributes;
		private CharacterAttributeTemplate moveSpeedTemplate;
		private CharacterAttributeTemplate sprintSpeedTemplate;
		private CharacterAttribute moveSpeed;
		private CharacterAttribute sprintSpeed;
		private CharacterAttributeTemplate[] templates;

		[SetUp]
		public void SetUp()
		{
			moveSpeedTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			moveSpeedTemplate.name = "KCCGroundSpeed_MoveSpeed";
			moveSpeedTemplate.InitialValue = 100;
			moveSpeedTemplate.IsPercentage = true;
			moveSpeedTemplate.AddToCache(moveSpeedTemplate.name);

			sprintSpeedTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			sprintSpeedTemplate.name = "KCCGroundSpeed_SprintSpeed";
			sprintSpeedTemplate.InitialValue = 100;
			sprintSpeedTemplate.IsPercentage = true;
			sprintSpeedTemplate.AddToCache(sprintSpeedTemplate.name);

			templates = new[] { moveSpeedTemplate, sprintSpeedTemplate };

			gameObject = new GameObject("KCCGroundSpeedTest");
			controller = gameObject.AddComponent<KCCController>();
			controller.MoveSpeedTemplate = moveSpeedTemplate;
			controller.SprintSpeedTemplate = sprintSpeedTemplate;

			attributes = gameObject.AddComponent<CharacterAttributeController>();
			moveSpeed = new CharacterAttribute(attributes, moveSpeedTemplate.ID, 100, 0);
			sprintSpeed = new CharacterAttribute(attributes, sprintSpeedTemplate.ID, 100, 0);
			attributes.AddAttribute(moveSpeed);
			attributes.AddAttribute(sprintSpeed);
		}

		[TearDown]
		public void TearDown()
		{
			if (gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(gameObject);
			}

			foreach (CharacterAttributeTemplate template in templates)
			{
				if (template != null)
				{
					template.RemoveFromCache();
					UnityEngine.Object.DestroyImmediate(template);
				}
			}
		}

		/// <summary>
		/// States a Move Speed contribution the way a buff does — through the attributed ledger, so
		/// the value reaches <c>FinalValue</c> by the same route production uses.
		/// </summary>
		private void ApplyMoveSpeedBuff(int bonus)
		{
			moveSpeed.SetSource(ModifierSource.Buff(moveSpeedTemplate.ID), bonus);
		}

		private float Resolve(bool crouching, bool sprinting, float magnitude = 1f)
		{
			return controller.ResolveGroundSpeed(crouching, sprinting, magnitude, attributes);
		}

		[Test]
		public void StockMovement_IsTheAuthoredRunAndSprintConstants()
		{
			Assert.That(Resolve(false, false), Is.EqualTo(Constants.Character.RunSpeed).Within(0.0001f),
				"Running, unbuffed, is the run constant.");
			Assert.That(Resolve(false, true), Is.EqualTo(Constants.Character.SprintSpeed).Within(0.0001f),
				"Sprinting, unbuffed, is the sprint constant.");
		}

		/// <summary>
		/// The issue itself: a Move Speed buff has to reach a sprinting character.
		/// </summary>
		[Test]
		public void MoveSpeedBuff_AppliesWhileSprinting_NotJustWhileRunning()
		{
			ApplyMoveSpeedBuff(30);

			float run = Resolve(false, false);
			float sprint = Resolve(false, true);

			Assert.That(run, Is.EqualTo(Constants.Character.RunSpeed * 1.30f).Within(0.0001f),
				"A +30% buff is +30% running.");
			Assert.That(sprint, Is.EqualTo(Constants.Character.RunSpeed * StockSprintRatio * 1.30f).Within(0.0001f),
				"...and +30% sprinting. This is the defect in issue #279: the buff used to be discarded "
				+ "the moment sprint was held, because the two were if / else if alternatives.");

			Assert.That(sprint, Is.EqualTo(run * StockSprintRatio).Within(0.0001f),
				"Sprint has to stay the same multiple of run. Scaling one and not the other is how the "
				+ "buff went missing.");
		}

		[Test]
		public void MoveSpeedDebuff_SlowsWhileSprinting_NotJustWhileRunning()
		{
			ApplyMoveSpeedBuff(-50);

			float run = Resolve(false, false);
			float sprint = Resolve(false, true);

			Assert.That(run, Is.EqualTo(Constants.Character.RunSpeed * 0.50f).Within(0.0001f),
				"A -50% debuff halves run speed.");
			Assert.That(sprint, Is.EqualTo(Constants.Character.RunSpeed * StockSprintRatio * 0.50f).Within(0.0001f),
				"Mock Slow Debuff has to slow a sprinting character. Before the fix a sprinting "
				+ "character was immune to it.");

			Assert.That(sprint, Is.LessThan(Constants.Character.SprintSpeed),
				$"A slowed sprint ({sprint}) has to be slower than an unslowed one "
				+ $"({Constants.Character.SprintSpeed}), or the debuff is cosmetic.");
		}

		/// <summary>
		/// Sprint Speed is the sprint/run ratio, so it still authorises the sprint gap while Move
		/// Speed scales both. Losing this would make the NPC-authored Sprint Speed rolls inert.
		/// </summary>
		[Test]
		public void SprintSpeedAttribute_StillScalesTheSprintGap()
		{
			sprintSpeed.SetSource(ModifierSource.Buff(sprintSpeedTemplate.ID), 10);

			Assert.That(Resolve(false, true), Is.EqualTo(Constants.Character.SprintSpeed * 1.10f).Within(0.0001f),
				"Sprint Speed 110 must still be worth +10% over the stock sprint.");

			Assert.That(Resolve(false, false), Is.EqualTo(Constants.Character.RunSpeed).Within(0.0001f),
				"Sprint Speed must not touch run speed — that is Move Speed's job.");
		}

		[Test]
		public void BothAttributes_CompoundRatherThanReplace()
		{
			ApplyMoveSpeedBuff(30);
			sprintSpeed.SetSource(ModifierSource.Buff(sprintSpeedTemplate.ID), 10);

			float expected = Constants.Character.RunSpeed * StockSprintRatio * 1.10f * 1.30f;

			Assert.That(Resolve(false, true), Is.EqualTo(expected).Within(0.0001f),
				"A separate buff to each attribute has to compound. An implementation that read only "
				+ "one of them would land on the stock sprint or on the run buff and look plausible.");
		}

		[Test]
		public void Crouch_IgnoresBothSpeedAttributes()
		{
			ApplyMoveSpeedBuff(200);
			sprintSpeed.SetSource(ModifierSource.Buff(sprintSpeedTemplate.ID), 200);

			Assert.That(Resolve(true, false), Is.EqualTo(Constants.Character.CrouchSpeed).Within(0.0001f),
				"Crouch is a stance, not a speed attribute — no buff scales it.");
			Assert.That(Resolve(true, true), Is.EqualTo(Constants.Character.CrouchSpeed).Within(0.0001f),
				"...and a sprint request while crouching does not beat the stance either.");
		}

		/// <summary>
		/// Exhaustion is resolved by the caller, which passes <c>sprinting: false</c>. The character
		/// then runs — with the Move Speed buff intact, which is the part the old <c>else if</c> got
		/// wrong twice over: it fell to bare <c>RunSpeed</c>, slower than not holding sprint at all.
		/// </summary>
		[Test]
		public void ExhaustedSprint_FallsBackToBuffedRunSpeed_NotUnbuffedRunSpeed()
		{
			ApplyMoveSpeedBuff(30);

			float exhausted = Resolve(false, false);

			Assert.That(exhausted, Is.EqualTo(Constants.Character.RunSpeed * 1.30f).Within(0.0001f),
				"A character that has run out of stamina must not lose their Move Speed buff. Holding "
				+ "sprint at zero stamina used to move SLOWER than releasing it.");
			Assert.That(exhausted, Is.LessThan(Resolve(false, true)),
				"Exhaustion still has to cost the sprint bonus.");
		}

		[Test]
		public void StationarySprint_IsRunSpeed_NotSprintSpeed()
		{
			ApplyMoveSpeedBuff(30);

			Assert.That(Resolve(false, true, 0f), Is.EqualTo(Constants.Character.RunSpeed * 1.30f).Within(0.0001f),
				"Holding sprint with no move input must not produce sprint speed — the character is "
				+ "standing still.");
		}

		/// <summary>
		/// A character with no attribute controller still gets the stance behaviours, and still
		/// cannot be scaled by anything.
		/// </summary>
		[Test]
		public void NoAttributeController_StillHonoursStances()
		{
			Assert.That(controller.ResolveGroundSpeed(false, false, 1f, null),
				Is.EqualTo(Constants.Character.RunSpeed).Within(0.0001f), "Run.");
			Assert.That(controller.ResolveGroundSpeed(false, true, 1f, null),
				Is.EqualTo(Constants.Character.SprintSpeed).Within(0.0001f), "Sprint.");
			Assert.That(controller.ResolveGroundSpeed(true, false, 1f, null),
				Is.EqualTo(Constants.Character.CrouchSpeed).Within(0.0001f), "Crouch.");
		}

		/// <summary>
		/// The exploit cap is the resolver's output, not a later step, so nothing can reach past it
		/// by reading the speed the resolver returns.
		/// </summary>
		[Test]
		public void StackedBuffs_CannotExceedTheExploitCap()
		{
			const float cap = Constants.Character.SprintSpeed * 3.0f;

			ApplyMoveSpeedBuff(200);
			sprintSpeed.SetSource(ModifierSource.Buff(sprintSpeedTemplate.ID), 200);

			float sprint = Resolve(false, true);
			float run = Resolve(false, false);

			Assert.That(sprint, Is.LessThanOrEqualTo(cap), $"Sprint reached {sprint}, past the {cap} cap.");
			Assert.That(run, Is.LessThanOrEqualTo(cap), $"Run reached {run}, past the {cap} cap.");
		}
	}
}
