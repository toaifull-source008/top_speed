using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Audio;
using TopSpeed.Common;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Protocol;
using TopSpeed.Speech;
using TopSpeed.Windowing;
using TS.Audio;

namespace TopSpeed.Core
{
    internal sealed class MultiplayerCoordinator
    {
        private const string MultiplayerLobbyMenuId = "multiplayer_lobby";
        private const string MultiplayerRoomControlsMenuId = "multiplayer_room_controls";
        private const string MultiplayerRoomOptionsMenuId = "multiplayer_room_options";
        private const string MultiplayerRoomBrowserMenuId = "multiplayer_rooms";
        private const string MultiplayerCreateRoomMenuId = "multiplayer_create_room";
        private const string MultiplayerLeaveRoomConfirmMenuId = "multiplayer_leave_room_confirm";
        private const string MultiplayerLoadoutVehicleMenuId = "multiplayer_loadout_vehicle";
        private const string MultiplayerLoadoutTransmissionMenuId = "multiplayer_loadout_transmission";
        private static readonly string[] RoomTypeOptions = { "Race with bots", "One-on-one without bots" };
        private static readonly string[] PlayerCountOptions = BuildNumericOptions(1, ProtocolConstants.MaxRoomPlayersToStart, "players");
        private static readonly string[] LapCountOptions = BuildNumericOptions(1, 16, "laps");
        private static readonly TrackInfo[] RoomTrackOptions = BuildRoomTrackOptions();
        private static readonly string[] RoomTrackLabels = BuildRoomTrackLabels();
        private const int ConnectingPulseIntervalMs = 500;

        private readonly MenuManager _menu;
        private readonly AudioManager _audio;
        private readonly SpeechService _speech;
        private readonly RaceSettings _settings;
        private readonly MultiplayerConnector _connector;
        private readonly Func<string, string?, SpeechService.SpeakFlag, bool, TextInputResult> _promptTextInput;
        private readonly Action _saveSettings;
        private readonly Action _enterMenuState;
        private readonly Action<MultiplayerSession> _setSession;
        private readonly Func<MultiplayerSession?> _getSession;
        private readonly Action _clearSession;
        private readonly Action _resetPendingState;
        private readonly Action<int, bool> _setLocalMultiplayerLoadout;

        private Task<IReadOnlyList<ServerInfo>>? _discoveryTask;
        private CancellationTokenSource? _discoveryCts;
        private Task<ConnectResult>? _connectTask;
        private CancellationTokenSource? _connectCts;
        private string _pendingServerAddress = string.Empty;
        private int _pendingServerPort;
        private string _pendingCallSign = string.Empty;

        private PacketRoomList _roomList = new PacketRoomList();
        private PacketRoomState _roomState = new PacketRoomState { InRoom = false, Players = Array.Empty<PacketRoomPlayer>() };
        private bool _wasInRoom;
        private uint _lastRoomId;
        private bool _wasHost;
        private bool _roomBrowserOpenPending;
        private GameRoomType _createRoomType = GameRoomType.BotsRace;
        private byte _createRoomPlayersToStart = 2;
        private string _createRoomName = string.Empty;
        private int _pendingLoadoutVehicleIndex;
        private CancellationTokenSource? _connectingSoundCts;
        private AudioSourceHandle? _connectingSound;
        private AudioSourceHandle? _connectedSound;
        private AudioSourceHandle? _onlineSound;
        private AudioSourceHandle? _offlineSound;

        public MultiplayerCoordinator(
            MenuManager menu,
            AudioManager audio,
            SpeechService speech,
            RaceSettings settings,
            MultiplayerConnector connector,
            Func<string, string?, SpeechService.SpeakFlag, bool, TextInputResult> promptTextInput,
            Action saveSettings,
            Action enterMenuState,
            Action<MultiplayerSession> setSession,
            Func<MultiplayerSession?> getSession,
            Action clearSession,
            Action resetPendingState,
            Action<int, bool> setLocalMultiplayerLoadout)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _speech = speech ?? throw new ArgumentNullException(nameof(speech));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
            _promptTextInput = promptTextInput ?? throw new ArgumentNullException(nameof(promptTextInput));
            _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _enterMenuState = enterMenuState ?? throw new ArgumentNullException(nameof(enterMenuState));
            _setSession = setSession ?? throw new ArgumentNullException(nameof(setSession));
            _getSession = getSession ?? throw new ArgumentNullException(nameof(getSession));
            _clearSession = clearSession ?? throw new ArgumentNullException(nameof(clearSession));
            _resetPendingState = resetPendingState ?? throw new ArgumentNullException(nameof(resetPendingState));
            _setLocalMultiplayerLoadout = setLocalMultiplayerLoadout ?? throw new ArgumentNullException(nameof(setLocalMultiplayerLoadout));
        }

        public bool UpdatePendingOperations()
        {
            if (_connectTask != null)
            {
                if (!_connectTask.IsCompleted)
                    return true;

                var result = _connectTask.IsFaulted || _connectTask.IsCanceled
                    ? ConnectResult.CreateFail("Connection attempt failed.")
                    : _connectTask.GetAwaiter().GetResult();
                _connectTask = null;
                _connectCts?.Dispose();
                _connectCts = null;
                HandleConnectResult(result);
                return false;
            }

            if (_discoveryTask != null)
            {
                if (!_discoveryTask.IsCompleted)
                    return true;

                IReadOnlyList<ServerInfo> servers;
                if (_discoveryTask.IsFaulted || _discoveryTask.IsCanceled)
                    servers = Array.Empty<ServerInfo>();
                else
                    servers = _discoveryTask.GetAwaiter().GetResult();

                _discoveryTask = null;
                _discoveryCts?.Dispose();
                _discoveryCts = null;
                HandleDiscoveryResult(servers);
                return false;
            }

            return false;
        }

        public void OnSessionCleared()
        {
            StopConnectingPulse();
            _roomList = new PacketRoomList();
            _roomState = new PacketRoomState { InRoom = false, Players = Array.Empty<PacketRoomPlayer>() };
            _wasInRoom = false;
            _wasHost = false;
            _lastRoomId = 0;
            _roomBrowserOpenPending = false;
            ResetCreateRoomDraft();
            _pendingLoadoutVehicleIndex = 0;
            RebuildLobbyMenu();
            RebuildCreateRoomMenu();
            RebuildRoomControlsMenu();
            RebuildRoomOptionsMenu();
            RebuildLeaveRoomConfirmMenu();
            RebuildLoadoutVehicleMenu();
            RebuildLoadoutTransmissionMenu();
            UpdateRoomBrowserMenu();
        }

        public void HandleRoomList(PacketRoomList roomList)
        {
            _roomList = roomList ?? new PacketRoomList();
            if (!_roomBrowserOpenPending)
                return;

            _roomBrowserOpenPending = false;
            if (!string.Equals(_menu.CurrentId, MultiplayerLobbyMenuId, StringComparison.Ordinal))
                return;

            UpdateRoomBrowserMenu();
            _menu.Push(MultiplayerRoomBrowserMenuId);
        }

        public void HandleRoomState(PacketRoomState roomState)
        {
            var wasInRoom = _wasInRoom;
            var previousRoomId = _lastRoomId;
            var previousIsHost = _wasHost;
            var previousRoomType = _roomState.RoomType;
            _roomState = roomState ?? new PacketRoomState { InRoom = false, Players = Array.Empty<PacketRoomPlayer>() };

            if (_roomState.InRoom)
            {
                if (!wasInRoom || previousRoomId != _roomState.RoomId)
                {
                    var roomName = string.IsNullOrWhiteSpace(_roomState.RoomName) ? "game room" : _roomState.RoomName;
                    _speech.Speak($"Joined {roomName}.");
                }

                if (_roomState.IsHost && (!_wasHost || !wasInRoom))
                    _speech.Speak("You are now host of this game.");
            }
            else if (wasInRoom)
            {
                _speech.Speak("You left the game room.");
            }

            _wasInRoom = _roomState.InRoom;
            _lastRoomId = _roomState.RoomId;
            _wasHost = _roomState.IsHost;

            if (_roomState.InRoom && (!wasInRoom || previousRoomId != _roomState.RoomId))
            {
                _menu.ShowRoot(MultiplayerRoomControlsMenuId);
            }
            else if (!_roomState.InRoom && wasInRoom)
            {
                _menu.ShowRoot(MultiplayerLobbyMenuId);
            }

            var roomControlsChanged =
                wasInRoom != _roomState.InRoom ||
                previousIsHost != _roomState.IsHost ||
                previousRoomType != _roomState.RoomType;
            if (roomControlsChanged)
            {
                RebuildRoomControlsMenu();
                RebuildRoomOptionsMenu();
            }
        }

        public void HandleRoomEvent(PacketRoomEvent roomEvent)
        {
            if (roomEvent == null)
                return;

            ApplyRoomListEvent(roomEvent);

            ApplyCurrentRoomEvent(roomEvent, out var beginLoadout, out var localHostChanged);
            if (localHostChanged)
            {
                RebuildRoomControlsMenu();
                RebuildRoomOptionsMenu();
            }

            if (beginLoadout)
                BeginRaceLoadoutSelection();

        }

        public void HandleProtocolMessage(PacketProtocolMessage message)
        {
            if (message == null)
                return;

            if (message.Code == ProtocolMessageCode.ServerPlayerConnected)
                PlayNetworkSound("online.wav");
            else if (message.Code == ProtocolMessageCode.ServerPlayerDisconnected)
                PlayNetworkSound("offline.wav");

            if (!string.IsNullOrWhiteSpace(message.Message))
                _speech.Speak(message.Message);
        }

        private void ApplyRoomListEvent(PacketRoomEvent roomEvent)
        {
            if (roomEvent.Kind == RoomEventKind.None)
                return;

            var rooms = new List<PacketRoomSummary>(_roomList.Rooms ?? Array.Empty<PacketRoomSummary>());
            var index = rooms.FindIndex(r => r.RoomId == roomEvent.RoomId);

            switch (roomEvent.Kind)
            {
                case RoomEventKind.RoomRemoved:
                    if (index >= 0)
                        rooms.RemoveAt(index);
                    break;

                case RoomEventKind.RoomCreated:
                case RoomEventKind.RoomSummaryUpdated:
                case RoomEventKind.RaceStarted:
                case RoomEventKind.RaceStopped:
                case RoomEventKind.ParticipantJoined:
                case RoomEventKind.ParticipantLeft:
                case RoomEventKind.BotAdded:
                case RoomEventKind.BotRemoved:
                case RoomEventKind.PlayersToStartChanged:
                    var summary = new PacketRoomSummary
                    {
                        RoomId = roomEvent.RoomId,
                        RoomName = roomEvent.RoomName ?? string.Empty,
                        RoomType = roomEvent.RoomType,
                        PlayerCount = roomEvent.PlayerCount,
                        PlayersToStart = roomEvent.PlayersToStart,
                        RaceStarted = roomEvent.RaceStarted,
                        TrackName = roomEvent.TrackName ?? string.Empty
                    };
                    if (index >= 0)
                        rooms[index] = summary;
                    else if (roomEvent.Kind != RoomEventKind.RoomSummaryUpdated || roomEvent.RoomId != 0)
                        rooms.Add(summary);
                    break;
            }

            rooms.Sort((a, b) => a.RoomId.CompareTo(b.RoomId));
            _roomList = new PacketRoomList { Rooms = rooms.ToArray() };
        }

        private bool ApplyCurrentRoomEvent(PacketRoomEvent roomEvent, out bool beginLoadout, out bool localHostChanged)
        {
            beginLoadout = false;
            localHostChanged = false;

            if (!_roomState.InRoom || _roomState.RoomId != roomEvent.RoomId)
                return false;

            var previousIsHost = _roomState.IsHost;
            var session = SessionOrNull();

            _roomState.RoomVersion = roomEvent.RoomVersion;
            if (!string.IsNullOrWhiteSpace(roomEvent.RoomName))
                _roomState.RoomName = roomEvent.RoomName;
            _roomState.HostPlayerId = roomEvent.HostPlayerId;
            _roomState.RoomType = roomEvent.RoomType;
            _roomState.PlayersToStart = roomEvent.PlayersToStart;
            _roomState.RaceStarted = roomEvent.RaceStarted;
            _roomState.PreparingRace = roomEvent.PreparingRace;
            _roomState.TrackName = roomEvent.TrackName ?? string.Empty;
            _roomState.Laps = roomEvent.Laps;
            _roomState.IsHost = session != null && roomEvent.HostPlayerId == session.PlayerId;

            switch (roomEvent.Kind)
            {
                case RoomEventKind.ParticipantJoined:
                case RoomEventKind.BotAdded:
                    UpsertCurrentRoomParticipant(roomEvent);
                    break;

                case RoomEventKind.ParticipantLeft:
                case RoomEventKind.BotRemoved:
                    RemoveCurrentRoomParticipant(roomEvent.SubjectPlayerId);
                    break;

                case RoomEventKind.ParticipantStateChanged:
                    UpsertCurrentRoomParticipant(roomEvent);
                    break;

                case RoomEventKind.PrepareStarted:
                    beginLoadout = true;
                    break;
            }

            localHostChanged = previousIsHost != _roomState.IsHost;
            if (localHostChanged && _roomState.IsHost)
                _speech.Speak("You are now host of this game.");

            _wasHost = _roomState.IsHost;
            return true;
        }

        private void UpsertCurrentRoomParticipant(PacketRoomEvent roomEvent)
        {
            if (roomEvent.SubjectPlayerId == 0)
                return;

            var players = new List<PacketRoomPlayer>(_roomState.Players ?? Array.Empty<PacketRoomPlayer>());
            var index = players.FindIndex(p => p.PlayerId == roomEvent.SubjectPlayerId);
            var name = string.IsNullOrWhiteSpace(roomEvent.SubjectPlayerName)
                ? $"Player {roomEvent.SubjectPlayerNumber + 1}"
                : roomEvent.SubjectPlayerName;
            var item = new PacketRoomPlayer
            {
                PlayerId = roomEvent.SubjectPlayerId,
                PlayerNumber = roomEvent.SubjectPlayerNumber,
                State = roomEvent.SubjectPlayerState,
                Name = name
            };

            if (index >= 0)
                players[index] = item;
            else
                players.Add(item);

            players.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            _roomState.Players = players.ToArray();
        }

        private void RemoveCurrentRoomParticipant(uint playerId)
        {
            if (playerId == 0)
                return;

            var players = new List<PacketRoomPlayer>(_roomState.Players ?? Array.Empty<PacketRoomPlayer>());
            var removed = players.RemoveAll(p => p.PlayerId == playerId);
            if (removed == 0)
                return;

            players.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            _roomState.Players = players.ToArray();
        }

        public bool IsInRoom => _roomState.InRoom;

        public bool IsRoomMenu(string? currentMenuId)
        {
            if (!_roomState.InRoom)
                return false;
            return string.Equals(currentMenuId, MultiplayerRoomControlsMenuId, StringComparison.Ordinal);
        }

        public bool TryHandleEscapeFromRoomMenu(string? currentMenuId)
        {
            if (!IsRoomMenu(currentMenuId))
            {
                return false;
            }

            OpenLeaveRoomConfirmation();
            return true;
        }

        public bool TryHandleExitFromRaceLoadoutMenu(string? currentMenuId)
        {
            if (string.Equals(currentMenuId, MultiplayerLoadoutTransmissionMenuId, StringComparison.Ordinal))
            {
                _menu.ShowRoot(MultiplayerLoadoutVehicleMenuId);
                return true;
            }

            if (!string.Equals(currentMenuId, MultiplayerLoadoutVehicleMenuId, StringComparison.Ordinal))
                return false;

            _speech.Speak("Choose your vehicle and transmission mode to get ready for the race.");
            _menu.ShowRoot(MultiplayerLoadoutVehicleMenuId);
            return true;
        }

        public void ShowMultiplayerMenuAfterRace()
        {
            if (_roomState.InRoom)
                _menu.ShowRoot(MultiplayerRoomControlsMenuId);
            else
                _menu.ShowRoot(MultiplayerLobbyMenuId);
        }

        public void BeginRaceLoadoutSelection()
        {
            if (!_roomState.InRoom)
                return;

            _pendingLoadoutVehicleIndex = 0;
            RebuildLoadoutVehicleMenu();
            RebuildLoadoutTransmissionMenu();
            _menu.ShowRoot(MultiplayerLoadoutVehicleMenuId);
            _enterMenuState();
        }

        public void StartServerDiscovery()
        {
            if (_discoveryTask != null && !_discoveryTask.IsCompleted)
                return;

            _speech.Speak("Please wait. Scanning for servers on the local network.");
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            _discoveryCts = new CancellationTokenSource();
            _discoveryTask = Task.Run(async () =>
            {
                using var client = new DiscoveryClient();
                return await client.ScanAsync(ClientProtocol.DefaultDiscoveryPort, TimeSpan.FromSeconds(2), _discoveryCts.Token);
            }, _discoveryCts.Token);
        }

        public void BeginManualServerEntry()
        {
            while (true)
            {
                var result = _promptTextInput("Enter the server IP address or domain.", _settings.LastServerAddress,
                    SpeechService.SpeakFlag.InterruptableButStop, true);
                if (result.Cancelled)
                    return;
                if (HandleServerAddressInput(result.Text))
                    return;
            }
        }

        public void BeginServerPortEntry()
        {
            var current = _settings.ServerPort > 0 ? _settings.ServerPort.ToString() : string.Empty;
            var result = _promptTextInput("Enter a custom server port, or leave empty for default.", current,
                SpeechService.SpeakFlag.None, true);
            if (result.Cancelled)
                return;
            HandleServerPortInput(result.Text);
        }

        private void HandleDiscoveryResult(IReadOnlyList<ServerInfo> servers)
        {
            if (servers == null || servers.Count == 0)
            {
                _speech.Speak("No servers were found on the local network. You can enter an address manually.");
                return;
            }

            var items = new List<MenuItem>();
            foreach (var server in servers)
            {
                var info = server;
                var label = $"{info.Address}:{info.Port}";
                items.Add(new MenuItem(label, MenuAction.None, onActivate: () => SelectDiscoveredServer(info), suppressPostActivateAnnouncement: true));
            }

            items.Add(new MenuItem("Go back", MenuAction.Back));
            _menu.UpdateItems("multiplayer_servers", items);
            _menu.Push("multiplayer_servers");
        }

        private void SelectDiscoveredServer(ServerInfo server)
        {
            _pendingServerAddress = server.Address.ToString();
            _pendingServerPort = server.Port;
            BeginCallSignInput();
        }

        private bool HandleServerAddressInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _speech.Speak("Please enter a server address.");
                return false;
            }

            var host = trimmed;
            int? overridePort = null;
            var lastColon = trimmed.LastIndexOf(':');
            if (lastColon > 0 && lastColon < trimmed.Length - 1)
            {
                var portPart = trimmed.Substring(lastColon + 1);
                if (int.TryParse(portPart, out var parsedPort))
                {
                    host = trimmed.Substring(0, lastColon);
                    overridePort = parsedPort;
                }
            }

            _settings.LastServerAddress = host;
            _saveSettings();
            _pendingServerAddress = host;
            _pendingServerPort = overridePort ?? ResolveServerPort();
            return BeginCallSignInput();
        }

        private bool BeginCallSignInput()
        {
            while (true)
            {
                var result = _promptTextInput("Enter your call sign.", null,
                    SpeechService.SpeakFlag.InterruptableButStop, true);
                if (result.Cancelled)
                    return false;
                if (HandleCallSignInput(result.Text))
                    return true;
            }
        }

        private bool HandleCallSignInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _speech.Speak("Call sign cannot be empty.");
                return false;
            }

            _pendingCallSign = trimmed;
            AttemptConnect(_pendingServerAddress, _pendingServerPort, _pendingCallSign);
            return true;
        }

        private void AttemptConnect(string host, int port, string callSign)
        {
            _speech.Speak("Attempting to connect, please wait...");
            _clearSession();
            StartConnectingPulse();
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();
            _connectTask = _connector.ConnectAsync(host, port, callSign, TimeSpan.FromSeconds(3), _connectCts.Token);
        }

        private void HandleConnectResult(ConnectResult result)
        {
            StopConnectingPulse();
            if (result.Success && result.Session != null)
            {
                var session = result.Session;
                _setSession(session);
                _resetPendingState();

                OnSessionCleared();
                PlayNetworkSound("connected.wav");

                var welcome = "Connected to server.";
                if (!string.IsNullOrWhiteSpace(result.Motd))
                    welcome += $" Message of the day: {result.Motd}.";
                _speech.Speak(welcome);
                _menu.ShowRoot(MultiplayerLobbyMenuId);
                _enterMenuState();
                return;
            }

            _speech.Speak($"Failed to connect: {result.Message}");
            _enterMenuState();
        }

        private void StartConnectingPulse()
        {
            StopConnectingPulse();
            var handle = GetNetworkSound(ref _connectingSound, "connecting.wav");
            if (handle == null)
                return;

            _connectingSoundCts = new CancellationTokenSource();
            var token = _connectingSoundCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        handle.Restart(loop: false);
                    }
                    catch
                    {
                        // Ignore audio errors for network cue.
                    }

                    try
                    {
                        await Task.Delay(ConnectingPulseIntervalMs, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private void StopConnectingPulse()
        {
            _connectingSoundCts?.Cancel();
            _connectingSoundCts?.Dispose();
            _connectingSoundCts = null;
            try
            {
                _connectingSound?.Stop();
            }
            catch
            {
                // Ignore audio stop failures for network cue.
            }
        }

        private void PlayNetworkSound(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            AudioSourceHandle? handle;
            if (string.Equals(fileName, "online.wav", StringComparison.OrdinalIgnoreCase))
                handle = GetNetworkSound(ref _onlineSound, fileName);
            else if (string.Equals(fileName, "offline.wav", StringComparison.OrdinalIgnoreCase))
                handle = GetNetworkSound(ref _offlineSound, fileName);
            else if (string.Equals(fileName, "connecting.wav", StringComparison.OrdinalIgnoreCase))
                handle = GetNetworkSound(ref _connectingSound, fileName);
            else
                handle = GetNetworkSound(ref _connectedSound, fileName);

            if (handle == null)
                return;

            try
            {
                handle.Restart(loop: false);
            }
            catch
            {
                // Ignore audio errors for network cue.
            }
        }

        private AudioSourceHandle? GetNetworkSound(ref AudioSourceHandle? cache, string fileName)
        {
            if (cache != null)
                return cache;

            var path = Path.Combine(AssetPaths.SoundsRoot, "network", fileName ?? string.Empty);
            if (!_audio.TryResolvePath(path, out var fullPath))
                return null;

            try
            {
                cache = _audio.AcquireCachedSource(fullPath, streamFromDisk: false);
                return cache;
            }
            catch
            {
                return null;
            }
        }

        private void HandleServerPortInput(string text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                _settings.ServerPort = 0;
                _saveSettings();
                _speech.Speak("Server port cleared. The default port will be used.");
                return;
            }

            if (!int.TryParse(trimmed, out var port) || port < 1 || port > 65535)
            {
                _speech.Speak("Invalid port. Enter a number between 1 and 65535.");
                BeginServerPortEntry();
                return;
            }

            _settings.ServerPort = port;
            _saveSettings();
            _speech.Speak($"Server port set to {port}.");
        }

        private int ResolveServerPort()
        {
            return _settings.ServerPort > 0 ? _settings.ServerPort : ClientProtocol.DefaultServerPort;
        }

        private MultiplayerSession? SessionOrNull()
        {
            return _getSession();
        }

        private void RebuildLobbyMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Create a new game room", MenuAction.None, onActivate: OpenCreateRoomMenu),
                new MenuItem("Join an existing game", MenuAction.None, onActivate: OpenRoomBrowser),
                new MenuItem("Options", MenuAction.None, nextMenuId: "options_main"),
                new MenuItem("Disconnect from server", MenuAction.None, onActivate: Disconnect)
            };

            _menu.UpdateItems(MultiplayerLobbyMenuId, items);
        }

        private void RebuildCreateRoomMenu()
        {
            var items = new List<MenuItem>
            {
                new RadioButton(
                    "Game type",
                    RoomTypeOptions,
                    GetCreateRoomTypeIndex,
                    SetCreateRoomType,
                    hint: "Choose whether this room is a race with bots or a one-on-one game. Use LEFT or RIGHT to change."),
                new RadioButton(
                    "Maximum players allowed in this room",
                    PlayerCountOptions,
                    GetCreateRoomPlayersToStartIndex,
                    SetCreateRoomPlayersToStart,
                    hint: "Choose the player capacity from 1 to 10. Use LEFT or RIGHT to change."),
                new MenuItem(
                    () => string.IsNullOrWhiteSpace(_createRoomName)
                        ? "Room name, currently automatic"
                        : $"Room name, currently {_createRoomName}",
                    MenuAction.None,
                    onActivate: UpdateCreateRoomName,
                    hint: "Press ENTER to enter a room name. Leave it empty to use an automatic name."),
                new MenuItem("Create this game room", MenuAction.None, onActivate: ConfirmCreateRoom),
                new MenuItem("Cancel room creation", MenuAction.Back)
            };

            _menu.UpdateItems(MultiplayerCreateRoomMenuId, items);
        }

        private void RebuildRoomControlsMenu()
        {
            var items = new List<MenuItem>();
            if (!_roomState.InRoom)
            {
                items.Add(new MenuItem("You are not currently inside a game room.", MenuAction.None));
                items.Add(new MenuItem("Return to multiplayer lobby", MenuAction.None, onActivate: () => _menu.ShowRoot(MultiplayerLobbyMenuId)));
                _menu.UpdateItems(MultiplayerRoomControlsMenuId, items);
                return;
            }

            if (_roomState.IsHost)
                items.Add(new MenuItem("Start this game now", MenuAction.None, onActivate: StartGame));
            if (_roomState.IsHost)
                items.Add(new MenuItem("Change game options", MenuAction.None, nextMenuId: "multiplayer_room_options"));
            if (_roomState.IsHost && _roomState.RoomType == GameRoomType.BotsRace)
                items.Add(new MenuItem("Add a bot to this game room", MenuAction.None, onActivate: AddBotToRoom));
            if (_roomState.IsHost && _roomState.RoomType == GameRoomType.BotsRace)
                items.Add(new MenuItem("Remove the last bot that was added", MenuAction.None, onActivate: RemoveLastBotFromRoom));
            items.Add(new MenuItem("Who is currently present in this game room", MenuAction.None, onActivate: SpeakPresentPlayers));
            items.Add(new MenuItem("Leave this game room", MenuAction.None, onActivate: OpenLeaveRoomConfirmation));
            _menu.UpdateItems(MultiplayerRoomControlsMenuId, items);
        }

        private void RebuildRoomOptionsMenu()
        {
            var items = new List<MenuItem>();
            if (!_roomState.InRoom)
            {
                items.Add(new MenuItem("You are not currently inside a game room.", MenuAction.None));
                items.Add(new MenuItem("Return to room controls", MenuAction.Back));
                _menu.UpdateItems(MultiplayerRoomOptionsMenuId, items);
                return;
            }

            if (!_roomState.IsHost)
            {
                items.Add(new MenuItem("Only the host can change game options.", MenuAction.None));
                items.Add(new MenuItem("Return to room controls", MenuAction.Back));
                _menu.UpdateItems(MultiplayerRoomOptionsMenuId, items);
                return;
            }

            items.Add(new RadioButton(
                "Track",
                RoomTrackLabels,
                GetCurrentRoomTrackIndex,
                SetRoomTrackByIndex,
                hint: "Choose which track this room will use. Use LEFT or RIGHT to change."));

            items.Add(new RadioButton(
                "Number of laps",
                LapCountOptions,
                () => Math.Max(0, Math.Min(LapCountOptions.Length - 1, (_roomState.Laps > 0 ? _roomState.Laps : (byte)1) - 1)),
                value => SetLaps((byte)(value + 1)),
                hint: "Choose the number of laps for this room. Use LEFT or RIGHT to change."));

            items.Add(new RadioButton(
                "Players required before the host can start",
                PlayerCountOptions,
                () => Math.Max(0, Math.Min(PlayerCountOptions.Length - 1, (_roomState.PlayersToStart > 0 ? _roomState.PlayersToStart : (byte)1) - 1)),
                value => SetPlayersToStart((byte)(value + 1)),
                hint: "Select how many players are required before the host can start this game. Use LEFT or RIGHT to change."));

            items.Add(new MenuItem("Return to room controls", MenuAction.Back));
            var preserveSelection = string.Equals(_menu.CurrentId, MultiplayerRoomOptionsMenuId, StringComparison.Ordinal);
            _menu.UpdateItems(MultiplayerRoomOptionsMenuId, items, preserveSelection);
        }

        private void RebuildLeaveRoomConfirmMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Yes, leave this game room", MenuAction.None, onActivate: ConfirmLeaveRoom),
                new MenuItem("No, stay in this game room", MenuAction.Back)
            };
            _menu.UpdateItems(MultiplayerLeaveRoomConfirmMenuId, items);
        }

        private void RebuildLoadoutVehicleMenu()
        {
            var items = new List<MenuItem>();
            for (var i = 0; i < VehicleCatalog.VehicleCount; i++)
            {
                var vehicleIndex = i;
                var vehicleName = VehicleCatalog.Vehicles[i].Name;
                items.Add(new MenuItem(vehicleName, MenuAction.None, nextMenuId: MultiplayerLoadoutTransmissionMenuId, onActivate: () => _pendingLoadoutVehicleIndex = vehicleIndex));
            }

            items.Add(new MenuItem("Random vehicle", MenuAction.None, nextMenuId: MultiplayerLoadoutTransmissionMenuId, onActivate: () => _pendingLoadoutVehicleIndex = Algorithm.RandomInt(VehicleCatalog.VehicleCount)));
            _menu.UpdateItems(MultiplayerLoadoutVehicleMenuId, items);
        }

        private void RebuildLoadoutTransmissionMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Automatic transmission", MenuAction.None, onActivate: () => SubmitLoadoutReady(true)),
                new MenuItem("Manual transmission", MenuAction.None, onActivate: () => SubmitLoadoutReady(false)),
                new MenuItem("Random transmission mode", MenuAction.None, onActivate: () => SubmitLoadoutReady(Algorithm.RandomInt(2) == 0)),
                new MenuItem("Go back to vehicle selection", MenuAction.Back)
            };
            _menu.UpdateItems(MultiplayerLoadoutTransmissionMenuId, items);
        }

        private void OpenRoomBrowser()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (_roomBrowserOpenPending)
                return;

            _roomBrowserOpenPending = true;
            session.SendRoomListRequest();
        }

        private void OpenCreateRoomMenu()
        {
            if (SessionOrNull() == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            ResetCreateRoomDraft();
            RebuildCreateRoomMenu();
            _menu.Push(MultiplayerCreateRoomMenuId);
        }

        private void UpdateCreateRoomName()
        {
            var result = _promptTextInput(
                "Enter a room name. Leave this field empty to use an automatic room name.",
                _createRoomName,
                SpeechService.SpeakFlag.None,
                true);

            if (result.Cancelled)
                return;

            _createRoomName = (result.Text ?? string.Empty).Trim();
            RebuildCreateRoomMenu();

            if (string.IsNullOrWhiteSpace(_createRoomName))
            {
                _speech.Speak("Automatic room name selected.");
                return;
            }

            _speech.Speak($"Room name set to {_createRoomName}.");
        }

        private void ConfirmCreateRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            var playersToStart = _createRoomPlayersToStart;
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                playersToStart = 2;

            session.SendRoomCreate(_createRoomName, _createRoomType, playersToStart);
            _speech.Speak("Creating game room.");
            _menu.ShowRoot(MultiplayerLobbyMenuId);
        }

        private int GetCreateRoomTypeIndex()
        {
            return _createRoomType == GameRoomType.OneOnOne ? 1 : 0;
        }

        private void SetCreateRoomType(int index)
        {
            _createRoomType = index == 1 ? GameRoomType.OneOnOne : GameRoomType.BotsRace;
        }

        private int GetCreateRoomPlayersToStartIndex()
        {
            var playersToStart = _createRoomPlayersToStart;
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                playersToStart = 2;
            return playersToStart - 1;
        }

        private void SetCreateRoomPlayersToStart(int index)
        {
            var playersToStart = (byte)(index + 1);
            if (playersToStart < 1 || playersToStart > ProtocolConstants.MaxRoomPlayersToStart)
                return;
            _createRoomPlayersToStart = playersToStart;
        }

        private void ResetCreateRoomDraft()
        {
            _createRoomType = GameRoomType.BotsRace;
            _createRoomPlayersToStart = 2;
            _createRoomName = string.Empty;
        }

        private void JoinRoom(uint roomId)
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            session.SendRoomJoin(roomId);
        }

        private void OpenLeaveRoomConfirmation()
        {
            if (!_roomState.InRoom)
            {
                _speech.Speak("You are not currently inside a game room.");
                return;
            }

            if (string.Equals(_menu.CurrentId, MultiplayerLeaveRoomConfirmMenuId, StringComparison.Ordinal))
                return;

            _menu.Push(MultiplayerLeaveRoomConfirmMenuId);
        }

        private void ConfirmLeaveRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            session.SendRoomLeave();
            _speech.Speak("Leaving game room.");
            _menu.ShowRoot(MultiplayerLobbyMenuId);
        }

        private void StartGame()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom || !_roomState.IsHost)
            {
                _speech.Speak("Only the host can start the game.");
                return;
            }

            session.SendRoomStartRace();
        }

        private int GetCurrentRoomTrackIndex()
        {
            var currentTrack = string.IsNullOrWhiteSpace(_roomState.TrackName) ? TrackList.RaceTracks[0].Key : _roomState.TrackName;
            for (var i = 0; i < RoomTrackOptions.Length; i++)
            {
                if (string.Equals(RoomTrackOptions[i].Key, currentTrack, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }

        private void SetRoomTrackByIndex(int index)
        {
            var session = SessionOrNull();
            if (session == null)
                return;
            if (!_roomState.InRoom || !_roomState.IsHost)
                return;
            if (index < 0 || index >= RoomTrackOptions.Length)
                return;

            session.SendRoomSetTrack(RoomTrackOptions[index].Key);
        }

        private void SetLaps(byte laps)
        {
            var session = SessionOrNull();
            if (session == null || !_roomState.IsHost || !_roomState.InRoom)
                return;
            if (laps < 1 || laps > 16)
                return;

            session.SendRoomSetLaps(laps);
        }

        private void SetPlayersToStart(byte playersToStart)
        {
            var session = SessionOrNull();
            if (session == null || !_roomState.IsHost || !_roomState.InRoom)
                return;

            session.SendRoomSetPlayersToStart(playersToStart);
        }

        private void AddBotToRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom || !_roomState.IsHost || _roomState.RoomType != GameRoomType.BotsRace)
            {
                _speech.Speak("Bots can only be managed by the host in race-with-bots rooms.");
                return;
            }

            session.SendRoomAddBot();
        }

        private void RemoveLastBotFromRoom()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom || !_roomState.IsHost || _roomState.RoomType != GameRoomType.BotsRace)
            {
                _speech.Speak("Bots can only be managed by the host in race-with-bots rooms.");
                return;
            }

            session.SendRoomRemoveBot();
        }

        private void SubmitLoadoutReady(bool automaticTransmission)
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (!_roomState.InRoom)
            {
                _speech.Speak("You are not in a game room.");
                return;
            }

            var vehicleIndex = Math.Max(0, Math.Min(VehicleCatalog.VehicleCount - 1, _pendingLoadoutVehicleIndex));
            var selectedCar = (CarType)vehicleIndex;
            _setLocalMultiplayerLoadout(vehicleIndex, automaticTransmission);
            session.SendRoomPlayerReady(selectedCar, automaticTransmission);
            _speech.Speak("Ready. Waiting for other players.");
            _menu.ShowRoot(MultiplayerRoomControlsMenuId);
        }

        private void SpeakPresentPlayers()
        {
            if (!_roomState.InRoom)
            {
                _speech.Speak("You are not in a game room.");
                return;
            }

            if (_roomState.Players == null || _roomState.Players.Length == 0)
            {
                _speech.Speak("No players are in this game.");
                return;
            }

            var parts = new List<string>();
            foreach (var player in _roomState.Players)
            {
                var name = string.IsNullOrWhiteSpace(player.Name) ? $"Player {player.PlayerNumber + 1}" : player.Name;
                if (player.PlayerId == _roomState.HostPlayerId)
                    parts.Add($"{name}, host");
                else
                    parts.Add(name);
            }

            _speech.Speak(string.Join(". ", parts));
        }

        private void Disconnect()
        {
            _clearSession();
            _speech.Speak("Disconnected from server.");
            _menu.ShowRoot("main");
            _enterMenuState();
        }

        private void UpdateRoomBrowserMenu()
        {
            var items = new List<MenuItem>();
            var rooms = _roomList.Rooms ?? Array.Empty<PacketRoomSummary>();
            if (rooms.Length == 0)
            {
                items.Add(new MenuItem("No game rooms found", MenuAction.None));
            }
            else
            {
                foreach (var room in rooms)
                {
                    var roomCopy = room;
                    var typeText = roomCopy.RoomType == GameRoomType.OneOnOne ? "one-on-one" : "race with bots";
                    var label = $"{typeText} game with {roomCopy.PlayerCount} people";
                    label += $", maximum {roomCopy.PlayersToStart} players";
                    if (roomCopy.RaceStarted)
                        label += ", in progress";
                    else if (roomCopy.PlayerCount >= roomCopy.PlayersToStart)
                        label += ", room is full";
                    items.Add(new MenuItem(label, MenuAction.None, onActivate: () => JoinRoom(roomCopy.RoomId)));
                }
            }

            items.Add(new MenuItem("Return to multiplayer lobby", MenuAction.Back));
            _menu.UpdateItems(MultiplayerRoomBrowserMenuId, items);
        }

        private static string[] BuildNumericOptions(int min, int max, string suffix)
        {
            if (max < min)
                return Array.Empty<string>();

            var options = new string[max - min + 1];
            for (var i = min; i <= max; i++)
            {
                var index = i - min;
                var unit = i == 1
                    ? suffix.TrimEnd('s')
                    : suffix;
                options[index] = $"{i} {unit}";
            }

            return options;
        }

        private static TrackInfo[] BuildRoomTrackOptions()
        {
            var tracks = new List<TrackInfo>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in TrackList.RaceTracks)
            {
                if (names.Add(track.Display))
                    tracks.Add(track);
            }

            foreach (var track in TrackList.AdventureTracks)
            {
                if (names.Add(track.Display))
                    tracks.Add(track);
            }

            return tracks.ToArray();
        }

        private static string[] BuildRoomTrackLabels()
        {
            if (RoomTrackOptions.Length == 0)
                return new[] { "America" };

            var labels = new string[RoomTrackOptions.Length];
            for (var i = 0; i < RoomTrackOptions.Length; i++)
            {
                var track = RoomTrackOptions[i];
                labels[i] = track.Display;
            }

            return labels;
        }
    }
}
