using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>
	/// The inland water as what grows in it sees it (decision 35 (a)/(k), 2026-10-18): a small window round the camera of where a
	/// lake's or a river's surface stands and how its current runs, published for the aquatic plants (FishChannelFlow.hlsl), so a
	/// weed rooted in a river streams down ITS current and stays under ITS surface rather than being kept under the sea's.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A window, not the scene.</b> <see cref="ChannelCells"/>² cells of <see cref="ChannelCellMetres"/> (256 m), RGBA half: the
	/// current (m/s), the surface above the window's base height and whether the cell is over water. Read from the scene's
	/// <see cref="SceneWaterBodies"/> (its surface and its flow, the same the ribbons and the swimmers read) a few hundred cells a
	/// frame into a second buffer while the first is in use, uploaded whole when complete, again whenever the camera has moved
	/// <see cref="ChannelRecentreMetres"/> from the window's middle. Water never moves, so nothing else ever re-reads it.
	/// </para>
	/// <para>
	/// Past the window a plant rooted well above the sea is taken to be in a lake or a river and only drifts (the vegetation's
	/// own rule): the window is wider than the distance the detail plants are drawn to on every preset.
	/// </para>
	/// </remarks>
	public sealed partial class InlandWaterRenderer
	{
		/// <summary>Cells along each side of the window (FISH_CHANNEL_CELLS in FishChannelFlow.hlsl).</summary>
		public const int ChannelCells = 128;
		/// <summary>The window's cell, metres: a narrow stream is still a few cells across.</summary>
		public const float ChannelCellMetres = 2f;
		/// <summary>How far the camera may move from the window's middle before the next window is read, metres.</summary>
		public const float ChannelRecentreMetres = 40f;
		/// <summary>Cells read a frame: a window takes sixteen frames.</summary>
		private const int ChannelCellsPerFrame = 1024;

		private static readonly int ChannelFlowId = Shader.PropertyToID("_FishChannelFlow");
		private static readonly int ChannelFlowRectId = Shader.PropertyToID("_FishChannelFlowRect");

		private Texture2D channelTexture;
		private ushort[] channelReading;
		private int channelRead = -1;
		private bool channelReady;
		private Vector2 channelCorner, channelReadingCorner;
		private float channelReadingBase;
		private SceneWaterBodies channelBodies;

		/// <summary>Reads the next cells of the window being read, and starts one when the camera has moved off the last: once a frame, on the client.</summary>
		private void UpdateChannelFlow()
		{
#if !UNITY_SERVER
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			channelBodies ??= GetComponent<SceneWaterBodies>();
			if (channelBodies == null || channelBodies.Hydrology == null
				|| channelBodies.Hydrology.Rivers.Count == 0 && channelBodies.Hydrology.Lakes.Count == 0)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			float size = ChannelCells * ChannelCellMetres;
			if (channelRead < 0)
			{
				Vector2 middle = channelCorner + new Vector2(size * 0.5f, size * 0.5f);
				if (channelReady && (new Vector2(eye.x, eye.z) - middle).sqrMagnitude <= ChannelRecentreMetres * ChannelRecentreMetres)
				{
					return;
				}
				channelReadingCorner = ChannelCorner(eye);
				channelReadingBase = Mathf.Floor(eye.y);
				channelReading ??= new ushort[ChannelCells * ChannelCells * 4];
				channelRead = 0;
			}
			int end = Mathf.Min(ChannelCells * ChannelCells, channelRead + ChannelCellsPerFrame);
			ReadChannel(channelBodies, channelReadingCorner, channelReadingBase, channelRead, end, channelReading);
			channelRead = end;
			if (channelRead < ChannelCells * ChannelCells)
			{
				return;
			}
			channelRead = -1;
			if (channelTexture == null)
			{
				channelTexture = new Texture2D(ChannelCells, ChannelCells, TextureFormat.RGBAHalf, false, true)
				{
					name = "Channel Flow",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Point,
					hideFlags = HideFlags.DontSave,
				};
			}
			channelTexture.SetPixelData(channelReading, 0);
			channelTexture.Apply(false, false);
			channelCorner = channelReadingCorner;
			channelReady = true;
			Shader.SetGlobalTexture(ChannelFlowId, channelTexture);
			Shader.SetGlobalVector(ChannelFlowRectId, new Vector4(channelCorner.x, channelCorner.y, 1f / ChannelCellMetres, channelReadingBase));
#endif
		}

		/// <summary>The south-west corner of the window centred on a point, on the cells' own lattice (so a window's cells read the same water wherever it is moved).</summary>
		public static Vector2 ChannelCorner(Vector3 centre)
		{
			float half = ChannelCells * ChannelCellMetres * 0.5f;
			return new Vector2(Mathf.Floor((centre.x - half) / ChannelCellMetres) * ChannelCellMetres,
				Mathf.Floor((centre.z - half) / ChannelCellMetres) * ChannelCellMetres);
		}

		/// <summary>
		/// Reads cells <paramref name="from"/> … <paramref name="to"/> − 1 (row by row from the south-west) of a window into its
		/// halves, four to a cell: the current (x, z m/s), the surface above <paramref name="baseY"/>, 1 over water (all 0 off it).
		/// </summary>
		public static void ReadChannel(SceneWaterBodies water, Vector2 corner, float baseY, int from, int to, ushort[] halves)
		{
			ushort zero = Mathf.FloatToHalf(0f), one = Mathf.FloatToHalf(1f);
			for (int i = from; i < to; i++)
			{
				int cx = i % ChannelCells, cz = i / ChannelCells;
				float x = corner.x + (cx + 0.5f) * ChannelCellMetres;
				float z = corner.y + (cz + 0.5f) * ChannelCellMetres;
				int at = i * 4;
				if (water == null || !water.TryGetSurface(x, z, out float level))
				{
					halves[at] = halves[at + 1] = halves[at + 2] = halves[at + 3] = zero;
					continue;
				}
				Vector2 current = water.FlowAt(x, z, out _, out _);
				halves[at] = Mathf.FloatToHalf(current.x);
				halves[at + 1] = Mathf.FloatToHalf(current.y);
				halves[at + 2] = Mathf.FloatToHalf(level - baseY);
				halves[at + 3] = one;
			}
		}

		/// <summary>Takes the window down: the plants read no inland water until another renderer publishes one.</summary>
		private void ReleaseChannelFlow()
		{
#if !UNITY_SERVER
			if (channelReady)
			{
				Shader.SetGlobalVector(ChannelFlowRectId, Vector4.zero);
			}
			channelReady = false;
			channelRead = -1;
			if (channelTexture != null)
			{
				if (Application.isPlaying)
				{
					Destroy(channelTexture);
				}
				else
				{
					DestroyImmediate(channelTexture);
				}
				channelTexture = null;
			}
#endif
		}
	}
}
