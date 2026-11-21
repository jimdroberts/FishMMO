using System.Collections;
using System.Text;
using FishMMO.Shared;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// The shipped Punch and Lesser Fireball, fired on the combat sim's real server at a strafing victim: a hit aimed at
	/// a lagged client's view lands only BECAUSE of the rewind (the same shot with no claim misses), and the same
	/// attack aimed at the live body lands with no claim. See <see cref="CombatSimBootstrap.RunLagCompProbe"/>.
	/// </summary>
	public class LagCompProbePlayModeTests
	{
		public const string PunchPath = "Assets/FishMMO/Modules/Abilities/Templates/Types/Punch.asset";
		public const string ProjectilePath = "Assets/FishMMO/Modules/Abilities/Templates/Types/Lesser Fireball.asset";
		public const int ClaimMs = 350;

		private GameObject host;

		[UnityTearDown]
		public IEnumerator TearDownHost()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
			yield return null;
		}

		public static ushort FreeUdpPort()
		{
			using (System.Net.Sockets.UdpClient probe = new System.Net.Sockets.UdpClient(0))
			{
				return (ushort)((System.Net.IPEndPoint)probe.Client.LocalEndPoint).Port;
			}
		}

		public static AbilityTemplate Load(string path)
		{
#if UNITY_EDITOR
			return UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityTemplate>(path);
#else
			return null;
#endif
		}

		public static string Describe(LagCompProbeResult result)
		{
			StringBuilder sb = new StringBuilder();
			foreach (LagCompShot shot in result.Shots)
			{
				sb.AppendLine($"{shot.Ability} | {shot.Mode} | rewind {shot.RewindTicks:F2} ticks | live z {shot.LiveOffset:F2} " +
					$"rewound z {shot.RewoundOffset:F2} | {(shot.Hit ? "HIT" : "miss")} (expected {(shot.ExpectHit ? "hit" : "miss")}) {shot.Note}");
			}
			return sb.ToString();
		}

		public static void AssertProbe(LagCompProbeResult result)
		{
			Assert.IsNull(result.Error, "the probe could not run: " + result.Error);
			Assert.AreEqual(6, result.Shots.Count, "three shots each for Punch and the projectile\n" + Describe(result));
			foreach (LagCompShot shot in result.Shots)
			{
				Assert.IsNull(shot.Note, $"{shot.Ability} ({shot.Mode}): {shot.Note}\n" + Describe(result));
				if (!shot.Hit)
				{
					// A miss must be a sweep that ran and found nothing. (A Punch that hits ends inside its own input, so
					// its record is gone by the next read; the hit itself is the evidence there.)
					Assert.Greater(shot.SweepsRecorded, 0, $"{shot.Ability} ({shot.Mode}) recorded no server sweep\n" + Describe(result));
				}
				Assert.AreEqual(shot.ExpectHit, shot.Hit, $"{shot.Ability} ({shot.Mode})\n" + Describe(result));
			}
		}

		[UnityTest]
		public IEnumerator PunchAndProjectile_HitOnlyThroughTheRewind()
		{
			host = new GameObject("LagCompProbeUnderTest");
			CombatSimBootstrap sim = host.AddComponent<CombatSimBootstrap>();
			sim.AutoSweep = false;
			sim.ClaimMs = 0;
			sim.TeamSize = 1;
			sim.Port = FreeUdpPort();
			sim.DirectorPaused = true;

			float readyDeadline = Time.realtimeSinceStartup + 180f;
			while (!sim.Ready && Time.realtimeSinceStartup < readyDeadline)
			{
				yield return null;
			}
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

			LagCompProbeResult result = new LagCompProbeResult();
			yield return sim.RunLagCompProbe(result, Load(PunchPath), Load(ProjectilePath), ClaimMs);
			Debug.Log("[LagCompProbe]\n" + Describe(result));
			AssertProbe(result);
		}
	}
}
