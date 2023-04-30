public class Achievement
{
	public int templateID;
	public uint currentValue;

	public AchievementTemplate Template { get { return AchievementTemplate.Cache[templateID]; } }

	public Achievement(int templateID, uint Value)
	{
		this.templateID = templateID;
		this.currentValue = Value;
	}
}