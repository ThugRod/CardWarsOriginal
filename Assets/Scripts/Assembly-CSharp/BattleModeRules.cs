public static class BattleModeRules
{
	public const int TurboStartingMagic = 6;

	public const int TurboMagicPerRound = 3;

	public const int TurboMaxMagic = 20;

	public const int TurboMaxHandSize = 9;

	public const int TurboHeroHealth = 40;

	public const int TurboLeaderCooldown = 1;

	public static bool IsTurbo { get; private set; }

	public static bool IsSoloTurbo { get; private set; }

	public static int StartingMagic
	{
		get { return IsTurbo ? TurboStartingMagic : ParametersManager.Instance.Starting_Magic_Points; }
	}

	public static int MagicPerRound
	{
		get { return IsTurbo ? TurboMagicPerRound : 1; }
	}

	public static int MaxMagic
	{
		get { return IsTurbo ? TurboMaxMagic : ParametersManager.Instance.Max_Magic_Points; }
	}

	public static int MaxHandSize
	{
		get { return IsTurbo ? TurboMaxHandSize : GameState.MAX_HAND; }
	}

	public static void UseTurboMode(bool soloMatch)
	{
		IsTurbo = true;
		IsSoloTurbo = soloMatch;
	}

	public static void UseNormalMode()
	{
		IsTurbo = false;
		IsSoloTurbo = false;
	}

	public static int GetLeaderCooldown(int normalCooldown)
	{
		return IsTurbo ? TurboLeaderCooldown : normalCooldown;
	}
}
