using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The bands of sky a world's clouds live in: how high its deck sits, how deep it is, what stands
	/// above it. A body's own, where the default sky's would be wrong for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the PLANETARY part of how weather is drawn, and only that. The weather itself is the
	/// driver's and already comes from the body — its air, its tilt, its distance from its suns. How
	/// that weather is rendered is the client's Weather Render Profile: materials, shaders, sound, and
	/// the budgets each quality level gets. Almost none of that is a fact about a planet, a server has
	/// no business loading any of it, and a copy per world would be a copy per world to keep in step.
	/// What IS a fact about a planet is where its clouds stand: a world with thick air has a deep,
	/// high stack, a thin-aired one a few high wisps and nothing else. A band is plain data and
	/// already lives on the shared side, so a body can own its stack without owning a material.
	/// </para>
	/// <para>
	/// Left empty on a body, the Weather Render Profile's own stack is used, which is the home
	/// world's sky.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "Cloud Stack", menuName = "FishMMO/World/Cloud Stack", order = 18)]
	public class CloudStackProfile : CachedScriptableObject<CloudStackProfile>, ICachedObject
	{
		[Tooltip("The bands of sky this world's clouds live in, lowest first: the weather band at the ground, the cloud deck above it, and whatever is stacked over that.")]
		public List<CloudLayer> Layers = CloudLayerDefaults.Sky();

		/// <summary>True when there is something here to draw with.</summary>
		public bool HasLayers => Layers != null && Layers.Count > 0;
	}
}
