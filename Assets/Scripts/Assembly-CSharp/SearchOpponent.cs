using System.Collections;
using Multiplayer;
using UnityEngine;

public class SearchOpponent : AsyncData<MatchData>
{
	public RefreshMatch refreshMatch;

	public UILabel CostValue;

	public UIButtonTween SearchStatus;

	public UIButtonTween NotEnoughMoney;

	public UIButtonTween CouldNotFindMatch;

	public UIButtonTween ConnectionFailed;

	public UILabel NotEnoughMoneyDesc;

	public GameObject GoButton;

	private bool SearchTimerExpired;

	private int SearchFeesCoins;

	private int SearchFeesGems;

	private UILabel lanSearchLabel;

	private UILabel lanStatusLabel;

	private UILabel lanModeLabel;

	private PlayQuestButton lanPlayButton;

	private LanModeSelectorButton lanModeButton;

	private LanReadyButton lanReadyButton;

	private UILabel lanReadyLabel;

	private GameObject lanModeButtonObject;

	private bool lanMatchReady;

	private GameObject lanInfoPopup;

	private UILabel lanInfoLabel;

	private void Start()
	{
		if ((bool)CostValue)
		{
			CostValue.text = "IP";
			NGUITools.AddWidgetCollider(CostValue.gameObject);
			LanConnectionInfoOpener opener = CostValue.GetComponent<LanConnectionInfoOpener>();
			if (opener == null)
			{
				opener = CostValue.gameObject.AddComponent<LanConnectionInfoOpener>();
			}
			opener.owner = this;
		}
		UILabel[] labels = GetComponentsInChildren<UILabel>(true);
		for (int i = 0; i < labels.Length; i++)
		{
			if (labels[i] != CostValue && lanSearchLabel == null)
			{
				lanSearchLabel = labels[i];
			}
		}
		Transform root = base.transform.root;
		UILabel[] rootLabels = root.GetComponentsInChildren<UILabel>(true);
		for (int j = 0; j < rootLabels.Length; j++)
		{
			if (rootLabels[j].gameObject.name == "Label_Desc")
			{
				lanStatusLabel = rootLabels[j];
				break;
			}
		}
		if ((bool)GoButton)
		{
			GoButton.SetActive(true);
			lanPlayButton = GoButton.GetComponent<PlayQuestButton>();
			if (lanPlayButton != null)
			{
				lanPlayButton.enabled = false;
			}
			Vector3 buttonPosition = GoButton.transform.localPosition;
			lanModeButtonObject = Object.Instantiate(GoButton) as GameObject;
			lanModeButtonObject.name = "LanModeButton";
			lanModeButtonObject.transform.parent = GoButton.transform.parent;
			lanModeButtonObject.transform.localRotation = GoButton.transform.localRotation;
			lanModeButtonObject.transform.localScale = GoButton.transform.localScale;
			lanModeButtonObject.transform.localPosition = buttonPosition;
			GoButton.transform.localPosition = buttonPosition + Vector3.down * 270f;
			PrepareLanButtonArtwork(lanModeButtonObject);
			PrepareLanButtonArtwork(GoButton);

			PlayQuestButton modePlayButton = lanModeButtonObject.GetComponent<PlayQuestButton>();
			if (modePlayButton != null)
			{
				modePlayButton.enabled = false;
			}
			lanModeButton = lanModeButtonObject.GetComponent<LanModeSelectorButton>();
			if (lanModeButton == null)
			{
				lanModeButton = lanModeButtonObject.AddComponent<LanModeSelectorButton>();
			}
			lanModeButton.owner = this;
			lanModeLabel = FindVisibleButtonLabel(lanModeButtonObject);
			ConfigureLanButtonLabel(lanModeLabel);

			lanReadyButton = GoButton.GetComponent<LanReadyButton>();
			if (lanReadyButton == null)
			{
				lanReadyButton = GoButton.AddComponent<LanReadyButton>();
			}
			lanReadyButton.owner = this;
			lanReadyLabel = FindVisibleButtonLabel(GoButton);
			ConfigureLanButtonLabel(lanReadyLabel);
		}
		if (LanRealtimeManager.Instance != null)
		{
			LanRealtimeManager.Instance.ShowLobby(OnLanPeerReady, false);
		}
		SetupLanInfoPopup();
		UpdateLanUI();
	}

	private void OnClick()
	{
		LanRealtimeManager manager = LanRealtimeManager.Instance;
		if (manager != null)
		{
			manager.ShowLobby(OnLanPeerReady, false);
			if (manager.State == LanRealtimeManager.ConnectionState.Disconnected || manager.State == LanRealtimeManager.ConnectionState.Error)
			{
				manager.FindMatchAutomatically();
			}
			else if (manager.State == LanRealtimeManager.ConnectionState.Hosting || manager.State == LanRealtimeManager.ConnectionState.Connecting)
			{
				manager.Disconnect();
			}
			else if (manager.State == LanRealtimeManager.ConnectionState.Connected)
			{
				manager.Disconnect();
			}
			UICamera.UnlockInput();
		}
	}

	public void ToggleLanMode()
	{
		if (LanRealtimeManager.Instance != null)
		{
			LanRealtimeManager.Instance.ToggleTurboMode();
			UpdateLanUI();
		}
	}

	public void ToggleLanReady()
	{
		LanRealtimeManager manager = LanRealtimeManager.Instance;
		if (manager != null && !lanMatchReady && manager.State == LanRealtimeManager.ConnectionState.Connected && manager.PeerProfile != null)
		{
			manager.SetLocalReady(!manager.LocalReady);
			UpdateLanUI();
		}
	}

	public void ShowLanConnectionInfo()
	{
		if (lanInfoPopup == null || LanRealtimeManager.Instance == null)
		{
			return;
		}
		LanRealtimeManager manager = LanRealtimeManager.Instance;
		string role = manager.State == LanRealtimeManager.ConnectionState.Connected ? (manager.IsHost ? "ANFITRIÓN" : "INVITADO") : "SIN PARTIDA";
		string ping = manager.State == LanRealtimeManager.ConnectionState.Connected ? "\nPING: " + manager.PingMilliseconds + " ms" : string.Empty;
		if (lanInfoLabel != null)
		{
			lanInfoLabel.text = "CONEXIÓN LAN\n\nIP DE ESTE MÓVIL:\n" + manager.LocalIpList + "\nPUERTO: " + LanRealtimeManager.Port + "\nROL: " + role + ping + "\n\n" + manager.StatusText + "\n\nTOCA AQUÍ PARA CERRAR";
			lanInfoLabel.MakePixelPerfect();
		}
		lanInfoPopup.SetActive(true);
		UICamera.UnlockInput();
	}

	public void HideLanConnectionInfo()
	{
		if (lanInfoPopup != null)
		{
			lanInfoPopup.SetActive(false);
		}
		UICamera.UnlockInput();
	}

	private void OnLanPeerReady()
	{
		if (!LanRealtimeManager.IsConnected || LanRealtimeManager.Instance.PeerProfile == null)
		{
			return;
		}
		LanRealtimeManager.Instance.ApplyPeerProfileToLegacyState();
		MatchData lanMatch = LanRealtimeManager.Instance.CreateLegacyMatchData();
		if ((bool)refreshMatch && lanMatch != null)
		{
			refreshMatch.RefreshValues(lanMatch);
		}
		if ((bool)GoButton)
		{
			GoButton.SetActive(true);
		}
		if (lanModeButton != null)
		{
			lanModeButton.enabled = false;
		}
		lanMatchReady = true;
		if (lanReadyButton != null)
		{
			lanReadyButton.enabled = false;
		}
		// RefreshMatch starts the original automatic countdown and invokes the
		// PlayQuestButton itself when it reaches zero. Keep that component enabled,
		// but make the reused button a disabled-looking status indicator so players
		// cannot start one phone early by tapping it.
		if (lanPlayButton != null)
		{
			lanPlayButton.enabled = true;
		}
		if (lanReadyLabel != null)
		{
			SetLanButtonLabel(lanReadyLabel, "ESPERANDO...", 52);
		}
		SetLanButtonCollider(GoButton, false);
		SetLanButtonWaitingTint(GoButton);
		UICamera.UnlockInput();
	}

	private IEnumerator SearchTimer()
	{
		SearchTimerExpired = false;
		yield return new WaitForSeconds(2f);
		SearchTimerExpired = true;
		yield return null;
	}

	public void MatchDataCallback(MatchData data, ResponseFlag flag)
	{
		Asyncdata.Set(flag, data);
	}

	private void Update()
	{
		UpdateLanUI();
		if (Asyncdata.processed || !SearchTimerExpired)
		{
			return;
		}
		Asyncdata.processed = true;
		SearchTimerExpired = false;
		if ((bool)SearchStatus)
		{
			SearchStatus.Play(false);
		}
		if (Asyncdata.MP_Data != null)
		{
			if ((bool)refreshMatch)
			{
				Asyncdata.MP_Data.opponentLeader = ValidateOpponentLeader(Asyncdata.MP_Data.opponentLeader);
				refreshMatch.RefreshValues(Asyncdata.MP_Data);
			}
			if ((bool)GoButton)
			{
				GoButton.SetActive(true);
			}
			UICamera.UnlockInput();
			PlayerInfoScript instance = PlayerInfoScript.GetInstance();
			if ((bool)CostValue && (bool)instance)
			{
				instance.Coins -= SearchFeesCoins;
				instance.Save();
				Singleton<AnalyticsManager>.Instance.LogDeckFindWarOpponentPurchase(0, SearchFeesCoins);
			}
		}
		else
		{
			CouldNotFindMatch.Play(true);
			ConnectionFailed.Play(true);
			SearchTimerExpired = false;
		}
	}

	private void UpdateLanUI()
	{
		LanRealtimeManager manager = LanRealtimeManager.Instance;
		if (manager == null)
		{
			return;
		}
		if (lanStatusLabel != null)
		{
			lanStatusLabel.text = manager.StatusText + ((manager.State == LanRealtimeManager.ConnectionState.Connected) ? "   Ping " + manager.PingMilliseconds + " ms" : string.Empty);
		}
		if (lanSearchLabel != null)
		{
			if (manager.State == LanRealtimeManager.ConnectionState.Hosting || manager.State == LanRealtimeManager.ConnectionState.Connecting)
			{
				lanSearchLabel.text = "CANCELAR BÚSQUEDA";
			}
			else if (manager.State == LanRealtimeManager.ConnectionState.Connected && manager.PeerProfile != null)
			{
				lanSearchLabel.text = "DESCONECTAR";
			}
			else
			{
				lanSearchLabel.text = "BUSCAR PARTIDA LAN";
			}
		}
		if (CostValue != null)
		{
			CostValue.text = "IP";
		}
		if (lanModeLabel != null)
		{
			SetLanButtonLabel(lanModeLabel, manager.TurboModeSelected ? "MODO: GUERRA TURBO" : "MODO: NORMAL", manager.TurboModeSelected ? 42 : 52);
		}
		if (!lanMatchReady && lanReadyLabel != null)
		{
			if (manager.State == LanRealtimeManager.ConnectionState.Connected && manager.PeerProfile != null)
			{
				SetLanButtonLabel(lanReadyLabel, manager.LocalReady ? "CANCELAR LISTO" : "LISTO", manager.LocalReady ? 48 : 62);
			}
			else if (manager.State == LanRealtimeManager.ConnectionState.Hosting || manager.State == LanRealtimeManager.ConnectionState.Connecting)
			{
				SetLanButtonLabel(lanReadyLabel, "LISTO\nESPERANDO RIVAL", 40);
			}
			else
			{
				SetLanButtonLabel(lanReadyLabel, "LISTO\nSIN CONEXIÓN", 44);
			}
		}
		if (lanReadyButton != null && !lanMatchReady)
		{
			lanReadyButton.enabled = manager.State == LanRealtimeManager.ConnectionState.Connected && manager.PeerProfile != null;
		}
		if (lanPlayButton != null && !lanMatchReady)
		{
			lanPlayButton.enabled = false;
		}
		if (lanModeButton != null)
		{
			lanModeButton.enabled = manager.State != LanRealtimeManager.ConnectionState.Connected && manager.State != LanRealtimeManager.ConnectionState.Connecting;
		}
	}

	private void SetupLanInfoPopup()
	{
		Transform[] transforms = base.transform.root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
		{
			if (transforms[i].gameObject.name == "QuestReloadGame")
			{
				lanInfoPopup = transforms[i].gameObject;
				break;
			}
		}
		if (lanInfoPopup == null)
		{
			return;
		}
		ReloadButton[] reloadButtons = lanInfoPopup.GetComponentsInChildren<ReloadButton>(true);
		for (int j = 0; j < reloadButtons.Length; j++)
		{
			reloadButtons[j].enabled = false;
			LanConnectionInfoCloser popupCloser = reloadButtons[j].gameObject.GetComponent<LanConnectionInfoCloser>();
			if (popupCloser == null)
			{
				popupCloser = reloadButtons[j].gameObject.AddComponent<LanConnectionInfoCloser>();
			}
			popupCloser.owner = this;
			Object.Destroy(reloadButtons[j]);
		}
		UILabel[] labels = lanInfoPopup.GetComponentsInChildren<UILabel>(true);
		for (int j = 0; j < labels.Length; j++)
		{
			if (labels[j].gameObject.name == "Reload_Label")
			{
				lanInfoLabel = labels[j];
				lanInfoLabel.transform.localPosition = new Vector3(0f, 0f, lanInfoLabel.transform.localPosition.z);
				lanInfoLabel.shrinkToFit = true;
				lanInfoLabel.lineWidth = 780;
				lanInfoLabel.lineHeight = 480;
				lanInfoLabel.maxLineCount = 0;
				lanInfoLabel.maxFontSize = 36;
				break;
			}
		}
		Transform[] popupTransforms = lanInfoPopup.GetComponentsInChildren<Transform>(true);
		for (int k = 0; k < popupTransforms.Length; k++)
		{
			if (popupTransforms[k].gameObject.name == "Reload_BG")
			{
				Vector3 scale = popupTransforms[k].localScale;
				scale.y = 520f;
				popupTransforms[k].localScale = scale;
				NGUITools.AddWidgetCollider(popupTransforms[k].gameObject);
				LanConnectionInfoCloser closer = popupTransforms[k].GetComponent<LanConnectionInfoCloser>();
				if (closer == null)
				{
					closer = popupTransforms[k].gameObject.AddComponent<LanConnectionInfoCloser>();
				}
				closer.owner = this;
				break;
			}
		}
		lanInfoPopup.SetActive(false);
	}

	private static UILabel FindVisibleButtonLabel(GameObject button)
	{
		UILabel[] labels = button.GetComponentsInChildren<UILabel>(true);
		for (int i = 0; i < labels.Length; i++)
		{
			if (labels[i].gameObject.name == "Battle")
			{
				return labels[i];
			}
		}
		for (int i = 0; i < labels.Length; i++)
		{
			if (labels[i].gameObject.activeInHierarchy && labels[i].gameObject.name != "Stamina" && labels[i].gameObject.name != "StaminaLabel")
			{
				return labels[i];
			}
		}
		return labels.Length > 0 ? labels[0] : null;
	}

	private static void PrepareLanButtonArtwork(GameObject button)
	{
		Transform[] transforms = button.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
		{
			if (transforms[i].gameObject.name == "Stamina" || transforms[i].gameObject.name == "StaminaLabel")
			{
				transforms[i].gameObject.SetActive(false);
			}
		}
		UISprite[] sprites = button.GetComponentsInChildren<UISprite>(true);
		for (int i = 0; i < sprites.Length; i++)
		{
			if (sprites[i].spriteName == "PlayButton")
			{
				Vector3 scale = sprites[i].transform.localScale;
				sprites[i].spriteName = "uiButtonGreen";
				sprites[i].transform.localScale = scale;
			}
		}
	}

	private static void SetLanButtonCollider(GameObject button, bool enabled)
	{
		if (button == null)
		{
			return;
		}
		Collider[] colliders = button.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			colliders[i].enabled = enabled;
		}
	}

	private static void SetLanButtonWaitingTint(GameObject button)
	{
		if (button == null)
		{
			return;
		}
		UISprite[] sprites = button.GetComponentsInChildren<UISprite>(true);
		Color waitingColor = new Color(0.45f, 0.45f, 0.45f, 1f);
		for (int i = 0; i < sprites.Length; i++)
		{
			sprites[i].color = waitingColor;
		}
	}

	private static void ConfigureLanButtonLabel(UILabel label)
	{
		if (label == null)
		{
			return;
		}
		Vector3 position = label.transform.localPosition;
		position.x = 0f;
		position.y = 0f;
		label.transform.localPosition = position;
		label.shrinkToFit = true;
		label.lineWidth = 700;
		label.lineHeight = 180;
		label.maxLineCount = 2;
	}

	private static void SetLanButtonLabel(UILabel label, string text, int maxFontSize)
	{
		if (label == null)
		{
			return;
		}
		label.maxFontSize = maxFontSize;
		label.text = text;
		label.MakePixelPerfect();
	}

	private string ValidateOpponentLeader(string leader)
	{
		if (LeaderManager.Instance.IsLeaderFromFC(leader))
		{
			return "Leader_Finn";
		}
		return leader;
	}
}

public class LanModeSelectorButton : MonoBehaviour
{
	public SearchOpponent owner;

	private void OnClick()
	{
		if (enabled && owner != null)
		{
			owner.ToggleLanMode();
		}
	}
}

public class LanReadyButton : MonoBehaviour
{
	public SearchOpponent owner;

	private void OnClick()
	{
		if (enabled && owner != null)
		{
			owner.ToggleLanReady();
		}
	}
}

public class LanConnectionInfoOpener : MonoBehaviour
{
	public SearchOpponent owner;

	private void OnClick()
	{
		if (enabled && owner != null)
		{
			owner.ShowLanConnectionInfo();
		}
	}
}

public class LanConnectionInfoCloser : MonoBehaviour
{
	public SearchOpponent owner;

	private void OnClick()
	{
		if (enabled && owner != null)
		{
			owner.HideLanConnectionInfo();
		}
	}
}
