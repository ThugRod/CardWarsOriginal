using System.Collections.Generic;

public static class CardWarsModSettings
{
	public static bool AllContentUnlocked
	{
		get { return CardWarsProfileManager.ActiveProfile.allContentUnlocked; }
	}

	public static bool InfiniteCoins
	{
		get { return CardWarsProfileManager.ActiveProfile.infiniteCoins; }
	}

	public const int InfiniteCoinBalance = 1000000000;

	public static bool InfiniteGems
	{
		get { return CardWarsProfileManager.ActiveProfile.infiniteGems; }
	}

	public const int InfiniteGemBalance = 1000000000;

	public static bool InfiniteStamina
	{
		get { return CardWarsProfileManager.ActiveProfile.infiniteStamina; }
	}

	public const int InfiniteStaminaBalance = 9999;
}

public static class CardWarsMod
{
	public static void Apply(PlayerInfoScript playerInfo)
	{
		if (playerInfo == null)
		{
			return;
		}

		if (CardWarsModSettings.AllContentUnlocked)
		{
			UnlockCards(playerInfo);
			UnlockLeaders();
			UnlockQuests(playerInfo);
			UnlockDungeons();
			DisableTutorialAndDungeonLocks();
		}

		EnsureInfiniteStamina(playerInfo);
		CardWarsProfileManager.ApplyActiveProfile(playerInfo);
	}

	public static void EnsureInfiniteStamina(PlayerInfoScript playerInfo)
	{
		if (playerInfo != null && CardWarsModSettings.InfiniteStamina)
		{
			playerInfo.Stamina_Max = CardWarsModSettings.InfiniteStaminaBalance;
			playerInfo.Stamina = CardWarsModSettings.InfiniteStaminaBalance;
		}
	}

	private static void UnlockCards(PlayerInfoScript playerInfo)
	{
		if (playerInfo.DeckManager != null)
		{
			playerInfo.DeckManager.UnlockAllCards();
		}
	}

	private static void UnlockLeaders()
	{
		LeaderManager manager = LeaderManager.Instance;
		foreach (KeyValuePair<string, LeaderForm> leader in manager.leaderForms)
		{
			if (!leader.Key.Contains("_Dumb_"))
			{
				manager.AddNewLeaderIfUnique(leader.Key);
			}
		}
	}

	private static void UnlockQuests(PlayerInfoScript playerInfo)
	{
		QuestManager manager = QuestManager.Instance;
		foreach (string questType in manager.GetQuestTypes())
		{
			foreach (QuestData quest in manager.GetQuestsByType(questType))
			{
				playerInfo.SetQuestProgress(quest, 3);
			}
		}
	}

	private static void UnlockDungeons()
	{
		foreach (DungeonData dungeon in DungeonDataManager.Instance.GetAllDungeons())
		{
			for (int i = 0; i < dungeon.Quests.Count; i++)
			{
				dungeon.UnlockQuest(i, true);
			}
		}
	}

	private static void DisableTutorialAndDungeonLocks()
	{
		GlobalFlags.Instance.stopTutorial = true;
		GlobalFlags.Instance.disableDungeonTimeLock = true;

		DebugFlagsScript debugFlags = DebugFlagsScript.GetInstance();
		if (debugFlags != null)
		{
			debugFlags.stopTutorial = true;
			debugFlags.disableDungeonTimeLock = true;
		}
	}
}
