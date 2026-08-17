using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
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
		public int protocol = 4;
		public int sequence;
		public string type;
		public string payload;
	}

	[Serializable]
	private class HelloPayload
	{
		public bool host;
		public int matchSeed;
		public bool turboMode;
		public PlayerProfile profile;
	}

	[Serializable]
	private class PingPayload
	{
		public long sentTicks;
	}

	[Serializable]
	private class ReadyPayload
	{
		public bool ready;
	}

	[Serializable]
	public class BattleActionPayload
	{
		public string kind;
		public string cardId;
		public int handIndex = -1;
		public int lane = -1;
		public int cardType;
		public int targetIndex = -1;
		public string battleResult;
		public int randomSeed;
	}

	[Serializable]
	private class BattleStatePayload
	{
		public int lane;
		public int userHealth;
		public int opponentHealth;
		public int[] userCreatureDamage;
		public int[] opponentCreatureDamage;
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
	private const int DiscoveryPort = 39778;
	private const string DiscoveryRequest = "CARDWARS_LAN_DISCOVER_V4";
	private const string DiscoveryResponse = "CARDWARS_LAN_HOST_V4";
	public static LanRealtimeManager Instance { get; private set; }
	public static bool IsConnected
	{
		get { return Instance != null && Instance.State == ConnectionState.Connected; }
	}
	public static bool IsLanMatch
	{
		get
		{
			// CWMPMapController belongs to the map scene and is destroyed while the
			// battle loads. The network manager persists across scenes, so the peer
			// profile is the reliable source of truth for the whole LAN match.
			return Instance != null && Instance.HasActiveLanSession;
		}
	}
	public static bool IsRealtimeBattle
	{
		get { return IsConnected && IsLanMatch; }
	}
	public static bool IsLobbyVisible
	{
		get { return Instance != null && Instance.lobbyVisible; }
	}

	public ConnectionState State
	{
		get { return (ConnectionState)stateValue; }
	}

	public bool IsHost { get; private set; }
	public int MatchSeed { get; private set; }
	public PlayerProfile LocalProfile { get; private set; }
	public PlayerProfile PeerProfile { get; private set; }
	public bool TurboModeSelected { get; private set; }
	public bool LocalReady { get; private set; }
	public bool PeerReady { get; private set; }
	public int PingMilliseconds { get; private set; }
	public string StatusText { get; private set; }

	public string LocalIpList
	{
		get { return GetLocalIPv4List(); }
	}
	public bool HasActiveLanSession
	{
		get { return State == ConnectionState.Connected && running && matchCommitted && PeerProfile != null && MatchSeed != 0; }
	}

	private volatile int stateValue;
	private volatile bool running;
	private TcpListener listener;
	private TcpClient client;
	private StreamReader reader;
	private StreamWriter writer;
	private Thread connectionThread;
	private Thread readerThread;
	private Thread discoveryThread;
	private UdpClient discoverySocket;
	private readonly object writerLock = new object();
	private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
	private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
	private readonly Queue<BattleActionPayload> remoteBattleActions = new Queue<BattleActionPayload>();
	private readonly Queue<BattleActionPayload> remoteTargets = new Queue<BattleActionPayload>();
	private readonly Dictionary<int, string> remoteBattleResults = new Dictionary<int, string>();
	private readonly Dictionary<int, BattleStatePayload> remoteBattleStates = new Dictionary<int, BattleStatePayload>();
	private Action peerReadyCallback;
	private string joinAddress = string.Empty;
	private bool lobbyVisible;
	private bool matchCommitted;
	private int nextSequence = 1;
	private int nextBattleActionSequence = 1;
	private int currentActionSeed;
	public bool IsApplyingRemoteAction { get; private set; }
	private float nextPingTime;
	private GUIStyle titleStyle;
	private GUIStyle labelStyle;
	private GUIStyle buttonStyle;
	private GUIStyle fieldStyle;
	private GUIStyle boxStyle;
	private Vector2 lobbyScroll;
	private Texture2D lobbyPanelTexture;
	private Texture2D lobbyButtonTexture;
	private Texture2D lobbyFieldTexture;
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
		gameObject.hideFlags = HideFlags.DontSave;
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
		ShowLobby(onPeerReady, true);
	}

	public void ShowLobby(Action onPeerReady, bool showOverlay)
	{
		peerReadyCallback = onPeerReady;
		lobbyVisible = showOverlay;
		if (matchCommitted && IsConnected && PeerProfile != null)
		{
			NotifyPeerReady();
		}
	}

	public void ToggleTurboMode()
	{
		if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
		{
			return;
		}
		TurboModeSelected = !TurboModeSelected;
		if (IsHost)
		{
			ApplySelectedBattleMode();
		}
	}

	public void FindMatchAutomatically()
	{
		PlayerProfile profile;
		if (!TryBuildLocalProfile(out profile))
		{
			return;
		}
		Disconnect();
		LocalProfile = profile;
		IsHost = false;
		running = true;
		SetState(ConnectionState.Connecting, "Buscando otro jugador en tu Wi-Fi...");
		discoveryThread = new Thread(AutoMatchWorker);
		discoveryThread.IsBackground = true;
		discoveryThread.Start();
	}

	public void SetLocalReady(bool ready)
	{
		if (State != ConnectionState.Connected || PeerProfile == null || matchCommitted)
		{
			return;
		}
		LocalReady = ready;
		ReadyPayload payload = new ReadyPayload();
		payload.ready = ready;
		Send("ready", JsonUtility.ToJson(payload));
		UpdateReadyStatus();
		TryCommitReadyMatch();
	}

	private void UpdateReadyStatus()
	{
		if (matchCommitted)
		{
			SetState(ConnectionState.Connected, "Los dos están listos. Iniciando partida...");
		}
		else if (LocalReady && !PeerReady)
		{
			SetState(ConnectionState.Connected, "Estás listo. Esperando al otro jugador...");
		}
		else if (!LocalReady && PeerReady)
		{
			SetState(ConnectionState.Connected, "El otro jugador está listo. Pulsa LISTO para comenzar.");
		}
		else
		{
			SetState(ConnectionState.Connected, "Conectados. Ambos deben pulsar LISTO.");
		}
	}

	private void TryCommitReadyMatch()
	{
		if (matchCommitted || !LocalReady || !PeerReady || PeerProfile == null || State != ConnectionState.Connected)
		{
			return;
		}
		matchCommitted = true;
		UpdateReadyStatus();
		NotifyPeerReady();
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
		ApplySelectedBattleMode();
		MatchSeed = Environment.TickCount & int.MaxValue;
		running = true;
		SetState(ConnectionState.Hosting, "Esperando al segundo jugador en " + GetLocalIPv4() + ":" + Port);
		connectionThread = new Thread(HostWorker);
		connectionThread.IsBackground = true;
		connectionThread.Start();
		discoveryThread = new Thread(HostDiscoveryWorker);
		discoveryThread.IsBackground = true;
		discoveryThread.Start();
	}

	public void Join(string address)
	{
		PlayerProfile profile;
		if (!TryBuildLocalProfile(out profile))
		{
			return;
		}
		address = NormalizeJoinAddress(address);
		if (string.IsNullOrEmpty(address))
		{
			SetState(ConnectionState.Error, "Escribe la IP del anfitrión");
			return;
		}
		Disconnect();
		LocalProfile = profile;
		IsHost = false;
		BattleModeRules.UseNormalMode();
		MatchSeed = 0;
		running = true;
		SetState(ConnectionState.Connecting, "Conectando con " + address + ":" + Port);
		connectionThread = new Thread(delegate() { JoinWorker(address); });
		connectionThread.IsBackground = true;
		connectionThread.Start();
	}

	public void DiscoverHost()
	{
		PlayerProfile profile;
		if (!TryBuildLocalProfile(out profile))
		{
			return;
		}
		Disconnect();
		LocalProfile = profile;
		IsHost = false;
		running = true;
		SetState(ConnectionState.Connecting, "Buscando una partida en la red local...");
		discoveryThread = new Thread(DiscoverHostWorker);
		discoveryThread.IsBackground = true;
		discoveryThread.Start();
	}

	public void Disconnect()
	{
		running = false;
		PeerProfile = null;
		LocalReady = false;
		PeerReady = false;
		matchCommitted = false;
		PingMilliseconds = 0;
		remoteBattleActions.Clear();
		remoteTargets.Clear();
		remoteBattleResults.Clear();
		remoteBattleStates.Clear();
		IsApplyingRemoteAction = false;
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
			if (discoverySocket != null)
			{
				discoverySocket.Close();
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
		discoverySocket = null;
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

	private void HostDiscoveryWorker()
	{
		try
		{
			using (UdpClient socket = new UdpClient())
			{
				socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				socket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
				discoverySocket = socket;
				while (running && IsHost)
				{
					IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
					byte[] data = socket.Receive(ref sender);
					string request = Encoding.UTF8.GetString(data);
					if (request == DiscoveryRequest)
					{
						byte[] response = Encoding.UTF8.GetBytes(DiscoveryResponse + "|" + Port);
						socket.Send(response, response.Length, sender);
					}
				}
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (SocketException exception)
		{
			if (running && IsHost)
			{
				Debug.LogWarning("Descubrimiento LAN no disponible: " + exception.Message);
			}
		}
	}

	private void DiscoverHostWorker()
	{
		try
		{
			using (UdpClient socket = new UdpClient())
			{
				discoverySocket = socket;
				socket.EnableBroadcast = true;
				socket.Client.ReceiveTimeout = 1000;
				byte[] request = Encoding.UTF8.GetBytes(DiscoveryRequest);
				List<IPAddress> broadcasts = GetBroadcastAddresses();
				DateTime limit = DateTime.UtcNow.AddSeconds(8.0);
				while (running && DateTime.UtcNow < limit)
				{
					foreach (IPAddress broadcast in broadcasts)
					{
						try
						{
							socket.Send(request, request.Length, new IPEndPoint(broadcast, DiscoveryPort));
						}
						catch (SocketException)
						{
						}
					}
					try
					{
						IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
						byte[] responseData = socket.Receive(ref sender);
						string response = Encoding.UTF8.GetString(responseData);
						if (response.StartsWith(DiscoveryResponse + "|", StringComparison.Ordinal))
						{
							notices.Enqueue("DISCOVERED|" + sender.Address);
							return;
						}
					}
					catch (SocketException exception)
					{
						if (exception.SocketErrorCode != SocketError.TimedOut)
						{
							throw;
						}
					}
				}
			}
			if (running)
			{
				notices.Enqueue("ERROR|No se encontró anfitrión. Confirma que ambos móviles estén en la misma Wi-Fi y que la red no tenga aislamiento de clientes.");
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			if (running)
			{
				notices.Enqueue("ERROR|No se pudo buscar al anfitrión: " + exception.Message);
			}
		}
	}

	private void AutoMatchWorker()
	{
		try
		{
			using (UdpClient socket = new UdpClient())
			{
				discoverySocket = socket;
				socket.EnableBroadcast = true;
				socket.Client.ReceiveTimeout = 300;
				byte[] request = Encoding.UTF8.GetBytes(DiscoveryRequest);
				List<IPAddress> broadcasts = GetBroadcastAddresses();
				int lastOctet = 0;
				List<IPAddress> localAddresses = GetLocalIPv4Addresses();
				if (localAddresses.Count > 0)
				{
					byte[] localBytes = localAddresses[0].GetAddressBytes();
					lastOctet = localBytes[localBytes.Length - 1];
				}
				// A deterministic delay prevents two phones that tap Buscar together
				// from both becoming host. The earlier candidate starts hosting and
				// the other one discovers it during its remaining wait.
				DateTime limit = DateTime.UtcNow.AddSeconds(2.0 + (lastOctet % 10) * 0.55);
				while (running && DateTime.UtcNow < limit)
				{
					foreach (IPAddress broadcast in broadcasts)
					{
						try
						{
							socket.Send(request, request.Length, new IPEndPoint(broadcast, DiscoveryPort));
						}
						catch (SocketException)
						{
						}
					}
					try
					{
						IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
						byte[] responseData = socket.Receive(ref sender);
						string response = Encoding.UTF8.GetString(responseData);
						if (response.StartsWith(DiscoveryResponse + "|", StringComparison.Ordinal))
						{
							notices.Enqueue("DISCOVERED|" + sender.Address);
							return;
						}
					}
					catch (SocketException exception)
					{
						if (exception.SocketErrorCode != SocketError.TimedOut)
						{
							throw;
						}
					}
				}
			}
			if (running)
			{
				notices.Enqueue("BECOME_HOST");
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			if (running)
			{
				notices.Enqueue("ERROR|No se pudo buscar partida: " + exception.Message);
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
				throw new TimeoutException("tiempo agotado. Revisa que sea la IP Wi-Fi del anfitrión, que ambos estén en la misma red y que el router no aísle dispositivos");
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
			else if (notice.StartsWith("DISCOVERED|"))
			{
				string address = notice.Substring(11);
				joinAddress = address;
				Join(address);
			}
			else if (notice == "BECOME_HOST")
			{
				Host();
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
		ApplyPendingRemoteTarget();
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
		helloPayload.turboMode = TurboModeSelected;
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
		if (wirePacket == null || wirePacket.protocol != 4)
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
				TurboModeSelected = helloPayload.turboMode;
			}
			if (PeerProfile != null)
			{
				ApplySelectedBattleMode();
				LocalReady = false;
				PeerReady = false;
				matchCommitted = false;
				UpdateReadyStatus();
			}
		}
		else if (wirePacket.type == "ready")
		{
			ReadyPayload readyPayload = JsonUtility.FromJson<ReadyPayload>(wirePacket.payload);
			PeerReady = readyPayload != null && readyPayload.ready;
			UpdateReadyStatus();
			TryCommitReadyMatch();
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
		else if (wirePacket.type == "battle_action")
		{
			BattleActionPayload action = JsonUtility.FromJson<BattleActionPayload>(wirePacket.payload);
			if (action != null)
			{
				if (action.kind == "target")
				{
					remoteTargets.Enqueue(action);
				}
				else if (action.kind == "battle_result")
				{
					remoteBattleResults[action.lane] = action.battleResult;
				}
				else
				{
					remoteBattleActions.Enqueue(action);
				}
			}
		}
		else if (wirePacket.type == "battle_state")
		{
			BattleStatePayload state = JsonUtility.FromJson<BattleStatePayload>(wirePacket.payload);
			if (state != null)
			{
				remoteBattleStates[state.lane] = state;
			}
		}
	}

	private void ApplyPendingRemoteTarget()
	{
		if (!IsApplyingRemoteAction || remoteTargets.Count == 0 || GameState.Instance == null || !GameState.Instance.HasTargetingListener())
		{
			return;
		}
		BattleActionPayload target = remoteTargets.Dequeue();
		currentActionSeed = target.randomSeed;
		ApplyCurrentActionSeed();
		GameState.Instance.SelectTarget(target.targetIndex);
	}

	private BattleActionPayload CreateAction(string kind)
	{
		BattleActionPayload action = new BattleActionPayload();
		action.kind = kind;
		action.randomSeed = MatchSeed ^ (nextBattleActionSequence++ * 486187739) ^ (IsHost ? 1437226410 : 1515870810);
		currentActionSeed = action.randomSeed;
		return action;
	}

	private void SendBattleAction(BattleActionPayload action)
	{
		if (IsRealtimeBattle)
		{
			Send("battle_action", JsonUtility.ToJson(action));
		}
	}

	public void ReportCardPlayed(CardItem card, int lane)
	{
		if (!IsRealtimeBattle || IsApplyingRemoteAction || card == null)
		{
			return;
		}
		BattleActionPayload action = CreateAction("card");
		action.cardId = card.Form.ID;
		action.handIndex = GameState.Instance.GetHand(PlayerType.User).IndexOf(card);
		action.lane = lane;
		action.cardType = (int)card.Form.Type;
		SendBattleAction(action);
	}

	public void ReportFloop(int lane, CardType cardType)
	{
		if (!IsRealtimeBattle || IsApplyingRemoteAction)
		{
			return;
		}
		BattleActionPayload action = CreateAction("floop");
		action.lane = lane;
		action.cardType = (int)cardType;
		SendBattleAction(action);
	}

	public void ReportLeaderAbility()
	{
		if (!IsRealtimeBattle || IsApplyingRemoteAction)
		{
			return;
		}
		SendBattleAction(CreateAction("leader"));
	}

	public void ReportTarget(int targetIndex)
	{
		if (!IsRealtimeBattle || IsApplyingRemoteAction)
		{
			return;
		}
		BattleActionPayload action = CreateAction("target");
		action.targetIndex = targetIndex;
		SendBattleAction(action);
	}

	public void ReportEndTurn()
	{
		if (!IsRealtimeBattle || IsApplyingRemoteAction)
		{
			return;
		}
		SendBattleAction(CreateAction("end_turn"));
	}

	public void ReportBattleResult(int lane, string result)
	{
		if (!IsRealtimeBattle || IsApplyingRemoteAction)
		{
			return;
		}
		BattleActionPayload action = CreateAction("battle_result");
		action.lane = lane;
		action.battleResult = result;
		SendBattleAction(action);
	}

	public IEnumerator ExecuteRemoteOpponentTurn(CWOpponentActionSequencer sequencer)
	{
		while (IsRealtimeBattle)
		{
			if (remoteBattleActions.Count == 0)
			{
				yield return null;
				continue;
			}
			BattleActionPayload action = remoteBattleActions.Dequeue();
			IsApplyingRemoteAction = true;
			currentActionSeed = action.randomSeed;
			ApplyCurrentActionSeed();
			if (action.kind == "card")
			{
				yield return sequencer.StartCoroutine(sequencer.ExecuteRemoteCard(action.cardId, action.handIndex, action.lane));
			}
			else if (action.kind == "floop")
			{
				yield return sequencer.StartCoroutine(sequencer.ExecuteRemoteFloop(action.lane, (CardType)action.cardType));
			}
			else if (action.kind == "leader")
			{
				yield return sequencer.StartCoroutine(sequencer.ExecuteRemoteLeader());
			}
			else if (action.kind == "end_turn")
			{
				sequencer.FinishRemoteTurn();
				IsApplyingRemoteAction = false;
				yield break;
			}
			IsApplyingRemoteAction = false;
		}
	}

	public IEnumerator WaitForRemoteBattleResult(int lane, Action<string> callback)
	{
		while (IsRealtimeBattle && !remoteBattleResults.ContainsKey(lane))
		{
			yield return null;
		}
		string result;
		if (remoteBattleResults.TryGetValue(lane, out result))
		{
			remoteBattleResults.Remove(lane);
			callback(result);
		}
	}

	public void ReportBattleState(int lane)
	{
		if (!IsRealtimeBattle || BattlePhaseManager.GetInstance().Phase != BattlePhase.P1Battle)
		{
			return;
		}
		BattleStatePayload state = new BattleStatePayload();
		state.lane = lane;
		state.userHealth = GameState.Instance.GetHealth(PlayerType.User);
		state.opponentHealth = GameState.Instance.GetHealth(PlayerType.Opponent);
		state.userCreatureDamage = GetCreatureDamage(PlayerType.User);
		state.opponentCreatureDamage = GetCreatureDamage(PlayerType.Opponent);
		Send("battle_state", JsonUtility.ToJson(state));
	}

	public IEnumerator WaitAndApplyRemoteBattleState(int lane)
	{
		while (IsRealtimeBattle && !remoteBattleStates.ContainsKey(lane))
		{
			yield return null;
		}
		BattleStatePayload state;
		if (!remoteBattleStates.TryGetValue(lane, out state))
		{
			yield break;
		}
		remoteBattleStates.Remove(lane);
		GameState.Instance.SetHealth(PlayerType.User, state.opponentHealth);
		GameState.Instance.SetHealth(PlayerType.Opponent, state.userHealth);
		ApplyCreatureDamage(PlayerType.User, state.opponentCreatureDamage);
		ApplyCreatureDamage(PlayerType.Opponent, state.userCreatureDamage);
		GameState.Instance.CheckForDeaths();
		GameDataScript gameData = GameDataScript.GetInstance();
		if (gameData != null)
		{
			gameData.UpdateText();
		}
	}

	private static int[] GetCreatureDamage(PlayerType player)
	{
		int[] damage = new int[4];
		for (int i = 0; i < damage.Length; i++)
		{
			damage[i] = GameState.Instance.LaneHasCreature(player, i) ? GameState.Instance.GetCreature(player, i).Damage : -1;
		}
		return damage;
	}

	private static void ApplyCreatureDamage(PlayerType player, int[] damage)
	{
		if (damage == null)
		{
			return;
		}
		for (int i = 0; i < damage.Length && i < 4; i++)
		{
			if (damage[i] >= 0 && GameState.Instance.LaneHasCreature(player, i))
			{
				GameState.Instance.GetCreature(player, i).Damage = damage[i];
			}
		}
	}

	public void ApplyCurrentActionSeed()
	{
		if (IsRealtimeBattle && currentActionSeed != 0)
		{
			UnityEngine.Random.InitState(currentActionSeed);
		}
	}

	public void ShuffleBattleDeck(Deck deck, PlayerType localSide, bool shuffleLandscapes)
	{
		UnityEngine.Random.State previousState = UnityEngine.Random.state;
		bool ownerIsHost = (localSide == PlayerType.User) == IsHost;
		UnityEngine.Random.InitState(MatchSeed ^ (ownerIsHost ? 32452843 : 49979687));
		deck.Shuffle();
		if (shuffleLandscapes)
		{
			deck.ShuffleLandscapes();
		}
		UnityEngine.Random.state = previousState;
	}

	public int GetSynchronizedQuestIndex(int questCount)
	{
		return questCount <= 0 ? 0 : (MatchSeed & int.MaxValue) % questCount;
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
		ApplySelectedBattleMode();
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

	private void ApplySelectedBattleMode()
	{
		if (TurboModeSelected)
		{
			BattleModeRules.UseTurboMode(false);
		}
		else
		{
			BattleModeRules.UseNormalMode();
		}
	}

	private void SetState(ConnectionState newState, string text)
	{
		stateValue = (int)newState;
		StatusText = text;
	}

	private static string NormalizeJoinAddress(string address)
	{
		if (string.IsNullOrEmpty(address))
		{
			return string.Empty;
		}
		string normalized = address.Trim();
		if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(7);
		}
		else if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(8);
		}
		int slash = normalized.IndexOf('/');
		if (slash >= 0)
		{
			normalized = normalized.Substring(0, slash);
		}
		IPAddress parsedAddress;
		int colon = normalized.LastIndexOf(':');
		if (colon > 0 && normalized.IndexOf(':') == colon && IPAddress.TryParse(normalized.Substring(0, colon), out parsedAddress))
		{
			normalized = normalized.Substring(0, colon);
		}
		return normalized.Trim('[', ']', ' ');
	}

	private static List<IPAddress> GetLocalIPv4Addresses()
	{
		List<IPAddress> result = new List<IPAddress>();
		try
		{
			foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				{
					continue;
				}
				foreach (UnicastIPAddressInformation unicast in networkInterface.GetIPProperties().UnicastAddresses)
				{
					IPAddress address = unicast.Address;
					if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !result.Contains(address))
					{
						result.Add(address);
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
			{
				if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !result.Contains(address))
				{
					result.Add(address);
				}
			}
		}
		catch
		{
		}
		result.Sort(delegate(IPAddress left, IPAddress right)
		{
			return GetAddressPriority(left).CompareTo(GetAddressPriority(right));
		});
		return result;
	}

	private static int GetAddressPriority(IPAddress address)
	{
		byte[] bytes = address.GetAddressBytes();
		if (bytes[0] == 192 && bytes[1] == 168)
		{
			return 0;
		}
		if (bytes[0] == 10)
		{
			return 1;
		}
		if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
		{
			return 2;
		}
		return 3;
	}

	private static List<IPAddress> GetBroadcastAddresses()
	{
		List<IPAddress> result = new List<IPAddress>();
		result.Add(IPAddress.Broadcast);
		foreach (IPAddress address in GetLocalIPv4Addresses())
		{
			byte[] bytes = address.GetAddressBytes();
			IPAddress broadcast = new IPAddress(new byte[] { bytes[0], bytes[1], bytes[2], 255 });
			if (!result.Contains(broadcast))
			{
				result.Add(broadcast);
			}
		}
		return result;
	}

	private static string GetLocalIPv4()
	{
		List<IPAddress> addresses = GetLocalIPv4Addresses();
		return addresses.Count > 0 ? addresses[0].ToString() : "IP no detectada";
	}

	private static string GetLocalIPv4List()
	{
		List<IPAddress> addresses = GetLocalIPv4Addresses();
		if (addresses.Count == 0)
		{
			return "IP no detectada";
		}
		StringBuilder builder = new StringBuilder();
		for (int i = 0; i < addresses.Count && i < 3; i++)
		{
			if (i > 0)
			{
				builder.Append(" / ");
			}
			builder.Append(addresses[i]);
		}
		return builder.ToString();
	}

	private void EnsureStyles()
	{
		if (titleStyle == null)
		{
			lobbyPanelTexture = MakeGuiTexture(new Color(0.14f, 0.09f, 0.05f, 0.97f));
			lobbyButtonTexture = MakeGuiTexture(new Color(0.38f, 0.19f, 0.05f, 1f));
			lobbyFieldTexture = MakeGuiTexture(new Color(0.08f, 0.055f, 0.035f, 1f));
			titleStyle = new GUIStyle(GUI.skin.label);
			titleStyle.alignment = TextAnchor.MiddleCenter;
			titleStyle.fontStyle = FontStyle.Bold;
			titleStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);
			labelStyle = new GUIStyle(GUI.skin.label);
			labelStyle.wordWrap = true;
			labelStyle.normal.textColor = new Color(0.96f, 0.90f, 0.78f);
			buttonStyle = new GUIStyle(GUI.skin.button);
			buttonStyle.normal.background = lobbyButtonTexture;
			buttonStyle.normal.textColor = Color.white;
			buttonStyle.fontStyle = FontStyle.Bold;
			fieldStyle = new GUIStyle(GUI.skin.textField);
			fieldStyle.normal.background = lobbyFieldTexture;
			fieldStyle.normal.textColor = Color.white;
			fieldStyle.alignment = TextAnchor.MiddleCenter;
			boxStyle = new GUIStyle(GUI.skin.box);
			boxStyle.normal.background = lobbyPanelTexture;
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

	private static Texture2D MakeGuiTexture(Color color)
	{
		Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
		texture.hideFlags = HideFlags.DontSave;
		texture.SetPixel(0, 0, color);
		texture.Apply();
		return texture;
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
		float buttonHeight = Mathf.Clamp(safeArea.height * 0.09f, 60f, 100f);
		if (!lobbyVisible)
		{
			return;
		}
		float width = Mathf.Min(safeArea.width * 0.9f, 1000f);
		float height = Mathf.Min(safeArea.height * 0.96f, 900f);
		Rect panel = new Rect(safeArea.x + (safeArea.width - width) * 0.5f, safeArea.y + (safeArea.height - height) * 0.5f, width, height);
		GUI.Box(panel, string.Empty, boxStyle);
		GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, panel.height - 36f));
		GUILayout.Label("Card Wars Online local", titleStyle, GUILayout.Height(buttonHeight));
		GUILayout.Label(StatusText + ((State == ConnectionState.Connected) ? "  Ping: " + PingMilliseconds + " ms" : string.Empty), labelStyle);
		GUILayout.Space(12f);
		lobbyScroll = GUILayout.BeginScrollView(lobbyScroll, false, true, GUILayout.ExpandHeight(true));
		GUILayout.Label("IPs de este móvil: " + GetLocalIPv4List() + "   Puerto: " + Port, labelStyle);
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
		GUILayout.Space(12f);
		GUI.enabled = State == ConnectionState.Disconnected || State == ConnectionState.Error;
		if (GUILayout.Button("Buscar anfitrión automáticamente", buttonStyle, GUILayout.Height(buttonHeight * 0.8f)))
		{
			DiscoverHost();
		}
		GUI.enabled = true;
		GUILayout.Space(12f);
		GUI.enabled = State == ConnectionState.Disconnected || State == ConnectionState.Error || State == ConnectionState.Hosting;
		if (GUILayout.Button("Modo: " + (TurboModeSelected ? "GUERRA TURBO" : "Normal"), buttonStyle, GUILayout.Height(buttonHeight * 0.8f)))
		{
			TurboModeSelected = !TurboModeSelected;
			if (IsHost)
			{
				ApplySelectedBattleMode();
			}
		}
		GUI.enabled = true;
		GUILayout.Label(IsHost || State == ConnectionState.Disconnected || State == ConnectionState.Error ? "El anfitrión elige el modo de juego." : "El modo lo decide el anfitrión.", labelStyle);
		if (State == ConnectionState.Connected && PeerProfile != null)
		{
			GUILayout.Space(12f);
			GUILayout.Label("Rival: " + PeerProfile.playerName + "\nLíder: " + PeerProfile.leaderId + " nivel " + PeerProfile.leaderLevel + "\nMazo recibido: " + PeerProfile.cards.Length + " cartas", labelStyle);
		}
		GUILayout.EndScrollView();
		if (State == ConnectionState.Connected && PeerProfile != null)
		{
			GUILayout.Space(8f);
			GUILayout.Label("Tú: " + (LocalReady ? "LISTO" : "ESPERANDO") + "   Rival: " + (PeerReady ? "LISTO" : "ESPERANDO"), labelStyle);
			GUI.enabled = !matchCommitted;
			if (GUILayout.Button(LocalReady ? "CANCELAR LISTO" : "LISTO · JUGAR ONLINE", buttonStyle, GUILayout.Height(buttonHeight)))
			{
				SetLocalReady(!LocalReady);
			}
			GUI.enabled = true;
		}
		GUILayout.Space(8f);
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
