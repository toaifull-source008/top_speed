using System;
using System.Collections.Generic;
using TopSpeed.Input;
using TopSpeed.Menu;
using TopSpeed.Speech;

namespace TopSpeed.Core.Multiplayer
{
    internal sealed partial class MultiplayerCoordinator
    {
        public void OpenSavedServersManager()
        {
            RebuildSavedServersMenu();
            _menu.Push(MultiplayerSavedServersMenuId);
        }

        private IReadOnlyList<SavedServerEntry> SavedServers => _settings.SavedServers ?? (_settings.SavedServers = new List<SavedServerEntry>());

        private void RebuildSavedServersMenu()
        {
            var items = new List<MenuItem>();
            var servers = SavedServers;
            for (var i = 0; i < servers.Count; i++)
            {
                var index = i;
                var server = servers[i];
                var displayName = string.IsNullOrWhiteSpace(server.Name)
                    ? $"{server.Host}:{ResolveSavedServerPort(server)}"
                    : $"{server.Name}, {server.Host}:{ResolveSavedServerPort(server)}";

                items.Add(new MenuItem(
                    displayName,
                    MenuAction.None,
                    onActivate: () => ConnectUsingSavedServer(index),
                    actions: new[]
                    {
                        new MenuItemAction("Edit", () => OpenEditSavedServerForm(index)),
                        new MenuItemAction("Delete", () => OpenDeleteSavedServerConfirm(index))
                    }));
            }

            items.Add(new MenuItem("Add a new server", MenuAction.None, onActivate: OpenAddSavedServerForm));
            items.Add(new MenuItem("Go back", MenuAction.Back));
            _menu.UpdateItems(MultiplayerSavedServersMenuId, items, preserveSelection: true);
        }

        private void OpenAddSavedServerForm()
        {
            _savedServerEditIndex = -1;
            _savedServerOriginal = null;
            _savedServerDraft = new SavedServerEntry();
            RebuildSavedServerFormMenu();
            _menu.Push(MultiplayerSavedServerFormMenuId);
        }

        private void OpenEditSavedServerForm(int index)
        {
            var servers = SavedServers;
            if (index < 0 || index >= servers.Count)
                return;

            var source = servers[index];
            _savedServerEditIndex = index;
            _savedServerOriginal = CloneSavedServer(source);
            _savedServerDraft = CloneSavedServer(source);
            RebuildSavedServerFormMenu();
            _menu.Push(MultiplayerSavedServerFormMenuId);
        }

        private void ConnectUsingSavedServer(int index)
        {
            var servers = SavedServers;
            if (index < 0 || index >= servers.Count)
                return;

            var server = servers[index];
            var host = (server.Host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                _speech.Speak("Saved server host is empty.");
                return;
            }

            _pendingServerAddress = host;
            _pendingServerPort = ResolveSavedServerPort(server);
            BeginCallSignInput();
        }

        private void UpdateSavedServerDraftName()
        {
            var result = _promptTextInput("Enter the server name.", _savedServerDraft.Name, SpeechService.SpeakFlag.InterruptableButStop, true);
            if (result.Cancelled)
                return;

            _savedServerDraft.Name = (result.Text ?? string.Empty).Trim();
            RebuildSavedServerFormMenu();
        }

        private void UpdateSavedServerDraftHost()
        {
            var result = _promptTextInput("Enter the server IP address or host name.", _savedServerDraft.Host, SpeechService.SpeakFlag.InterruptableButStop, true);
            if (result.Cancelled)
                return;

            _savedServerDraft.Host = (result.Text ?? string.Empty).Trim();
            RebuildSavedServerFormMenu();
        }

        private void UpdateSavedServerDraftPort()
        {
            var current = _savedServerDraft.Port > 0 ? _savedServerDraft.Port.ToString() : string.Empty;
            var result = _promptTextInput("Enter the server port, or leave empty for default.", current, SpeechService.SpeakFlag.InterruptableButStop, true);
            if (result.Cancelled)
                return;

            var trimmed = (result.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _savedServerDraft.Port = 0;
                RebuildSavedServerFormMenu();
                return;
            }

            if (!int.TryParse(trimmed, out var port) || port < 1 || port > 65535)
            {
                _speech.Speak("Invalid port. Enter a number between 1 and 65535.");
                return;
            }

            _savedServerDraft.Port = port;
            RebuildSavedServerFormMenu();
        }

        private void RebuildSavedServerFormMenu()
        {
            var controls = new[]
            {
                new MenuFormControl(
                    () => string.IsNullOrWhiteSpace(_savedServerDraft.Name)
                        ? "Server name, currently empty."
                        : $"Server name, currently set to {_savedServerDraft.Name}",
                    UpdateSavedServerDraftName),
                new MenuFormControl(
                    () => string.IsNullOrWhiteSpace(_savedServerDraft.Host)
                        ? "Server IP or host, currently empty."
                        : $"Server IP or host, currently set to {_savedServerDraft.Host}",
                    UpdateSavedServerDraftHost),
                new MenuFormControl(
                    () => _savedServerDraft.Port > 0
                        ? $"Server port, currently set to {_savedServerDraft.Port}"
                        : "Server port, currently empty.",
                    UpdateSavedServerDraftPort)
            };

            var saveLabel = _savedServerEditIndex >= 0 ? "Save server changes" : "Save server";
            var items = MenuFormBuilder.BuildItems(
                controls,
                saveLabel,
                SaveSavedServerDraft,
                "Go back");
            _menu.UpdateItems(MultiplayerSavedServerFormMenuId, items, preserveSelection: true);
        }

        private void CloseSavedServerForm()
        {
            if (!IsSavedServerDraftDirty())
            {
                _menu.PopToPrevious();
                return;
            }

            _questions.Show(new Question(
                "Save changes before closing?",
                "Are you sure you would like to discard all changes?.",
                HandleSavedServerDiscardQuestionResult,
                new QuestionButton(QuestionId.Confirm, "Save changes", flags: QuestionButtonFlags.Default),
                new QuestionButton(QuestionId.Close, "Discard changes")));
        }

        private bool IsSavedServerDraftDirty()
        {
            var current = NormalizeSavedServerDraft(_savedServerDraft);
            var original = NormalizeSavedServerDraft(_savedServerOriginal ?? new SavedServerEntry());

            if (_savedServerEditIndex < 0)
                return !string.IsNullOrWhiteSpace(current.Host) || !string.IsNullOrWhiteSpace(current.Name) || current.Port != 0;

            return !string.Equals(current.Name, original.Name, StringComparison.Ordinal)
                || !string.Equals(current.Host, original.Host, StringComparison.OrdinalIgnoreCase)
                || current.Port != original.Port;
        }

        private void DiscardSavedServerDraftChanges()
        {
            if (_questions.IsQuestionMenu(_menu.CurrentId))
                _menu.PopToPrevious();
            if (string.Equals(_menu.CurrentId, MultiplayerSavedServerFormMenuId, StringComparison.Ordinal))
                _menu.PopToPrevious();
        }

        private void SaveSavedServerDraft()
        {
            var normalized = NormalizeSavedServerDraft(_savedServerDraft);
            if (string.IsNullOrWhiteSpace(normalized.Host))
            {
                _speech.Speak("Server IP or host cannot be empty.");
                return;
            }

            var servers = _settings.SavedServers ?? (_settings.SavedServers = new List<SavedServerEntry>());
            if (_savedServerEditIndex >= 0 && _savedServerEditIndex < servers.Count)
                servers[_savedServerEditIndex] = normalized;
            else
                servers.Add(normalized);

            _saveSettings();
            RebuildSavedServersMenu();

            if (_questions.IsQuestionMenu(_menu.CurrentId))
                _menu.PopToPrevious();
            if (string.Equals(_menu.CurrentId, MultiplayerSavedServerFormMenuId, StringComparison.Ordinal))
                _menu.PopToPrevious();

            _speech.Speak("Server saved.");
        }

        private void OpenDeleteSavedServerConfirm(int index)
        {
            if (index < 0 || index >= SavedServers.Count)
                return;

            _pendingDeleteServerIndex = index;
            _questions.Show(new Question(
                "Delete this server?",
                "This will remove the saved server entry from the list. Are you sure you would like to continue?",
                HandleDeleteSavedServerQuestionResult,
                new QuestionButton(QuestionId.Yes, "Yes, delete this server"),
                new QuestionButton(QuestionId.No, "No, keep this server", flags: QuestionButtonFlags.Default)));
        }

        private void HandleSavedServerDiscardQuestionResult(int resultId)
        {
            if (resultId == QuestionId.Confirm)
                SaveSavedServerDraft();
            else if (resultId == QuestionId.Close || resultId == QuestionId.Cancel || resultId == QuestionId.No)
                DiscardSavedServerDraftChanges();
        }

        private void HandleDeleteSavedServerQuestionResult(int resultId)
        {
            if (resultId == QuestionId.Yes)
                ConfirmDeleteSavedServer();
        }

        private void ConfirmDeleteSavedServer()
        {
            var servers = _settings.SavedServers ?? (_settings.SavedServers = new List<SavedServerEntry>());
            if (_pendingDeleteServerIndex < 0 || _pendingDeleteServerIndex >= servers.Count)
            {
                if (_questions.IsQuestionMenu(_menu.CurrentId))
                    _menu.PopToPrevious();
                return;
            }

            servers.RemoveAt(_pendingDeleteServerIndex);
            _pendingDeleteServerIndex = -1;
            _saveSettings();
            RebuildSavedServersMenu();
            if (_questions.IsQuestionMenu(_menu.CurrentId))
                _menu.PopToPrevious();
            _speech.Speak("Server deleted.");
        }

        private static SavedServerEntry CloneSavedServer(SavedServerEntry source)
        {
            if (source == null)
                return new SavedServerEntry();

            return new SavedServerEntry
            {
                Name = source.Name ?? string.Empty,
                Host = source.Host ?? string.Empty,
                Port = source.Port
            };
        }

        private static SavedServerEntry NormalizeSavedServerDraft(SavedServerEntry source)
        {
            var copy = CloneSavedServer(source);
            copy.Name = (copy.Name ?? string.Empty).Trim();
            copy.Host = (copy.Host ?? string.Empty).Trim();
            if (copy.Port < 0 || copy.Port > 65535)
                copy.Port = 0;
            return copy;
        }

        private int ResolveSavedServerPort(SavedServerEntry server)
        {
            if (server != null && server.Port >= 1 && server.Port <= 65535)
                return server.Port;
            return ResolveServerPort();
        }

    }
}
