using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TurboModeManager : MonoBehaviour
{
	public static TurboModeManager Instance { get; private set; }

	private bool launchingSoloMatch;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null)
		{
			return;
		}
		GameObject gameObject = new GameObject("TurboModeManager");
		gameObject.hideFlags = HideFlags.DontSave;
		DontDestroyOnLoad(gameObject);
		gameObject.AddComponent<TurboModeManager>();
	}

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(base.gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(base.gameObject);
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			Instance = null;
		}
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != "AdventureTime")
		{
			return;
		}
		launchingSoloMatch = false;
		BattleModeRules.UseNormalMode();
		StartCoroutine(ConfigureTurboButton());
	}

	private IEnumerator ConfigureTurboButton()
	{
		GameObject turboButton = null;
		GameObject multiplayerButton = null;
		for (int i = 0; i < 120 && (turboButton == null || multiplayerButton == null); i++)
		{
			turboButton = FindSceneObject("QuestMenu_LeaderBoards");
			multiplayerButton = FindSceneObject("QuestMenu_Multiplayer");
			if (turboButton == null || multiplayerButton == null)
			{
				yield return null;
			}
		}
		if (turboButton == null)
		{
			Debug.LogWarning("No se encontró el botón de Marcadores para configurar Guerra Turbo.");
			yield break;
		}

		turboButton.name = "QuestMenu_GuerraTurbo";
		UIButtonMessage[] messages = turboButton.GetComponents<UIButtonMessage>();
		for (int j = 0; j < messages.Length; j++)
		{
			messages[j].enabled = false;
		}
		if (turboButton.GetComponent<TurboModeMenuButton>() == null)
		{
			turboButton.AddComponent<TurboModeMenuButton>();
		}

		Transform trophy = FindChild(turboButton.transform, "Trophy");
		if (trophy != null)
		{
			trophy.gameObject.SetActive(false);
		}
		Transform existingIcon = FindChild(turboButton.transform, "GuerraTurboIcon");
		Transform deckWarsIcon = multiplayerButton == null ? null : FindChild(multiplayerButton.transform, "MultiPlayer");
		if (existingIcon == null && deckWarsIcon != null)
		{
			GameObject icon = Instantiate(deckWarsIcon.gameObject);
			icon.name = "GuerraTurboIcon";
			icon.transform.SetParent(turboButton.transform, false);
			icon.transform.localPosition = deckWarsIcon.localPosition;
			icon.transform.localRotation = deckWarsIcon.localRotation;
			icon.transform.localScale = deckWarsIcon.localScale;
			Collider[] colliders = icon.GetComponentsInChildren<Collider>(true);
			for (int k = 0; k < colliders.Length; k++)
			{
				colliders[k].enabled = false;
			}
		}

		Text label = turboButton.GetComponentInChildren<Text>(true);
		if (label != null)
		{
			UGuiLocalizedText localizedText = label.GetComponent<UGuiLocalizedText>();
			if (localizedText != null)
			{
				localizedText.enabled = false;
			}
			label.text = "Guerra Turbo";
		}
	}

	public void StartSoloTurboMatch()
	{
		if (!launchingSoloMatch)
		{
			StartCoroutine(LaunchSoloTurboMatch());
		}
	}

	private IEnumerator LaunchSoloTurboMatch()
	{
		launchingSoloMatch = true;
		PlayerInfoScript playerInfo = PlayerInfoScript.GetInstance();
		if (playerInfo == null || QuestManager.Instance == null || AIDeckManager.Instance == null)
		{
			Debug.LogError("Guerra Turbo todavía no puede iniciar: los datos del jugador no terminaron de cargar.");
			launchingSoloMatch = false;
			yield break;
		}
		Deck playerDeck = playerInfo.GetSelectedDeckCopy();
		QuestData quest = playerInfo.GetCurrentQuest();
		if (playerDeck == null || playerDeck.Leader == null || playerDeck.CardCount() == 0 || quest == null)
		{
			Debug.LogError("Guerra Turbo necesita un mazo seleccionado y una misión disponible.");
			launchingSoloMatch = false;
			yield break;
		}

		BattleModeRules.UseTurboMode(true);
		GlobalFlags.Instance.InMPMode = false;
		GlobalFlags.Instance.BattleResult = null;
		GameState.Instance.ResetFromQuestData(quest, playerDeck);
		GameState.Instance.BattleResolver = null;
		yield return Resources.UnloadUnusedAssets();
		SLOTGameSingleton<SLOTSceneManager>.GetInstance().LoadLevel("LoadingScreen");
	}

	private static GameObject FindSceneObject(string objectName)
	{
		GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
		for (int i = 0; i < objects.Length; i++)
		{
			GameObject candidate = objects[i];
			if (candidate.name == objectName && candidate.scene.IsValid())
			{
				return candidate;
			}
		}
		return null;
	}

	private static Transform FindChild(Transform parent, string childName)
	{
		Transform[] children = parent.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i].name == childName)
			{
				return children[i];
			}
		}
		return null;
	}
}

public class TurboModeMenuButton : MonoBehaviour
{
	private void OnClick()
	{
		if (TurboModeManager.Instance != null)
		{
			TurboModeManager.Instance.StartSoloTurboMatch();
		}
	}
}
