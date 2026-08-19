using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class CardWarsProfile
{
	public string id;
	public string displayName;
	public bool builtIn;
	public bool allContentUnlocked;
	public bool infiniteCoins;
	public bool infiniteGems;
	public bool infiniteStamina;
	public int minimumCoins;
	public int minimumGems;
	public long createdUtcTicks;
	public long lastPlayedUtcTicks;
}

[Serializable]
internal class CardWarsProfileCollection
{
	public List<CardWarsProfile> profiles = new List<CardWarsProfile>();
}

public class CardWarsProfileManager : MonoBehaviour
{
	private const string ProfilesKey = "CardWars.LocalProfiles.V1";
	private const string ActiveProfileKey = "CardWars.ActiveProfile.V1";
	private const string UnlimitedProfileId = "unlimited";
	private const string StoryProfileId = "story";
	private const string LegacyGameFile = "game.json";
	private static CardWarsProfileCollection data;
	private static CardWarsProfileManager instance;

	private bool menuVisible;
	private bool createMode;
	private int selectedIndex;
	private string newProfileName = "Nuevo aventurero";
	private bool newUnlockAll;
	private bool newInfiniteCoins;
	private bool newInfiniteGems;
	private bool newInfiniteStamina = true;
	private int newMinimumCoins;
	private int newMinimumGems;
	private Vector2 profileScroll;
	private Vector2 detailScroll;
	private string resetConfirmationId;
	private GUIStyle titleStyle;
	private GUIStyle headingStyle;
	private GUIStyle labelStyle;
	private GUIStyle smallStyle;
	private GUIStyle buttonStyle;
	private GUIStyle selectedButtonStyle;
	private GUIStyle panelStyle;
	private GUIStyle toggleStyle;
	private Texture2D panelTexture;
	private Texture2D goldTexture;
	private Texture2D selectedTexture;
	private int styledWidth;
	private int styledHeight;

	public static CardWarsProfile ActiveProfile
	{
		get
		{
			EnsureProfiles();
			string activeId = PlayerPrefs.GetString(ActiveProfileKey, UnlimitedProfileId);
			CardWarsProfile profile = FindProfile(activeId);
			return profile ?? data.profiles[0];
		}
	}

	public static bool IsMenuVisible
	{
		get { return instance != null && instance.menuVisible; }
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		EnsureProfiles();
		if (instance != null)
		{
			return;
		}
		GameObject gameObject = new GameObject("CardWarsProfileManager");
		gameObject.hideFlags = HideFlags.DontSave;
		DontDestroyOnLoad(gameObject);
		instance = gameObject.AddComponent<CardWarsProfileManager>();
	}

	private void Awake()
	{
		if (instance != null && instance != this)
		{
			Destroy(base.gameObject);
			return;
		}
		instance = this;
		DontDestroyOnLoad(base.gameObject);
		EnsureProfiles();
		selectedIndex = Mathf.Max(0, FindProfileIndex(ActiveProfile.id));
		menuVisible = true;
	}

	public static string GetActiveGameFileName()
	{
		CardWarsProfile profile = ActiveProfile;
		if (profile.id == UnlimitedProfileId)
		{
			// Keep using the old save for the unlimited profile so existing progress is preserved.
			return LegacyGameFile;
		}
		return "game_profile_" + SanitizeId(profile.id) + ".json";
	}

	public static void ShowProfiles()
	{
		if (instance != null)
		{
			instance.selectedIndex = Mathf.Max(0, FindProfileIndex(ActiveProfile.id));
			instance.createMode = false;
			instance.menuVisible = true;
		}
	}

	public static void ApplyActiveProfile(PlayerInfoScript playerInfo)
	{
		if (playerInfo == null)
		{
			return;
		}
		CardWarsProfile profile = ActiveProfile;
		if (profile.minimumCoins > 0 && playerInfo.Coins < profile.minimumCoins)
		{
			playerInfo.Coins = profile.minimumCoins;
		}
		if (profile.minimumGems > 0 && playerInfo.Gems < profile.minimumGems)
		{
			playerInfo.Gems = profile.minimumGems;
		}
	}

	private static void EnsureProfiles()
	{
		if (data != null)
		{
			return;
		}
		string json = PlayerPrefs.GetString(ProfilesKey, string.Empty);
		if (!string.IsNullOrEmpty(json))
		{
			try
			{
				data = JsonUtility.FromJson<CardWarsProfileCollection>(json);
			}
			catch
			{
				data = null;
			}
		}
		if (data == null)
		{
			data = new CardWarsProfileCollection();
		}
		if (data.profiles == null)
		{
			data.profiles = new List<CardWarsProfile>();
		}
		if (FindProfile(UnlimitedProfileId) == null)
		{
			data.profiles.Insert(0, CreateProfile(UnlimitedProfileId, "Todo ilimitado", true, true, true, true, true, 1000000000, 1000000000));
		}
		if (FindProfile(StoryProfileId) == null)
		{
			data.profiles.Add(CreateProfile(StoryProfileId, "Historia desde cero", true, false, false, false, false, 0, 0));
		}
		if (!PlayerPrefs.HasKey(ActiveProfileKey) || FindProfile(PlayerPrefs.GetString(ActiveProfileKey)) == null)
		{
			PlayerPrefs.SetString(ActiveProfileKey, UnlimitedProfileId);
		}
		SaveProfiles();
	}

	private static CardWarsProfile CreateProfile(string id, string name, bool builtIn, bool unlockAll, bool coins, bool gems, bool stamina, int minimumCoins, int minimumGems)
	{
		CardWarsProfile profile = new CardWarsProfile();
		profile.id = id;
		profile.displayName = name;
		profile.builtIn = builtIn;
		profile.allContentUnlocked = unlockAll;
		profile.infiniteCoins = coins;
		profile.infiniteGems = gems;
		profile.infiniteStamina = stamina;
		profile.minimumCoins = minimumCoins;
		profile.minimumGems = minimumGems;
		profile.createdUtcTicks = DateTime.UtcNow.Ticks;
		profile.lastPlayedUtcTicks = 0L;
		return profile;
	}

	private static CardWarsProfile FindProfile(string id)
	{
		if (data == null || data.profiles == null)
		{
			return null;
		}
		for (int i = 0; i < data.profiles.Count; i++)
		{
			if (data.profiles[i] != null && data.profiles[i].id == id)
			{
				return data.profiles[i];
			}
		}
		return null;
	}

	private static int FindProfileIndex(string id)
	{
		EnsureProfiles();
		for (int i = 0; i < data.profiles.Count; i++)
		{
			if (data.profiles[i].id == id)
			{
				return i;
			}
		}
		return -1;
	}

	private static string SanitizeId(string id)
	{
		char[] chars = id.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
			{
				chars[i] = '_';
			}
		}
		return new string(chars);
	}

	private static void SaveProfiles()
	{
		PlayerPrefs.SetString(ProfilesKey, JsonUtility.ToJson(data));
		PlayerPrefs.Save();
	}

	private void ActivateProfile(CardWarsProfile profile)
	{
		if (profile == null)
		{
			return;
		}
		CardWarsProfile current = ActiveProfile;
		PlayerInfoScript playerInfo = PlayerInfoScript.GetInstance();
		if (current.id != profile.id && IsPlayerReady() && playerInfo != null)
		{
			playerInfo.Save();
		}
		profile.lastPlayedUtcTicks = DateTime.UtcNow.Ticks;
		PlayerPrefs.SetString(ActiveProfileKey, profile.id);
		SaveProfiles();
		if (current.id == profile.id)
		{
			if (IsPlayerReady())
			{
				CardWarsMod.Apply(playerInfo);
			}
			menuVisible = false;
			return;
		}
		menuVisible = false;
		if (LanRealtimeManager.Instance != null)
		{
			LanRealtimeManager.Instance.Disconnect();
		}
		if (playerInfo != null)
		{
			playerInfo.ReloadGame();
		}
		else
		{
			SceneManager.LoadScene("AppReloadScene");
		}
	}

	private void ResetProfile(CardWarsProfile profile)
	{
		if (profile == null)
		{
			return;
		}
		if (resetConfirmationId != profile.id)
		{
			resetConfirmationId = profile.id;
			return;
		}
		string activeFile = profile.id == UnlimitedProfileId ? LegacyGameFile : "game_profile_" + SanitizeId(profile.id) + ".json";
		try
		{
			PlayerInfoScript playerInfo = PlayerInfoScript.GetInstance();
			SessionManager sessionManager = SessionManager.GetInstance();
			if (sessionManager != null)
			{
				string path = sessionManager.GetPlayerDataPath(activeFile);
				if (!string.IsNullOrEmpty(path) && File.Exists(path))
				{
					File.Delete(path);
				}
			}
			resetConfirmationId = null;
			if (ActiveProfile.id == profile.id)
			{
				menuVisible = false;
				if (playerInfo != null)
				{
					playerInfo.ReloadGame();
				}
			}
		}
		catch (Exception exception)
		{
			Debug.LogWarning("No se pudo reiniciar el perfil: " + exception.Message);
		}
	}

	private void CreateCustomProfile()
	{
		string trimmedName = newProfileName.Trim();
		if (trimmedName.Length == 0)
		{
			trimmedName = "Nuevo aventurero";
		}
		string id = "custom_" + DateTime.UtcNow.Ticks;
		CardWarsProfile profile = CreateProfile(id, trimmedName, false, newUnlockAll, newInfiniteCoins, newInfiniteGems, newInfiniteStamina, newMinimumCoins, newMinimumGems);
		data.profiles.Add(profile);
		SaveProfiles();
		selectedIndex = data.profiles.Count - 1;
		createMode = false;
	}

	private void DeleteProfile(CardWarsProfile profile)
	{
		if (profile == null || profile.builtIn || profile.id == ActiveProfile.id)
		{
			return;
		}
		if (resetConfirmationId != "delete_" + profile.id)
		{
			resetConfirmationId = "delete_" + profile.id;
			return;
		}
		ResetProfileFile(profile);
		data.profiles.Remove(profile);
		selectedIndex = Mathf.Clamp(selectedIndex - 1, 0, data.profiles.Count - 1);
		resetConfirmationId = null;
		SaveProfiles();
	}

	private static void ResetProfileFile(CardWarsProfile profile)
	{
		try
		{
			SessionManager sessionManager = SessionManager.GetInstance();
			if (sessionManager == null)
			{
				return;
			}
			string file = profile.id == UnlimitedProfileId ? LegacyGameFile : "game_profile_" + SanitizeId(profile.id) + ".json";
			string path = sessionManager.GetPlayerDataPath(file);
			if (!string.IsNullOrEmpty(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception exception)
		{
			Debug.LogWarning("No se pudo borrar el guardado del perfil: " + exception.Message);
		}
	}

	private void AddCurrency(int coins, int gems)
	{
		PlayerInfoScript playerInfo = PlayerInfoScript.GetInstance();
		if (playerInfo == null || !IsPlayerReady())
		{
			return;
		}
		if (coins > 0)
		{
			playerInfo.Coins += coins;
		}
		if (gems > 0)
		{
			playerInfo.Gems += gems;
		}
		playerInfo.Save();
	}

	private static bool IsPlayerReady()
	{
		try
		{
			SessionManager manager = SessionManager.GetInstance();
			return manager != null && manager.IsReady();
		}
		catch
		{
			return false;
		}
	}

	private static Texture2D MakeTexture(Color color)
	{
		Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
		texture.hideFlags = HideFlags.DontSave;
		texture.SetPixel(0, 0, color);
		texture.Apply();
		return texture;
	}

	private void EnsureStyles()
	{
		if (panelTexture == null)
		{
			panelTexture = MakeTexture(new Color(0.14f, 0.09f, 0.05f, 0.97f));
			goldTexture = MakeTexture(new Color(0.76f, 0.45f, 0.08f, 1f));
			selectedTexture = MakeTexture(new Color(0.35f, 0.18f, 0.06f, 1f));
			panelStyle = new GUIStyle(GUI.skin.box);
			panelStyle.normal.background = panelTexture;
			titleStyle = new GUIStyle(GUI.skin.label);
			titleStyle.alignment = TextAnchor.MiddleCenter;
			titleStyle.fontStyle = FontStyle.Bold;
			titleStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);
			headingStyle = new GUIStyle(titleStyle);
			headingStyle.alignment = TextAnchor.MiddleLeft;
			labelStyle = new GUIStyle(GUI.skin.label);
			labelStyle.wordWrap = true;
			labelStyle.normal.textColor = Color.white;
			smallStyle = new GUIStyle(labelStyle);
			smallStyle.normal.textColor = new Color(0.85f, 0.75f, 0.58f);
			buttonStyle = new GUIStyle(GUI.skin.button);
			buttonStyle.normal.textColor = Color.white;
			buttonStyle.hover.textColor = Color.white;
			buttonStyle.active.textColor = Color.white;
			selectedButtonStyle = new GUIStyle(buttonStyle);
			selectedButtonStyle.normal.background = selectedTexture;
			toggleStyle = new GUIStyle(GUI.skin.toggle);
			toggleStyle.normal.textColor = Color.white;
			toggleStyle.onNormal.textColor = new Color(1f, 0.82f, 0.25f);
		}
		if (styledWidth == Screen.width && styledHeight == Screen.height)
		{
			return;
		}
		styledWidth = Screen.width;
		styledHeight = Screen.height;
		int font = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.028f), 18, 34);
		titleStyle.fontSize = font + 10;
		headingStyle.fontSize = font + 4;
		labelStyle.fontSize = font;
		smallStyle.fontSize = Mathf.Max(16, font - 4);
		buttonStyle.fontSize = font;
		selectedButtonStyle.fontSize = font;
		toggleStyle.fontSize = font;
	}

	private static Rect GetSafeArea()
	{
		Rect area = Screen.safeArea;
		return new Rect(area.x, Screen.height - area.yMax, area.width, area.height);
	}

	private bool IsBattleRunning()
	{
		try
		{
			return GameState.Instance != null && GameState.Instance.IsSetUp;
		}
		catch
		{
			return false;
		}
	}

	private void OnGUI()
	{
		GUI.depth = -11000;
		GUI.matrix = Matrix4x4.identity;
		EnsureStyles();
		Rect safe = GetSafeArea();
		float quickHeight = Mathf.Clamp(safe.height * 0.065f, 48f, 72f);
		if (!menuVisible)
		{
			return;
		}
		GUI.Box(safe, string.Empty, panelStyle);
		float margin = Mathf.Clamp(safe.width * 0.025f, 18f, 42f);
		GUILayout.BeginArea(new Rect(safe.x + margin, safe.y + margin * 0.5f, safe.width - margin * 2f, safe.height - margin));
		GUILayout.Label("CARD WARS · PERFILES", titleStyle, GUILayout.Height(quickHeight));
		GUILayout.Label("Cada perfil conserva por separado su historia, mazos, cartas y recursos.", smallStyle);
		GUILayout.Space(8f);
		GUILayout.BeginHorizontal();
		DrawProfileList(Mathf.Clamp(safe.width * 0.30f, 260f, 430f));
		GUILayout.Space(18f);
		if (createMode)
		{
			DrawCreatePanel();
		}
		else
		{
			DrawProfileDetails();
		}
		GUILayout.EndHorizontal();
		GUILayout.EndArea();
	}

	private void DrawProfileList(float width)
	{
		GUILayout.BeginVertical(panelStyle, GUILayout.Width(width), GUILayout.ExpandHeight(true));
		GUILayout.Label("AVENTUREROS", headingStyle);
		profileScroll = GUILayout.BeginScrollView(profileScroll);
		for (int i = 0; i < data.profiles.Count; i++)
		{
			CardWarsProfile profile = data.profiles[i];
			string prefix = profile.id == ActiveProfile.id ? "★ " : string.Empty;
			GUIStyle style = i == selectedIndex && !createMode ? selectedButtonStyle : buttonStyle;
			if (GUILayout.Button(prefix + profile.displayName, style, GUILayout.MinHeight(58f)))
			{
				selectedIndex = i;
				createMode = false;
				resetConfirmationId = null;
			}
		}
		GUILayout.EndScrollView();
		if (GUILayout.Button("+ CREAR PERFIL", buttonStyle, GUILayout.MinHeight(58f)))
		{
			createMode = true;
			resetConfirmationId = null;
		}
		GUILayout.EndVertical();
	}

	private void DrawCreatePanel()
	{
		GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
		detailScroll = GUILayout.BeginScrollView(detailScroll);
		GUILayout.Label("NUEVO PERFIL", headingStyle);
		GUILayout.Label("Nombre", labelStyle);
		newProfileName = GUILayout.TextField(newProfileName, GUILayout.MinHeight(52f));
		GUILayout.Space(10f);
		GUILayout.Label("Elige las reglas iniciales. Podrás cambiarlas después.", smallStyle);
		newUnlockAll = DrawCheckbox("Desbloquear todo el contenido", newUnlockAll);
		newInfiniteCoins = DrawCheckbox("Monedas infinitas", newInfiniteCoins);
		newInfiniteGems = DrawCheckbox("Gemas infinitas", newInfiniteGems);
		newInfiniteStamina = DrawCheckbox("Corazones/energía ilimitados", newInfiniteStamina);
		GUILayout.Space(12f);
		newMinimumCoins = DrawAmount("Monedas mínimas", newMinimumCoins, 1000);
		newMinimumGems = DrawAmount("Gemas mínimas", newMinimumGems, 100);
		GUILayout.EndScrollView();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("CANCELAR", buttonStyle, GUILayout.MinHeight(62f)))
		{
			createMode = false;
		}
		if (GUILayout.Button("CREAR", buttonStyle, GUILayout.MinHeight(62f)))
		{
			CreateCustomProfile();
		}
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private int DrawAmount(string label, int value, int step)
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label(label + ": " + value, labelStyle, GUILayout.ExpandWidth(true));
		if (GUILayout.Button("−", buttonStyle, GUILayout.Width(70f), GUILayout.Height(48f)))
		{
			value = Mathf.Max(0, value - step);
		}
		if (GUILayout.Button("+", buttonStyle, GUILayout.Width(70f), GUILayout.Height(48f)))
		{
			value = Mathf.Min(1000000000, value + step);
		}
		GUILayout.EndHorizontal();
		return value;
	}

	private bool DrawCheckbox(string label, bool value)
	{
		GUIStyle style = value ? selectedButtonStyle : buttonStyle;
		if (GUILayout.Button((value ? "☑  " : "☐  ") + label, style, GUILayout.MinHeight(48f)))
		{
			value = !value;
		}
		return value;
	}

	private void DrawProfileDetails()
	{
		selectedIndex = Mathf.Clamp(selectedIndex, 0, data.profiles.Count - 1);
		CardWarsProfile profile = data.profiles[selectedIndex];
		bool isActive = profile.id == ActiveProfile.id;
		GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
		detailScroll = GUILayout.BeginScrollView(detailScroll);
		GUILayout.Label(profile.displayName.ToUpperInvariant(), headingStyle);
		GUILayout.Label(profile.id == StoryProfileId ? "Campaña limpia: empieza con el tutorial y desbloquea todo poco a poco." : "Configura libremente las ayudas de este aventurero.", smallStyle);
		GUILayout.Space(10f);
		bool oldUnlockAll = profile.allContentUnlocked;
		bool oldInfiniteCoins = profile.infiniteCoins;
		bool oldInfiniteGems = profile.infiniteGems;
		bool oldInfiniteStamina = profile.infiniteStamina;
		int oldMinimumCoins = profile.minimumCoins;
		int oldMinimumGems = profile.minimumGems;
		profile.allContentUnlocked = DrawCheckbox("Desbloquear todo el contenido", profile.allContentUnlocked);
		profile.infiniteCoins = DrawCheckbox("Monedas infinitas", profile.infiniteCoins);
		profile.infiniteGems = DrawCheckbox("Gemas infinitas", profile.infiniteGems);
		profile.infiniteStamina = DrawCheckbox("Corazones/energía ilimitados", profile.infiniteStamina);
		profile.minimumCoins = DrawAmount("Monedas mínimas", profile.minimumCoins, 1000);
		profile.minimumGems = DrawAmount("Gemas mínimas", profile.minimumGems, 100);
		if (oldUnlockAll != profile.allContentUnlocked || oldInfiniteCoins != profile.infiniteCoins || oldInfiniteGems != profile.infiniteGems || oldInfiniteStamina != profile.infiniteStamina || oldMinimumCoins != profile.minimumCoins || oldMinimumGems != profile.minimumGems)
		{
			SaveProfiles();
		}
		GUILayout.Space(8f);
		GUILayout.Label("BÓVEDA LOCAL · no realiza compras reales", headingStyle);
		GUILayout.BeginHorizontal();
		GUI.enabled = isActive;
		if (GUILayout.Button("+10 000 MONEDAS", buttonStyle, GUILayout.MinHeight(54f)))
		{
			AddCurrency(10000, 0);
		}
		if (GUILayout.Button("+500 GEMAS", buttonStyle, GUILayout.MinHeight(54f)))
		{
			AddCurrency(0, 500);
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();
		if (!isActive)
		{
			GUILayout.Label("Activa el perfil para modificar su saldo.", smallStyle);
		}
		GUILayout.EndScrollView();
		if (GUILayout.Button(isActive ? "CONTINUAR CON ESTE PERFIL" : "ACTIVAR PERFIL", buttonStyle, GUILayout.MinHeight(64f)))
		{
			ActivateProfile(profile);
		}
		GUILayout.BeginHorizontal();
		string resetText = resetConfirmationId == profile.id ? "CONFIRMAR REINICIO" : "REINICIAR DESDE CERO";
		if (GUILayout.Button(resetText, buttonStyle, GUILayout.MinHeight(56f)))
		{
			ResetProfile(profile);
		}
		if (!profile.builtIn && !isActive)
		{
			string deleteText = resetConfirmationId == "delete_" + profile.id ? "CONFIRMAR BORRADO" : "BORRAR PERFIL";
			if (GUILayout.Button(deleteText, buttonStyle, GUILayout.MinHeight(56f)))
			{
				DeleteProfile(profile);
			}
		}
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}
}
