using System.Collections.Generic;

public static class CardWarsModSettings
{
	public const bool AllContentUnlocked = true;

	public const bool InfiniteCoins = true;

	public const int InfiniteCoinBalance = 1000000000;
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

		if (CardWarsModSettings.InfiniteCoins)
		{
			playerInfo.Coins = CardWarsModSettings.InfiniteCoinBalance;
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
