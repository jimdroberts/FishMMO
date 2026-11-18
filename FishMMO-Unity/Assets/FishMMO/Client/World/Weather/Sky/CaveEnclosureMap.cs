using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The live caves' columns round the camera (<see cref="CaveEnclosure"/>, 2026-10-21), for the lit surfaces' underground
	/// darkness (FishGroundMap.hlsl FishCaveEnclosed): a window of <see cref="Resolution"/> columns either way on the world's
	/// column grid, gathered again when the camera has moved <see cref="Recentre"/> from its middle or a cave is enabled or
	/// disabled.
	/// </summary>
	/// <remarks>
	/// <c>_FishCaveEnclosure</c>: per column r its floor and g its roof (world y, the inner faces' lowest and highest), b its
	/// closure (0..1), a 1 where it holds cave rock. Read texel by texel (no sampler: the lit pass is near its sixteen).
	/// <c>_FishCaveEnclosureRect</c>: xy the window's first column, z its columns across, w 1 when any cave's columns are in
	/// it (otherwise no shader reads the texture). Two caves over one column take its widest span and greatest closure.
	/// </remarks>
	public sealed class CaveEnclosureMap
	{
		public const int Resolution = 320;
		/// <summary>How far the camera may move from the window's middle before it is gathered again, m.</summary>
		private const float Recentre = 100f;

		private static readonly int TextureId = Shader.PropertyToID("_FishCaveEnclosure");
		private static readonly int RectId = Shader.PropertyToID("_FishCaveEnclosureRect");

		private Texture2D texture;
		private float[] texels;
		private int cornerX, cornerZ;
		private int builtVersion = -1;
		private bool any;
		private bool built;

		/// <summary>Keeps the window round the camera and publishes it.</summary>
		public void Update(Vector3 camera)
		{
			float middleX = (cornerX + Resolution * 0.5f) * CaveEnclosure.Cell, middleZ = (cornerZ + Resolution * 0.5f) * CaveEnclosure.Cell;
			bool far = (new Vector2(camera.x - middleX, camera.z - middleZ)).sqrMagnitude > Recentre * Recentre;
			if (!built || far || builtVersion != CaveVisibility.LiveVersion)
			{
				Build(camera);
			}
			if (any)
			{
				Shader.SetGlobalTexture(TextureId, texture);
			}
			// Over a cloud world nothing is underground (FishUnderground).
			bool on = any && !SceneCloudWorld.Any;
			Shader.SetGlobalVector(RectId, new Vector4(cornerX, cornerZ, Resolution, on ? 1f : 0f));
		}

		private void Build(Vector3 camera)
		{
			built = true;
			builtVersion = CaveVisibility.LiveVersion;
			cornerX = CaveEnclosure.ColumnOf(camera.x) - Resolution / 2;
			cornerZ = CaveEnclosure.ColumnOf(camera.z) - Resolution / 2;
			texels ??= new float[Resolution * Resolution * 4];
			System.Array.Clear(texels, 0, texels.Length);
			any = false;
			var caves = CaveVisibility.Live;
			for (int c = 0; c < caves.Count; c++)
			{
				CaveEnclosure e = caves[c] != null ? caves[c].Enclosure : null;
				if (e == null)
				{
					continue;
				}
				int x0 = Mathf.Max(e.X0, cornerX), x1 = Mathf.Min(e.X0 + e.Width, cornerX + Resolution);
				int z0 = Mathf.Max(e.Z0, cornerZ), z1 = Mathf.Min(e.Z0 + e.Depth, cornerZ + Resolution);
				for (int z = z0; z < z1; z++)
				{
					for (int x = x0; x < x1; x++)
					{
						if (!e.TryColumn(x, z, out float floor, out float roof, out float shut))
						{
							continue;
						}
						int i = ((z - cornerZ) * Resolution + (x - cornerX)) * 4;
						if (texels[i + 3] > 0.5f)
						{
							floor = Mathf.Min(floor, texels[i]);
							roof = Mathf.Max(roof, texels[i + 1]);
							shut = Mathf.Max(shut, texels[i + 2]);
						}
						texels[i] = floor;
						texels[i + 1] = roof;
						texels[i + 2] = shut;
						texels[i + 3] = 1f;
						any = true;
					}
				}
			}
			if (!any)
			{
				return;
			}
			if (texture == null)
			{
				texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBAFloat, false, true)
				{
					name = "Cave Enclosure",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave,
				};
			}
			texture.SetPixelData(texels, 0);
			texture.Apply(false, false);
		}

		/// <summary>Lets the texture go (the sky system's teardown).</summary>
		public void Release()
		{
			if (texture != null)
			{
				Object.Destroy(texture);
				texture = null;
			}
			built = false;
		}
	}
}
