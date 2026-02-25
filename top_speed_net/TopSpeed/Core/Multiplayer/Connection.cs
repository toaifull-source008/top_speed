using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TopSpeed.Audio;
using TopSpeed.Common;
using TopSpeed.Menu;
using TopSpeed.Network;
using TopSpeed.Speech;
using TS.Audio;

namespace TopSpeed.Core.Multiplayer
{
    internal sealed partial class MultiplayerCoordinator
    {
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
            _pingPending = false;
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
                _menu.FadeOutMenuMusic();
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
            else if (string.Equals(fileName, "ping_start.ogg", StringComparison.OrdinalIgnoreCase))
                handle = GetNetworkSound(ref _pingStartSound, fileName);
            else if (string.Equals(fileName, "ping_stop.ogg", StringComparison.OrdinalIgnoreCase))
                handle = GetNetworkSound(ref _pingStopSound, fileName);
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

        private void CheckCurrentPing()
        {
            var session = SessionOrNull();
            if (session == null)
            {
                _speech.Speak("Not connected to a server.");
                return;
            }

            if (_pingPending)
            {
                _speech.Speak("Ping check already in progress.");
                return;
            }

            _pingPending = true;
            _pingStartedAtMs = DateTime.UtcNow.Ticks;
            PlayNetworkSound("ping_start.ogg");
            session.SendPing();
        }

        public void HandlePingReply(long receivedUtcTicks = 0)
        {
            if (!_pingPending)
                return;

            _pingPending = false;
            var endTicks = receivedUtcTicks > 0 ? receivedUtcTicks : DateTime.UtcNow.Ticks;
            var elapsed = TimeSpan.FromTicks(endTicks - _pingStartedAtMs).TotalMilliseconds;
            if (elapsed < 0)
                elapsed = 0;
            PlayNetworkSound("ping_stop.ogg");
            _speech.Speak($"The ping took {(int)Math.Round(elapsed)} milliseconds.");
        }
    }
}
