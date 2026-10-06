using System;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>How a swimming NPC goes through water.</summary>
	public enum AISwimMode : byte
	{
		/// <summary>Swims at the surface, head out: it never drowns, and cannot follow anything that dives.</summary>
		Surface = 0,
		/// <summary>Swims under water, along the bed, holding its breath: it can drown.</summary>
		Diver = 1,
		/// <summary>Walks the bed under water (crabs, golems): it holds its breath as a diver does.</summary>
		BottomWalker = 2,
	}

	/// <summary>What an NPC chasing something across water decides to do.</summary>
	public enum WaterVerdict : byte
	{
		/// <summary>No deep water on the way: an ordinary chase.</summary>
		Land = 0,
		/// <summary>Into the water after it.</summary>
		Swim = 1,
		/// <summary>Stay on the shore: face it, use anything with the reach, and wait for it to come back.</summary>
		WaitAtShore = 2,
		/// <summary>Not worth it: let it go.</summary>
		GiveUp = 3,
	}

	/// <summary>
	/// Whether, and how, an archetype's NPCs swim (Jim, 2026-10-05: not all NPCs can swim; swimming NPCs will probably
	/// drown, and an AI decides whether to chase into the water or stay on land).
	/// </summary>
	/// <remarks>
	/// Off by default: an archetype that says nothing never sets foot in deep water, which is what every existing
	/// creature did before there was any (the NavMesh's Deep Water area is outside its mask).
	/// </remarks>
	[Serializable]
	public class AISwimSettings
	{
		[Tooltip("Whether this archetype's NPCs go into water too deep to wade at all.")]
		public bool CanSwim;

		[Tooltip("How it swims: at the surface, head out; or under, holding its breath.")]
		public AISwimMode Mode = AISwimMode.Surface;

		[Tooltip("How good a swimmer it is, 0 to 1: how far it will swim, and how fast.")]
		[Range(0f, 1f)] public float SwimSkill = 0.5f;

		[Tooltip("Seconds it can hold its breath under water (divers and bed-walkers).")]
		[Min(1f)] public float BreathSeconds = 20f;

		[Tooltip("How much risk it will take for a target, 0 (never wets a foot it need not) to 1 (into anything).")]
		[Range(0f, 1f)] public float Courage = 0.5f;

		[Tooltip("Seconds it waits on the shore for a target out in the water before letting it go.")]
		[Min(1f)] public float ShorePatience = 20f;

		/// <summary>Its speed in water, m/s, from its speed on land: a third to two thirds of it, by skill.</summary>
		public float SwimSpeed(float landSpeed) => Mathf.Max(0.5f, landSpeed * Mathf.Lerp(0.33f, 0.66f, SwimSkill));

		/// <summary>
		/// What Deep Water costs its paths, against land's 1: a bold, able swimmer cuts across, a timid one goes round
		/// if there is a way round at all.
		/// </summary>
		public float DeepWaterCost => Mathf.Lerp(10f, 1.5f, Courage * SwimSkill);

		/// <summary>True when its head is under water as it swims, so it holds its breath.</summary>
		public bool HoldsBreath => Mode != AISwimMode.Surface;
	}

	/// <summary>The facts a chase across water is decided on.</summary>
	public struct AISwimRiskInput
	{
		/// <summary>Metres of water too deep to wade between the NPC and its target.</summary>
		public float WaterMetres;
		/// <summary>How far under the surface the target is, metres (0 at or above it).</summary>
		public float TargetDepth;
		/// <summary>The NPC's speed on land, m/s.</summary>
		public float LandSpeed;
		/// <summary>How much the target matters, 0 to 1: the top of its threat table is 1.</summary>
		public float TargetValue;
		/// <summary>How far the target stands from the NPC's home, against how far the NPC may go: 1 is the leash.</summary>
		public float LeashShare;
		/// <summary>Whether the NPC is already in the water.</summary>
		public bool AlreadySwimming;
	}

	/// <summary>
	/// The decision an NPC makes when the way to its target is through deep water: scored, never scripted. The risk is
	/// how long it would be in the water against how good a swimmer it is, with its breath on top for one that swims
	/// under; its appetite for risk is its courage, scaled by how much it wants this target. In if the appetite covers
	/// the risk; otherwise it waits on the shore; and past its leash, it lets the target go.
	/// </summary>
	/// <remarks>Pure, so a designer's numbers can be checked without a scene (AISwimRiskTests).</remarks>
	public static class AISwimRisk
	{
		/// <summary>Metres of open water a perfect swimmer counts as no risk at all; a poor one, a fifth of it.</summary>
		public const float EasyWaterMetres = 200f;

		/// <summary>Metres a second divers rise and dive.</summary>
		public const float DiveSpeed = 1.5f;

		/// <summary>How much of its breath a diver will spend getting there: the rest is for the fight and the way back.</summary>
		public const float BreathBudget = 0.4f;

		public static WaterVerdict Evaluate(AISwimSettings swim, in AISwimRiskInput input)
		{
			if (input.WaterMetres <= 0f && input.TargetDepth <= 0f)
			{
				return WaterVerdict.Land;
			}
			if (input.LeashShare >= 1f)
			{
				return WaterVerdict.GiveUp;
			}
			if (swim == null || !swim.CanSwim)
			{
				return WaterVerdict.WaitAtShore;
			}
			// A surface swimmer cannot follow anything that has dived.
			if (!swim.HoldsBreath && input.TargetDepth > 1.5f)
			{
				return WaterVerdict.WaitAtShore;
			}

			float speed = swim.SwimSpeed(input.LandSpeed);
			float risk = input.WaterMetres / (EasyWaterMetres * Mathf.Lerp(0.2f, 1f, swim.SwimSkill));
			if (swim.HoldsBreath)
			{
				float seconds = input.WaterMetres / speed + 2f * input.TargetDepth / DiveSpeed;
				float breath = seconds / Mathf.Max(1f, swim.BreathSeconds * BreathBudget);
				if (breath >= 1f)
				{
					return WaterVerdict.WaitAtShore; // it would drown before it got there and back
				}
				risk += breath;
			}
			float appetite = swim.Courage * Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(input.TargetValue));
			// Already in: turning back costs as much as going on, so it holds to what it chose.
			if (input.AlreadySwimming)
			{
				appetite *= 1.5f;
			}
			return risk <= appetite ? WaterVerdict.Swim : WaterVerdict.WaitAtShore;
		}
	}
}
