using FishMMO.Server.Implementation.World.SceneServer.AI;
using NUnit.Framework;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// The chase-across-water decision (Jim, 2026-10-05: not all NPCs swim; those that do may drown, so they choose
	/// between chasing in and staying on land): scored from the water, the breath, the courage and the target.
	/// </summary>
	[TestFixture]
	public class AISwimRiskTests
	{
		private static AISwimSettings Swimmer(float courage = 0.5f, float skill = 0.5f, AISwimMode mode = AISwimMode.Surface, float breath = 20f) =>
			new AISwimSettings { CanSwim = true, Courage = courage, SwimSkill = skill, Mode = mode, BreathSeconds = breath };

		private static AISwimRiskInput Across(float metres, float targetDepth = 0f, float leash = 0.3f) => new AISwimRiskInput
		{
			WaterMetres = metres,
			TargetDepth = targetDepth,
			LandSpeed = 4f,
			TargetValue = 1f,
			LeashShare = leash,
		};

		[Test]
		public void NoWaterOnTheWayIsAnOrdinaryChase()
		{
			Assert.That(AISwimRisk.Evaluate(null, Across(0f)), Is.EqualTo(WaterVerdict.Land));
		}

		[Test]
		public void ANonSwimmerWaitsOnTheShore()
		{
			Assert.That(AISwimRisk.Evaluate(new AISwimSettings(), Across(10f)), Is.EqualTo(WaterVerdict.WaitAtShore));
			Assert.That(AISwimRisk.Evaluate(null, Across(10f)), Is.EqualTo(WaterVerdict.WaitAtShore));
		}

		[Test]
		public void PastTheLeashItLetsGo()
		{
			Assert.That(AISwimRisk.Evaluate(Swimmer(1f, 1f), Across(5f, leash: 1.2f)), Is.EqualTo(WaterVerdict.GiveUp));
		}

		[Test]
		public void ABoldSwimmerCrossesWhatATimidOneWillNot()
		{
			AISwimRiskInput river = Across(40f);
			Assert.That(AISwimRisk.Evaluate(Swimmer(courage: 0.9f, skill: 0.8f), river), Is.EqualTo(WaterVerdict.Swim));
			Assert.That(AISwimRisk.Evaluate(Swimmer(courage: 0.05f, skill: 0.3f), river), Is.EqualTo(WaterVerdict.WaitAtShore));
		}

		[Test]
		public void ASurfaceSwimmerCannotFollowADiver()
		{
			Assert.That(AISwimRisk.Evaluate(Swimmer(1f, 1f), Across(5f, targetDepth: 6f)), Is.EqualTo(WaterVerdict.WaitAtShore));
			Assert.That(AISwimRisk.Evaluate(Swimmer(1f, 1f, AISwimMode.Diver, 60f), Across(5f, targetDepth: 6f)), Is.EqualTo(WaterVerdict.Swim));
		}

		[Test]
		public void ADiverWillNotGoFurtherThanItsBreathTakesItAndBack()
		{
			// 60 m at a diver's speed and a dive, against 10 s of breath of which it spends 40% getting there.
			Assert.That(AISwimRisk.Evaluate(Swimmer(1f, 1f, AISwimMode.Diver, 10f), Across(60f, targetDepth: 4f)), Is.EqualTo(WaterVerdict.WaitAtShore));
		}

		[Test]
		public void OnceInItHoldsToItsChoice()
		{
			AISwimSettings swim = Swimmer(courage: 0.3f, skill: 0.5f);
			AISwimRiskInput input = Across(70f);
			Assert.That(AISwimRisk.Evaluate(swim, input), Is.EqualTo(WaterVerdict.WaitAtShore), "from the shore, not worth it");
			input.AlreadySwimming = true;
			Assert.That(AISwimRisk.Evaluate(swim, input), Is.EqualTo(WaterVerdict.Swim), "but turning back from halfway costs as much");
		}

		[Test]
		public void BoldAndAbleSwimmersPayLessForDeepWater()
		{
			Assert.That(Swimmer(1f, 1f).DeepWaterCost, Is.LessThan(Swimmer(0.2f, 0.2f).DeepWaterCost));
		}
	}
}
