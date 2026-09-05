namespace FishMMO.Database.Data
{
	/// <summary>
	/// Plot structure data transfer object.
	/// </summary>
	public struct PlotStructureData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>The plot this structure stands on.</summary>
		public readonly long PlotID;
		/// <summary>Which structure was built.</summary>
		public readonly int TemplateID;
		/// <summary>Offset east of the plot's origin, in metres.</summary>
		public readonly float LocalX;
		/// <summary>Offset above the plot's origin, in metres.</summary>
		public readonly float LocalY;
		/// <summary>Offset north of the plot's origin, in metres.</summary>
		public readonly float LocalZ;
		/// <summary>Rotation about the vertical axis, in degrees.</summary>
		public readonly float Yaw;

		public PlotStructureData(long id, long plotID, int templateID, float localX, float localY, float localZ, float yaw)
		{
			ID = id;
			PlotID = plotID;
			TemplateID = templateID;
			LocalX = localX;
			LocalY = localY;
			LocalZ = localZ;
			Yaw = yaw;
		}
	}
}
