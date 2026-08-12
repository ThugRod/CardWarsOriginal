using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Multiplayer;
using UnityEngine;

public class LanRealtimeManager : MonoBehaviour
{
	[Serializable]
	public class PlayerProfile
	{
		public string playerName;
		public string leaderId;
		public int leaderLevel;
		public string[] cards;
		public int[] cardLevels;
		public string[] landscapes;
	}

	[Serializable]
	private class WirePacket
	{
		public int protocol = 1;
		public int sequence;
		public string type;
		public string payload;
	}

	[Serializable]
	private class HelloPayload
	{
		public bool host;
		public int matchSeed;
		public PlayerProfile profile;
	}

	[Serializable]
	private class PingPayload
	{
		public long sentTicks;
	}

	public enum ConnectionState
	{
		Disconnected,
		Hosting,
		Connecting,
		Connected,
		Error
	}

	public const int Port = 39777;
	public static LanRealtimeManager Instance { get; private set; }
	public static bool IsConnected
	{
		get { return Instance != null && Instance.State == ConnectionState.Connected; }
	}
	public static bool IsLanMatch
	{
		get
		{
			CWMPMapController controller = CWMPMapController.GetInstance();
			return controller != null && controller.mLastMPData != null && !string.IsNullOrEmpty(controller.mLastMPData.mMatchID) && controller.mLastMPData.mMatchID.StartsWith("LAN-", StringComparison.Ordinal);
		}
	}

	public ConnectionState State
	{
		get { return (ConnectionState)stateValue; }
	}

	public bool IsHost { get; private set; }
	public int MatchSeed { get; private set; }
	public PlayerProfile LocalProfile { get; private set; }
	public PlayerProfile PeerProfile { get; private set; }
	public int PingMilliseconds { get; private set; }
	public string StatusText { get; private set; }

	private volatile int stateValue;
	private volatile bool running;
	private TcpListener listener;
	private TcpClient client;
	private StreamReader reader;
	private StreamWriter writer;
	private Thread connectionThread;
	private Thread readerThread;
	private readonly object writerLock = new object();
	private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
	private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
	private Action peerReadyCallback;
	private string joinAddress = "192.168.1.2";
	private bool lobbyVisible;
	private int nextSequence = 1;
	private float nextPingTime;
	private GUIStyle titleStyle;
	private GUIStyle labelStyle;
	private GUIStyle buttonStyle;
	private GUIStyle fieldStyle;
	private GUIStyle boxStyle;
	private int styledScreenWidth;
	private int styledScreenHeight;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null)
		{
			return;
		}
		GameObject gameObject = new GameObject("LanRealtimeManager");
		DontDestroyOnLoad(gameObject);
		gameObject.AddComponent<LanRealtimeManager>();
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
		SetState(ConnectionState.Disconnected, "Sin conexión LAN");
	}

	public void ShowLobby(Action onPeerReady)
	{
		peerReadyCallback = onPeerReady;
		lobbyVisible = true;
		if (IsConnected && PeerProfile != null)
		{
			NotifyPeerReady();
		}
	}

	private void NotifyPeerReady()
	{
		Action callback = peerReadyCallback;
		peerReadyCallback = null;
		lobbyVisible = false;
		if (callback != null)
		{
			callback();
		}
	}

	public void Host()
	{
		PlayerProfile profile;
		if (!TryBuildLocalProfile(out profile))
		{
			return;
		}
		Disconnect();
		LocalProfile = profile;
		IsHost = true;
		MatchSeed = Environment.TickCount & int.MaxValue;
		running = true;
		SetState(ConnectionState.Hosting, "Esperando al segundo jugador en " + GetLocalIPv4() + ":" + Port);
		connectionThread = new Thread(HostWorker);
		connectionThread.IsBackground = true;
		connectionThread.Start();
	}

	public void Join(string address)
	{
		PlayerProfile profile;
		if (!TryBuildLocalProfile(out profile))
		{
			return;
		}
		if (string.IsNullOrEmpty(address))
		{
			SetState(ConnectionState.Error, "Escribe la IP del anfitrión");
			return;
		}
		Disconnect();
		LocalProfile = profile;
		IsHost = false;
		MatchSeed = 0;
		running = true;
		SetState(ConnectionState.Connecting, "Conectando con " + address + ":" + Port);
		connectionThread = new Thread(delegate() { JoinWorker(address); });
		connectionThread.IsBackground = true;
		connectionThread.Start();
	}

	public void Disconnect()
	{
		running = false;
		PeerProfile = null;
		PingMilliseconds = 0;
		try
		{
			if (reader != null)
			{
				reader.Close();
			}
		}
		catch
		{
		}
		try
		{
			if (writer != null)
			{
				writer.Close();
			}
		}
		catch
		{
		}
		try
		{
			if (client != null)
			{
				client.Close();
			}
		}
		catch
		{
		}
		try
		{
			if (listener != null)
			{
				listener.Stop();
			}
		}
		catch
		{
		}
		reader = null;
		writer = null;
		client = null;
		listener = null;
		SetState(ConnectionState.Disconnected, "Sin conexión LAN");
	}

	private void HostWorker()
	{
		try
		{
			listener = new TcpListener(IPAddress.Any, Port);
			listener.Start(1);
			TcpClient accepted = listener.AcceptTcpClient();
			if (running)
			{
				AttachClient(accepted);
			}
		}
		catch (Exception exception)
		{
			if (running)
			{
				notices.Enqueue("ERROR|No se pudo alojar: " + exception.Message);
			}
		}
	}

	private void JoinWorker(string address)
	{
		try
		{
			TcpClient connectedClient = new TcpClient();
			connectedClient.NoDelay = true;
			IAsyncResult asyncResult = connectedClient.BeginConnect(address, Port, null, null);
			if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(8.0)))
			{
				connectedClient.Close();
				throw new TimeoutException("tiempo de conexión agotado");
			}
			connectedClient.EndConnect(asyncResult);
			if (running)
			{
				AttachClient(connectedClient);
			}
		}
		catch (Exception exception)
		{
			if (running)
			{
				notices.Enqueue("ERROR|No se pudo conectar: " + exception.Message);
			}
		}
	}

	private void AttachClient(TcpClient connectedClient)
	{
		client = connectedClient;
		client.NoDelay = true;
		NetworkStream stream = client.GetStream();
		reader = new StreamReader(stream, Encoding.UTF8);
		writer = new StreamWriter(stream, new UTF8Encoding(false));
		writer.AutoFlush = true;
		notices.Enqueue("CONNECTED");
		readerThread = new Thread(ReadWorker);
		readerThread.IsBackground = true;
		readerThread.Start();
	}

	private void ReadWorker()
	{
		try
		{
			while (running && reader != null)
			{
				string text = reader.ReadLine();
				if (text == null)
				{
					break;
				}
				incoming.Enqueue(text);
			}
		}
		catch (Exception exception)
		{
			if (running)
			{
				notices.Enqueue("ERROR|Conexión perdida: " + exception.Message);
			}
		}
		if (running)
		{
			notices.Enqueue("ERROR|El otro jugador se desconectó");
		}
	}

	private void Update()
	{
		string notice;
		while (notices.TryDequeue(out notice))
		{
			if (notice == "CONNECTED")
			{
				SetState(ConnectionState.Connected, "Conectado; intercambiando mazos...");
				SendHello();
			}
			else if (notice.StartsWith("ERROR|"))
			{
				running = false;
				SetState(ConnectionState.Error, notice.Substring(6));
			}
		}
		string rawPacket;
		while (incoming.TryDequeue(out rawPacket))
		{
			HandlePacket(rawPacket);
		}
		if (State == ConnectionState.Connected && Time.realtimeSinceStartup >= nextPingTime)
		{
			nextPingTime = Time.realtimeSinceStartup + 1f;
			PingPayload pingPayload = new PingPayload();
			pingPayload.sentTicks = DateTime.UtcNow.Ticks;
			Send("ping", JsonUtility.ToJson(pingPayload));
		}
		ApplyPeerProfileToLegacyState();
	}

	private void SendHello()
	{
		HelloPayload helloPayload = new HelloPayload();
		helloPayload.host = IsHost;
		helloPayload.matchSeed = MatchSeed;
		helloPayload.profile = LocalProfile;
		Send("hello", JsonUtility.ToJson(helloPayload));
	}

	private void HandlePacket(string rawPacket)
	{
		WirePacket wirePacket;
		try
		{
			wirePacket = JsonUtility.FromJson<WirePacket>(rawPacket);
		}
		catch (Exception exception)
		{
			SetState(ConnectionState.Error, "Paquete LAN inválido: " + exception.Message);
			return;
		}
		if (wirePacket == null || wirePacket.protocol != 1)
		{
			SetState(ConnectionState.Error, "Las dos versiones del mod LAN no coinciden");
			return;
		}
		if (wirePacket.type == "hello")
		{
			HelloPayload helloPayload = JsonUtility.FromJson<HelloPayload>(wirePacket.payload);
			PeerProfile = helloPayload.profile;
			if (!IsHost && helloPayload.host)
			{
				MatchSeed = helloPayload.matchSeed;
			}
			if (PeerProfile != null)
			{
				SetState(ConnectionState.Connected, "Listo contra " + PeerProfile.playerName);
				NotifyPeerReady();
			}
		}
		else if (wirePacket.type == "ping")
		{
			Send("pong", wirePacket.payload);
		}
		else if (wirePacket.type == "pong")
		{
			PingPayload pingPayload = JsonUtility.FromJson<PingPayload>(wirePacket.payload);
			long ticks = DateTime.UtcNow.Ticks - pingPayload.sentTicks;
			PingMilliseconds = Mathf.Max(0, (int)TimeSpan.FromTicks(ticks).TotalMilliseconds);
		}
	}

	private void Send(string type, string payload)
	{
		if (writer == null)
		{
			return;
		}
		WirePacket wirePacket = new WirePacket();
		wirePacket.sequence = nextSequence++;
		wirePacket.type = type;
		wirePacket.payload = payload;
		string value = JsonUtility.ToJson(wirePacket);
		try
		{
			lock (writerLock)
			{
				writer.WriteLine(value);
				writer.Flush();
			}
		}
		catch (Exception exception)
		{
			SetState(ConnectionState.Error, "No se pudo enviar: " + exception.Message);
		}
	}

	private bool TryBuildLocalProfile(out PlayerProfile profile)
	{
		profile = null;
		PlayerInfoScript instance = PlayerInfoScript.GetInstance();
		if (instance == null)
		{
			SetState(ConnectionState.Error, "Espera a que termine de cargar tu perfil");
			return false;
		}
		Deck selectedDeckCopy = instance.GetSelectedDeckCopy();
		if (selectedDeckCopy == null || selectedDeckCopy.Leader == null || selectedDeckCopy.CardCount() == 0)
		{
			SetState(ConnectionState.Error, "Selecciona primero un mazo válido");
			return false;
		}
		profile = new PlayerProfile();
		profile.playerName = string.IsNullOrEmpty(instance.MPPlayerName) ? "Jugador LAN" : instance.MPPlayerName;
		profile.leaderId = selectedDeckCopy.Leader.Form.ID;
		profile.leaderLevel = selectedDeckCopy.Leader.Rank;
		profile.cards = new string[selectedDeckCopy.CardCount()];
		profile.cardLevels = new int[selectedDeckCopy.CardCount()];
		for (int i = 0; i < selectedDeckCopy.CardCount(); i++)
		{
			CardItem card = selectedDeckCopy.GetCard(i);
			profile.cards[i] = card.Form.ID;
			profile.cardLevels[i] = card.Level;
		}
		profile.landscapes = new string[selectedDeckCopy.GetLandscapeCount()];
		for (int j = 0; j < selectedDeckCopy.GetLandscapeCount(); j++)
		{
			profile.landscapes[j] = selectedDeckCopy.GetLandscape(j).ToString();
		}
		return true;
	}

	public MatchData CreateLegacyMatchData()
	{
		if (PeerProfile == null)
		{
			return null;
		}
		return new MatchData("LAN-" + MatchSeed, PeerProfile.playerName, PeerProfile.leaderId, PeerProfile.leaderLevel, PeerProfile.landscapes);
	}

	public void ApplyPeerProfileToLegacyState()
	{
		if (PeerProfile == null || GlobalFlags.Instance == null)
		{
			return;
		}
		GlobalFlags.Instance.InMPMode = true;
		CWMPMapController instance = CWMPMapController.GetInstance();
		if (instance == null)
		{
			return;
		}
		CWMPMapController.MPData mPData = instance.mLastMPData;
		mPData.mCards = PeerProfile.cards;
		mPData.mCardLevels = PeerProfile.cardLevels;
		mPData.mLandscapes = PeerProfile.landscapes;
		mPData.OpponentLeader = PeerProfile.leaderId;
		mPData.mLeaderLevel = PeerProfile.leaderLevel;
		mPData.mMatchID = "LAN-" + MatchSeed;
		mPData.PlayerPVPName = (LocalProfile == null) ? "Jugador LAN" : LocalProfile.playerName;
		mPData.OpponentPVPName = PeerProfile.playerName;
		mPData.TrophyWin = 0;
		mPData.TrophyLoss = 0;
	}

	private void SetState(ConnectionState newState, string text)
	{
		stateValue = (int)newState;
		StatusText = text;
	}

	private static string GetLocalIPv4()
	{
		try
		{
			IPAddress[] addressList = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
			foreach (IPAddress iPAddress in addressList)
			{
				if (iPAddress.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(iPAddress))
				{
					return iPAddress.ToString();
				}
			}
		}
		catch
		{
		}
		return "IP no detectada";
	}

	private void EnsureStyles()
	{
		if (titleStyle == null)
		{
			titleStyle = new GUIStyle(GUI.skin.label);
			titleStyle.alignment = TextAnchor.MiddleCenter;
			titleStyle.normal.textColor = Color.white;
			labelStyle = new GUIStyle(GUI.skin.label);
			labelStyle.wordWrap = true;
			labelStyle.normal.textColor = Color.white;
			buttonStyle = new GUIStyle(GUI.skin.button);
			fieldStyle = new GUIStyle(GUI.skin.textField);
			boxStyle = new GUIStyle(GUI.skin.box);
		}
		if (styledScreenWidth == Screen.width && styledScreenHeight == Screen.height)
		{
			return;
		}
		styledScreenWidth = Screen.width;
		styledScreenHeight = Screen.height;
		int fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.035f), 20, 38);
		titleStyle.fontSize = fontSize + 4;
		labelStyle.fontSize = fontSize;
		buttonStyle.fontSize = fontSize;
		fieldStyle.fontSize = fontSize;
	}

	private static Rect GetGuiSafeArea()
	{
		Rect safeArea = Screen.safeArea;
		return new Rect(safeArea.x, Screen.height - safeArea.yMax, safeArea.width, safeArea.height);
	}

	private void OnGUI()
	{
		GUI.depth = -10000;
		GUI.enabled = true;
		GUI.color = Color.white;
		GUI.contentColor = Color.white;
		GUI.backgroundColor = Color.white;
		GUI.matrix = Matrix4x4.identity;
		EnsureStyles();
		Rect safeArea = GetGuiSafeArea();
		float buttonHeight = Mathf.Clamp(safeArea.height * 0.12f, 70f, 120f);
		if (!lobbyVisible)
		{
			return;
		}
		float width = Mathf.Min(safeArea.width * 0.9f, 1000f);
		float height = Mathf.Min(safeArea.height * 0.9f, 700f);
		Rect panel = new Rect(safeArea.x + (safeArea.width - width) * 0.5f, safeArea.y + (safeArea.height - height) * 0.5f, width, height);
		GUI.Box(panel, string.Empty, boxStyle);
		GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, panel.height - 36f));
		GUILayout.Label("Card Wars Online local", titleStyle, GUILayout.Height(buttonHeight));
		GUILayout.Label(StatusText + ((State == ConnectionState.Connected) ? "  Ping: " + PingMilliseconds + " ms" : string.Empty), labelStyle);
		GUILayout.Space(12f);
		GUILayout.Label("IP del anfitrión: " + GetLocalIPv4() + "   Puerto: " + Port, labelStyle);
		joinAddress = GUILayout.TextField(joinAddress, fieldStyle, GUILayout.Height(buttonHeight));
		GUILayout.Space(12f);
		GUILayout.BeginHorizontal();
		GUI.enabled = State == ConnectionState.Disconnected || State == ConnectionState.Error;
		if (GUILayout.Button("Crear partida", buttonStyle, GUILayout.Height(buttonHeight)))
		{
			Host();
		}
		if (GUILayout.Button("Unirse", buttonStyle, GUILayout.Height(buttonHeight)))
		{
			Join(joinAddress.Trim());
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();
		if (State == ConnectionState.Connected && PeerProfile != null)
		{
			GUILayout.Space(12f);
			GUILayout.Label("Rival: " + PeerProfile.playerName + "\nLíder: " + PeerProfile.leaderId + " nivel " + PeerProfile.leaderLevel + "\nMazo recibido: " + PeerProfile.cards.Length + " cartas", labelStyle);
			GUILayout.Label("Conexión lista. Los dos jugadores deben entrar al mapa PvP y pulsar Jugar.", labelStyle);
		}
		GUILayout.FlexibleSpace();
		GUILayout.BeginHorizontal();
		if (State != ConnectionState.Disconnected && GUILayout.Button("Desconectar", buttonStyle, GUILayout.Height(buttonHeight)))
		{
			Disconnect();
		}
		if (GUILayout.Button("Cerrar", buttonStyle, GUILayout.Height(buttonHeight)))
		{
			lobbyVisible = false;
		}
		GUILayout.EndHorizontal();
		GUILayout.EndArea();
	}

	private void OnApplicationQuit()
	{
		Disconnect();
	}
}
