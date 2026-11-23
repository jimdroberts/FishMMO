using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a client draws for the Gate spell (issue #241): each open portal, and each lit Gate Stone, as a glowing arch of the
	/// scene portals' surface (<c>FishMMO/Portal</c>, glow only: a gate's far side is not baked) and a light.
	/// </summary>
	/// <remarks>
	/// <para>Built from code, so the module ships no prefab: the arch is a quad the shader cuts to shape, the glow a point light.
	/// Without the shader loaded (a scene with no portal of its own) only the light shows.</para>
	/// <para>Each object is put in the local character's scene, so a scene change takes it away, and is destroyed when its
	/// time runs out even if the server's close never arrives.</para>
	/// </remarks>
	public static class GateVisuals
	{
		/// <summary>The scene portals' shader, glow only without a baked view.</summary>
		public const string ShaderName = "FishMMO/Portal";

		private static readonly Color Glow = new Color(0.62f, 0.52f, 1.0f, 1.0f);
		private static readonly int GlowColourId = Shader.PropertyToID("_GlowColour");

		private static readonly Dictionary<long, GameObject> portals = new Dictionary<long, GameObject>();
		private static readonly Dictionary<int, GameObject> stones = new Dictionary<int, GameObject>();
		private static Material material;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			portals.Clear();
			stones.Clear();
			material = null;
		}

		/// <summary>Shows (<paramref name="seconds"/> above 0) or removes the portal of gate <paramref name="gateID"/>.</summary>
		public static void SetPortal(ICharacter local, long gateID, Vector3 position, float yaw, float radius, float seconds)
		{
			Remove(portals, gateID);
			if (seconds <= 0.0f)
			{
				return;
			}
			float width = Mathf.Clamp(radius * 2.0f, 1.2f, 4.0f);
			portals[gateID] = Build(local, "Gate Portal", position, yaw, width, width * 1.5f, 5.0f, seconds);
		}

		/// <summary>Lights (<paramref name="seconds"/> above 0) or darkens the Gate Stone at <paramref name="waypointIndex"/>.</summary>
		public static void SetStone(ICharacter local, int waypointIndex, Vector3 position, float yaw, float seconds)
		{
			Remove(stones, waypointIndex);
			if (seconds <= 0.0f)
			{
				return;
			}
			stones[waypointIndex] = Build(local, "Lit Gate Stone", position, yaw, 1.4f, 2.2f, 7.0f, seconds);
		}

		/// <summary>Removes everything (the local character left).</summary>
		public static void Clear()
		{
			foreach (GameObject go in portals.Values)
			{
				Destroy(go);
			}
			foreach (GameObject go in stones.Values)
			{
				Destroy(go);
			}
			portals.Clear();
			stones.Clear();
		}

		private static void Remove<TKey>(Dictionary<TKey, GameObject> map, TKey key)
		{
			if (map.TryGetValue(key, out GameObject go))
			{
				Destroy(go);
				map.Remove(key);
			}
		}

		private static void Destroy(GameObject go)
		{
			if (go != null)
			{
				Object.Destroy(go);
			}
		}

		private static GameObject Build(ICharacter local, string name, Vector3 position, float yaw, float width, float height,
			float lightRange, float seconds)
		{
			var root = new GameObject(name);
			if (local?.GameObject != null && local.GameObject.scene.IsValid())
			{
				SceneManager.MoveGameObjectToScene(root, local.GameObject.scene);
			}
			root.transform.SetPositionAndRotation(position, Quaternion.Euler(0.0f, yaw, 0.0f));

			var glow = new GameObject("Glow");
			glow.transform.SetParent(root.transform, false);
			glow.transform.localPosition = new Vector3(0.0f, height * 0.5f, 0.0f);
			Light light = glow.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = Glow;
			light.range = lightRange;
			light.intensity = 2.5f;
			light.shadows = LightShadows.None;

			Material surface = SurfaceMaterial();
			if (surface != null)
			{
				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
				quad.name = "Arch";
				Collider collider = quad.GetComponent<Collider>();
				if (collider != null)
				{
					Object.Destroy(collider);
				}
				quad.transform.SetParent(root.transform, false);
				quad.transform.localPosition = new Vector3(0.0f, height * 0.5f, 0.0f);
				quad.transform.localScale = new Vector3(width, height, 1.0f);
				MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = surface;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.receiveShadows = false;
			}

			Object.Destroy(root, seconds + 1.0f);
			return root;
		}

		private static Material SurfaceMaterial()
		{
			if (material != null)
			{
				return material;
			}
			Shader shader = ShaderLookup.Find(ShaderName);
			if (shader == null)
			{
				return null;
			}
			material = new Material(shader) { name = "Gate Portal (runtime)" };
			material.SetColor(GlowColourId, Glow);
			return material;
		}
	}
}
