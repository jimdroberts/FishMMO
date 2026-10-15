using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// A cold sea's pack ice: a mosaic of floes laid at the concentration the climate gives, merged into chunks whose
	/// colliders the server keeps and players walk on, riding the tide. Open leads run through it so a boat can always
	/// cross. Cut with the scene (IcePlacer); nothing about it is random at runtime.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a surface and not pieces (2026-10-15).</b> Sea ice was placed as separate floating prefabs, capped at 35% cover
	/// and 600 pieces of at most 18 m: on a sea the climate froze solid, about one percent of it was ever white, and the
	/// globe drew a closed cap over a scene of open water. The pack is the ice at its real concentration; the loose
	/// pieces remain for the thin ice at its edge and the bergs.
	/// </para>
	/// <para>
	/// <b>It rides the tide, as a moored boat does</b> (<see cref="FloatingBoats"/>): each chunk's root — its collider — at
	/// the still sea level of the tick (<see cref="WaterQuery"/>), the same on the server and every client, re-placed only
	/// when the level has moved. The sea has no swell any peer shares, so the colliders do not heave; the water under the
	/// pack is calmed instead (the water's ice mask reads <see cref="Floes"/>), which is what pack ice does to a sea.
	/// </para>
	/// </remarks>
	public sealed class PackIce : MonoBehaviour
	{
		/// <summary>One floe, for what reads the pack's cover (the water's calm): its centre in the world's x/z and its radius, m.</summary>
		[Serializable]
		public struct Floe
		{
			public Vector2 Centre;
			public float Radius;
		}

		/// <summary>The floes, as cut.</summary>
		public Floe[] Floes = Array.Empty<Floe>();

		/// <summary>The chunk roots: each holds its chunk's collider, its mesh laid about the still water line, and its drawn ice under a client-only child.</summary>
		public Transform[] Chunks = Array.Empty<Transform>();

		/// <summary>The still sea level the pack was cut against, world y: where it lies when the scene has no tide to read.</summary>
		public float SeaLevelY;

		/// <summary>Where the pack lies, world x/z: the rectangle its floes cover.</summary>
		public Rect Area;

		/// <summary>The level the chunks were last placed at.</summary>
		private float placedLevel = float.NaN;

		private static readonly Dictionary<int, PackIce> byScene = new Dictionary<int, PackIce>();

		/// <summary>The pack in a scene, if it has one.</summary>
		public static bool TryGetForScene(Scene scene, out PackIce pack)
		{
			return byScene.TryGetValue(scene.handle, out pack) && pack != null;
		}

		/// <summary>Raised when a scene's pack comes or goes, for what draws from it.</summary>
		public static event Action<Scene> Changed;

		private void OnEnable()
		{
			byScene[gameObject.scene.handle] = this;
			placedLevel = float.NaN;
			Changed?.Invoke(gameObject.scene);
		}

		private void OnDisable()
		{
			if (byScene.TryGetValue(gameObject.scene.handle, out PackIce current) && current == this)
			{
				byScene.Remove(gameObject.scene.handle);
			}
			Changed?.Invoke(gameObject.scene);
		}

		private void LateUpdate()
		{
			Float(WeatherQuery.CurrentTick);
		}

		/// <summary>
		/// Every chunk to the sea's still level at a tick. A pure function of the tick: the same pose on the server and every
		/// client. Moved only when the level has: a static collider re-placed for nothing costs the physics a re-insert.
		/// </summary>
		public void Float(uint tick)
		{
			float level = LevelAt(tick);
			if (!float.IsNaN(placedLevel) && Mathf.Abs(level - placedLevel) < 0.002f)
			{
				return;
			}
			placedLevel = level;
			for (int i = 0; i < Chunks.Length; i++)
			{
				Transform chunk = Chunks[i];
				if (chunk == null)
				{
					continue;
				}
				Vector3 p = chunk.position;
				chunk.position = new Vector3(p.x, level, p.z);
			}
			// Automatic transform sync is off: without this the ice is walked on where it was last synced.
			Physics.SyncTransforms();
		}

		/// <summary>The sea's still level under the pack at a tick, world y.</summary>
		public float LevelAt(uint tick)
		{
			Vector2 c = Area.width > 0f ? Area.center : Vector2.zero;
			float surface = WaterQuery.Sample(gameObject.scene, c.x, c.y, tick).Surface;
			return float.IsNegativeInfinity(surface) || float.IsNaN(surface) ? SeaLevelY : surface;
		}
	}
}
