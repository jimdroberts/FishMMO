using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Water half of <see cref="AIController"/>: which NavMesh areas the agent may walk (deep water for a swimmer that
	/// chose to go in, lava for the fire-immune), the decision a chase across water comes to
	/// (<see cref="AISwimRisk"/>), a surface swimmer floating over an agent that walks the bed, and the breath of one
	/// that swims under.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The mask follows the decision.</b> Deep Water is in the agent's mask only while the NPC is swimming or has
	/// chosen to: otherwise even a swimmer paths round a lake while it wanders or goes home, and never wets a foot it
	/// need not. Once in, the mask stays wide until it is out, because an agent standing on an area its mask excludes
	/// cannot path at all.
	/// </para>
	/// <para>
	/// <b>Server only,</b> as the whole brain is: there is no prediction here, and the breath is a plain timer.
	/// </para>
	/// </remarks>
	public partial class AIController
	{
		/// <summary>Seconds between chase-across-water decisions: the water does not move, and the target is slow in it.</summary>
		private const float SwimDecisionInterval = 0.75f;

		/// <summary>Seconds between asking the NavMesh whether the NPC stands in deep water.</summary>
		private const float DeepWaterProbeInterval = 0.25f;

		/// <summary>Metres between the samples along a chase's line that measure how much of it is deep water.</summary>
		private const float WaterSampleStep = 4f;

		private static readonly int FireDamageID = (nameof(DamageAttributeTemplate) + "Fire Damage").GetDeterministicHashCode();
		private static readonly int DrowningDamageID = (nameof(DamageAttributeTemplate) + "Drowning Damage").GetDeterministicHashCode();

		private float swimDecisionTimer;
		private float deepWaterProbeTimer;
		private bool inDeepWater;
		private bool agentSwimming;
		private float npcBreath = -1f;

		/// <summary>Whether and how this NPC swims, or null when the archetype says nothing (it does not).</summary>
		public AISwimSettings Swim => archetype != null ? archetype.Swim : null;

		/// <summary>What the last chase across water came to.</summary>
		public WaterVerdict WaterVerdict { get; private set; }

		/// <summary>Seconds this NPC has waited at the water's edge for a target out in it.</summary>
		public float ShoreWaitTimer;

		/// <summary>Whether the NPC stands on deep water now (the NavMesh's Deep Water area).</summary>
		public bool InDeepWater => inDeepWater;

		/// <summary>Seconds of breath this NPC has left (divers); its archetype's full breath when it has not been under.</summary>
		public float NpcBreathSeconds => npcBreath < 0f ? (Swim != null ? Swim.BreathSeconds : 0f) : npcBreath;

		private void ResetSwimming()
		{
			swimDecisionTimer = 0f;
			deepWaterProbeTimer = 0f;
			inDeepWater = false;
			agentSwimming = false;
			npcBreath = -1f;
			ShoreWaitTimer = 0f;
			WaterVerdict = WaterVerdict.Land;
			// A pooled body's next occupant may be another race: lava is ground only to the fire-immune.
			ApplySwimAreas(swimming: false);
		}

		/// <summary>True when this NPC's race takes no fire damage: lava is ground to it.</summary>
		private bool IsLavaWalker()
		{
			RaceTemplate race = RaceTemplate.Of(Character);
			return race != null && race.IsImmuneTo(CharacterAttributeTemplate.Get<DamageAttributeTemplate>(FireDamageID));
		}

		/// <summary>
		/// Sets the areas the agent may walk: land always, lava for the fire-immune, and deep water while
		/// <paramref name="swimming"/> for an archetype that swims, at the cost its courage and skill put on it.
		/// </summary>
		private void ApplySwimAreas(bool swimming)
		{
			if (Agent == null)
			{
				return;
			}
			int mask = LandAreas;
			if (LavaArea >= 0 && Character != null && IsLavaWalker())
			{
				mask |= 1 << LavaArea;
				Agent.SetAreaCost(LavaArea, 1f);
			}
			AISwimSettings swim = Swim;
			agentSwimming = swimming && swim != null && swim.CanSwim && DeepWaterArea >= 0;
			if (agentSwimming)
			{
				mask |= 1 << DeepWaterArea;
				Agent.SetAreaCost(DeepWaterArea, swim.DeepWaterCost);
			}
			if (Agent.areaMask != mask)
			{
				Agent.areaMask = mask;
			}
		}

		/// <summary>
		/// What to do about a target the way to which may be through water: measured along the line to it, scored by
		/// <see cref="AISwimRisk"/>, and the agent's areas set to match. Asked by the attacking state before each move.
		/// </summary>
		public WaterVerdict DecideChase(Vector3 targetPosition)
		{
			if (Character == null || Agent == null)
			{
				return WaterVerdict = WaterVerdict.Land;
			}
			swimDecisionTimer -= StateDeltaTime;
			if (swimDecisionTimer > 0f)
			{
				return WaterVerdict;
			}
			swimDecisionTimer = SwimDecisionInterval;

			UnityEngine.SceneManagement.Scene scene = Character.GameObject.scene;
			if (!WaterQuery.HasWater(scene) || DeepWaterArea < 0)
			{
				ShoreWaitTimer = 0f;
				return WaterVerdict = WaterVerdict.Land;
			}

			WaterSample atTarget = WaterQuery.Sample(scene, targetPosition);
			float leash = CurrentState != null ? CurrentState.MaxLeashRange : 0f;
			var input = new AISwimRiskInput
			{
				WaterMetres = DeepWaterAlong(Character.Transform.position, targetPosition),
				// Measured at the target's middle: a target swimming at the surface is not one that has dived.
				TargetDepth = atTarget.Present ? Mathf.Max(0f, atTarget.Surface - (targetPosition.y + 1f)) : 0f,
				LandSpeed = Agent.speed,
				TargetValue = 1f,
				LeashShare = leash > 0f ? Vector3.Distance(Home, targetPosition) / leash : 0f,
				AlreadySwimming = inDeepWater,
			};
			WaterVerdict verdict = AISwimRisk.Evaluate(Swim, input);

			// A diver running out of breath turns for the shore whatever the target is worth.
			AISwimSettings swim = Swim;
			if (verdict == WaterVerdict.Swim && swim != null && swim.HoldsBreath && NpcBreathSeconds < swim.BreathSeconds * 0.35f)
			{
				verdict = WaterVerdict.WaitAtShore;
			}

			ApplySwimAreas(verdict == WaterVerdict.Swim || inDeepWater);
			if (verdict != WaterVerdict.WaitAtShore)
			{
				ShoreWaitTimer = 0f;
			}
			return WaterVerdict = verdict;
		}

		/// <summary>Metres of deep water (the NavMesh's area) on the straight line between two points.</summary>
		private static float DeepWaterAlong(Vector3 from, Vector3 to)
		{
			Vector3 line = to - from;
			line.y = 0f;
			float length = line.magnitude;
			if (length < 0.01f)
			{
				return 0f;
			}
			int samples = Mathf.Clamp(Mathf.CeilToInt(length / WaterSampleStep), 1, 64);
			float step = length / samples;
			int deep = 1 << DeepWaterArea;
			int wet = 0;
			for (int i = 0; i <= samples; i++)
			{
				Vector3 at = Vector3.Lerp(from, to, i / (float)samples);
				if (NavMesh.SamplePosition(at, out NavMeshHit hit, 1.5f, deep))
				{
					wet++;
				}
			}
			return wet * step;
		}

		/// <summary>The nearest land to the NPC, for one in the water that has decided against it.</summary>
		public bool TryFindShore(out Vector3 shore)
		{
			return TrySampleNavMesh(Character.Transform.position, out shore, 6f, LandAreas);
		}

		/// <summary>
		/// Where a swimmer's body is over its agent: a surface swimmer floats with its head out wherever the water is
		/// deeper than that; a diver or bed-walker, and anything on land, stands where the agent does.
		/// </summary>
		private Vector3 SwimHeightOver(Vector3 agentPosition)
		{
			if (!inDeepWater || Character == null)
			{
				return agentPosition;
			}
			AISwimSettings swim = Swim;
			if (swim == null || !swim.CanSwim || swim.Mode != AISwimMode.Surface)
			{
				return agentPosition;
			}
			WaterSample water = WaterQuery.Sample(Character.GameObject.scene, agentPosition);
			if (!water.Present)
			{
				return agentPosition;
			}
			float floating = water.Surface - Constants.Character.SwimFloatSubmersion * Mathf.Max(0.5f, Agent.height);
			if (floating > agentPosition.y)
			{
				agentPosition.y = floating;
			}
			return agentPosition;
		}

		/// <summary>
		/// One network tick of the water: whether the NPC stands in deep water, its areas back to land once it is out,
		/// and a diver's breath, with drowning on the server's once-a-second pulse when it runs out.
		/// </summary>
		private void TickSwimming(float tickDelta)
		{
			if (Character == null || Agent == null || DeepWaterArea < 0)
			{
				return;
			}
			deepWaterProbeTimer -= tickDelta;
			if (deepWaterProbeTimer <= 0f)
			{
				deepWaterProbeTimer = DeepWaterProbeInterval;
				inDeepWater = NavMesh.SamplePosition(Agent.nextPosition, out NavMeshHit hit, 0.5f, NavMesh.AllAreas) &&
					(hit.mask & (1 << DeepWaterArea)) != 0;
				// Out of the water and not chasing back into it: land only again, so it goes round lakes when it wanders.
				if (!inDeepWater && agentSwimming && WaterVerdict != WaterVerdict.Swim)
				{
					ApplySwimAreas(swimming: false);
				}
			}

			AISwimSettings swim = Swim;
			if (swim == null || !swim.HoldsBreath)
			{
				return;
			}
			bool holding = false;
			if (inDeepWater)
			{
				Vector3 at = Character.Transform.position;
				WaterSample water = WaterQuery.Sample(Character.GameObject.scene, at);
				holding = water.Present && at.y + Agent.height < water.Surface;
			}
			npcBreath = BreathRules.Step(NpcBreathSeconds, swim.BreathSeconds, holding, tickDelta);
			if (!holding || npcBreath > 0f || !BreathRules.IsPulseTick(WeatherQuery.CurrentTick, tickDelta))
			{
				return;
			}
			if (Character.TryGet(out ICharacterDamageController damage) && damage.IsAlive &&
				Character.TryGet(out ICharacterAttributeController attributes) &&
				attributes.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				damage.Damage(null, BreathRules.DrowningDamage(health.FinalValue), CharacterAttributeTemplate.Get<DamageAttributeTemplate>(DrowningDamageID), ignoreAchievements: true, periodic: true);
			}
		}
	}
}
