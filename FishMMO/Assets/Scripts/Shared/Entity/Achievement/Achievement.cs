public class Achievement
{
	public int templateID;
	public uint CurrentValue;

	private AchievementTemplate cachedTemplate;
	public AchievementTemplate Template { get { return cachedTemplate; } }

	public Achievement(int templateID, uint Value)
	{
		this.templateID = templateID;
		this.cachedTemplate = AchievementTemplate.Get<AchievementTemplate>(templateID);
		this.CurrentValue = Value;
	}
}