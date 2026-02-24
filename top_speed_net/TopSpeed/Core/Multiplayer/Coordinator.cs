using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Audio;
using TopSpeed.Data;
using TopSpeed.Input;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Protocol;
using TopSpeed.Speech;
using TopSpeed.Windowing;
using TS.Audio;

namespace TopSpeed.Core.Multiplayer
{
    internal sealed partial class MultiplayerCoordinator
    {
        private const string MultiplayerLobbyMenuId = "multiplayer_lobby";
        private const string MultiplayerRoomControlsMenuId = "multiplayer_room_controls";
        private const string MultiplayerRoomOptionsMenuId = "multiplayer_room_options";
        private const string MultiplayerRoomPlayersMenuId = "multiplayer_room_players";
        private const string MultiplayerRoomBrowserMenuId = "multiplayer_rooms";
        private const string MultiplayerCreateRoomMenuId = "multiplayer_create_room";
        private const string MultiplayerLoadoutVehicleMenuId = "multiplayer_loadout_vehicle";
        private const string MultiplayerLoadoutTransmissionMenuId = "multiplayer_loadout_transmission";
        private const string MultiplayerSavedServersMenuId = "multiplayer_saved_servers";
        private const string MultiplayerSavedServerFormMenuId = "multiplayer_saved_server_form";
        private static readonly string[] RoomTypeOptions = { "Race with bots", "Race without bots", "One-on-one without bots" };
        private static readonly string[] PlayerCountOptions = BuildNumericOptions(1, ProtocolConstants.MaxRoomPlayersToStart, "players");
        private static readonly string[] LapCountOptions = BuildNumericOptions(1, 16, "laps");
        private static readonly TrackInfo[] RoomTrackOptions = BuildRoomTrackOptions();
        private static readonly string[] RoomTrackLabels = BuildRoomTrackLabels();
        private const int ConnectingPulseIntervalMs = 500;

        private readonly MenuManager _menu;
        private readonly QuestionDialog _questions;
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
        private SavedServerEntry _savedServerDraft = new SavedServerEntry();
        private SavedServerEntry? _savedServerOriginal;
        private int _savedServerEditIndex = -1;
        private int _pendingDeleteServerIndex = -1;

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
            _questions = new QuestionDialog(_menu);
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
            _savedServerDraft = new SavedServerEntry();
            _savedServerOriginal = null;
            _savedServerEditIndex = -1;
            _pendingDeleteServerIndex = -1;
            RebuildLobbyMenu();
            RebuildCreateRoomMenu();
            RebuildSavedServersMenu();
            RebuildSavedServerFormMenu();
            RebuildRoomControlsMenu();
            RebuildRoomOptionsMenu();
            RebuildRoomPlayersMenu();
            RebuildLoadoutVehicleMenu();
            RebuildLoadoutTransmissionMenu();
            UpdateRoomBrowserMenu();
        }
    }
}
