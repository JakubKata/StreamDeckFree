using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Variables;
using MacroButton = SuchByte.MacroDeck.ActionButton.ActionButton;
using MacroFolder = SuchByte.MacroDeck.Folders.MacroDeckFolder;

namespace StreamDeckFree
{
    public class StreamDeckFreePlugin : MacroDeckPlugin
    {
        private const int ScreenWidth = 320;
        private const int ScreenHeight = 240;
        private const int GridMarginX = 2;
        private const int GridMarginY = 2;
        private const int GridGapX = 3;
        private const int GridGapY = 3;
        private const int MaxColumns = 8;
        private const int MaxRows = 6;
        private const int MaxButtons = 64;
        private const int LongPressMs = 750;

        private readonly object _sync = new object();
        private readonly Dictionary<int, MacroButton> _buttonsByDeviceId = new Dictionary<int, MacroButton>();
        private readonly HashSet<string> _subscribedButtonGuids = new HashSet<string>();
        private readonly Dictionary<int, PressInfo> _pressedButtons = new Dictionary<int, PressInfo>();

        private CydDevice _device;
        private MacroDeckProfile _profile;
        private MacroFolder _currentFolder;
        private int _columns = 5;
        private int _rows = 3;
        private CancellationTokenSource _refreshDebounce;

        public override bool CanConfigure => true;

        public override void Enable()
        {
            try
            {
                MacroDeckLogger.Info(this, "Starting Stream Deck Free CYD mirror...");

                _device = new CydDevice(this);
                _device.ButtonEvent += HandleDeviceButtonEvent;

                string targetPort = PluginConfiguration.GetValue(this, "CydPort");
                if (string.IsNullOrWhiteSpace(targetPort))
                {
                    MacroDeckLogger.Warning(this, "COM port is not configured. Open plugin settings and select the ESP32 COM port.");
                    return;
                }

                if (!_device.Connect(targetPort))
                {
                    MacroDeckLogger.Error(this, $"Could not connect to CYD on {targetPort}");
                    return;
                }

                ProfileManager.ProfilesSaved += HandleProfilesSaved;

                ScheduleFullRefresh(700);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(this, $"CRITICAL STARTUP ERROR: {ex.Message} | {ex.StackTrace}");
            }
        }

        public override void OpenConfigurator()
        {
            using (var configWindow = new ConfigWindow(this))
            {
                configWindow.ShowDialog();
            }
        }

        private void HandleProfilesSaved(object sender, EventArgs e)
        {
            ScheduleFullRefresh(300);
        }

        private void ScheduleFullRefresh(int delayMs = 150)
        {
            try
            {
                _refreshDebounce?.Cancel();
                _refreshDebounce = new CancellationTokenSource();
                CancellationToken token = _refreshDebounce.Token;

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                        if (!token.IsCancellationRequested)
                        {
                            await RefreshFullProfileAsync().ConfigureAwait(false);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // ignored
                    }
                    catch (Exception ex)
                    {
                        MacroDeckLogger.Warning(this, $"Refresh failed: {ex.Message}");
                    }
                }, token);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(this, $"Could not schedule refresh: {ex.Message}");
            }
        }

        private async Task RefreshFullProfileAsync()
        {
            if (_device == null || !_device.IsConnected)
            {
                return;
            }

            MacroDeckProfile profile = ProfileManager.CurrentProfile ?? ProfileManager.Profiles.FirstOrDefault();
            if (profile == null)
            {
                MacroDeckLogger.Warning(this, "No Macro Deck profile found");
                return;
            }

            MacroFolder folder;
            lock (_sync)
            {
                _profile = profile;
                folder = ResolveCurrentFolder(profile);
                _currentFolder = folder;
                ResolveGrid(profile);
            }

            if (folder == null)
            {
                MacroDeckLogger.Warning(this, $"Profile '{profile.DisplayName}' has no folder to mirror");
                return;
            }

            MacroDeckLogger.Info(this, $"Mirroring profile '{profile.DisplayName}', folder '{folder.DisplayName}', grid {_columns}x{_rows}, transport RAW RGB565");

            bool gridOk = await _device.SetGridAsync((byte)_columns, (byte)_rows).ConfigureAwait(false);
            if (!gridOk)
            {
                MacroDeckLogger.Warning(this, "ESP32 did not accept grid command; profile refresh aborted");
                return;
            }

            Dictionary<int, MacroButton> newMap = new Dictionary<int, MacroButton>();
            int buttonWidth = GetButtonWidth(_columns);
            int buttonHeight = GetButtonHeight(_rows);

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    int deviceId = row * _columns + col;
                    MacroButton button = ProfileManager.FindActionButton(folder, row, col);
                    Rgb565Image frame;

                    if (button != null)
                    {
                        newMap[deviceId] = button;
                        SubscribeToButton(button);
                        frame = ImageEncoder.RenderButtonRgb565(button, buttonWidth, buttonHeight);
                    }
                    else
                    {
                        frame = ImageEncoder.RenderEmptyRgb565(buttonWidth, buttonHeight);
                    }

                    bool ok = await _device.SendRgb565ImageAsync((byte)deviceId, frame.Width, frame.Height, frame.Bytes).ConfigureAwait(false);
                    if (!ok)
                    {
                        MacroDeckLogger.Warning(this, $"Failed to send RAW RGB565 button {deviceId} ({row},{col}) to ESP32");
                    }
                }
            }

            lock (_sync)
            {
                _buttonsByDeviceId.Clear();
                foreach (var pair in newMap)
                {
                    _buttonsByDeviceId[pair.Key] = pair.Value;
                }
            }
        }

        private MacroFolder ResolveCurrentFolder(MacroDeckProfile profile)
        {
            if (profile == null)
            {
                return null;
            }

            if (_currentFolder != null && profile.Folders != null && profile.Folders.Any(f => f.FolderId == _currentFolder.FolderId))
            {
                return profile.Folders.First(f => f.FolderId == _currentFolder.FolderId);
            }

            return profile.Folders?.FirstOrDefault(f => f.IsRootFolder) ?? profile.Folders?.FirstOrDefault();
        }

        private void ResolveGrid(MacroDeckProfile profile)
        {
            int columns = profile.Columns <= 0 ? 5 : profile.Columns;
            int rows = profile.Rows <= 0 ? 3 : profile.Rows;

            columns = Math.Max(1, Math.Min(MaxColumns, columns));
            rows = Math.Max(1, Math.Min(MaxRows, rows));

            while (columns * rows > MaxButtons && rows > 1)
            {
                rows--;
            }

            _columns = columns;
            _rows = rows;
        }

        private void SubscribeToButton(MacroButton button)
        {
            if (button == null || string.IsNullOrWhiteSpace(button.Guid))
            {
                return;
            }

            lock (_sync)
            {
                if (_subscribedButtonGuids.Contains(button.Guid))
                {
                    return;
                }

                _subscribedButtonGuids.Add(button.Guid);
            }

            button.StateChanged += HandleButtonVisualChanged;
            button.IconChanged += HandleButtonVisualChanged;

            if (button.LabelOff != null)
            {
                button.LabelOff.LabelBase64Changed += HandleAnyLabelChanged;
            }

            if (button.LabelOn != null)
            {
                button.LabelOn.LabelBase64Changed += HandleAnyLabelChanged;
            }
        }

        private void HandleButtonVisualChanged(object sender, EventArgs e)
        {
            if (sender is MacroButton button)
            {
                ScheduleSingleButtonRefresh(button);
            }
            else
            {
                ScheduleFullRefresh(150);
            }
        }

        private void HandleAnyLabelChanged(object sender, EventArgs e)
        {
            ScheduleFullRefresh(150);
        }

        private void ScheduleSingleButtonRefresh(MacroButton button)
        {
            if (button == null)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(80).ConfigureAwait(false);
                    await RefreshSingleButtonAsync(button).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Warning(this, $"Single button refresh failed: {ex.Message}");
                }
            });
        }

        private async Task RefreshSingleButtonAsync(MacroButton button)
        {
            if (_device == null || !_device.IsConnected || button == null)
            {
                return;
            }

            int deviceId = -1;
            int columns;
            int rows;

            lock (_sync)
            {
                columns = _columns;
                rows = _rows;
                foreach (var pair in _buttonsByDeviceId)
                {
                    if (pair.Value.Guid == button.Guid)
                    {
                        deviceId = pair.Key;
                        break;
                    }
                }
            }

            if (deviceId < 0)
            {
                return;
            }

            Rgb565Image frame = ImageEncoder.RenderButtonRgb565(button, GetButtonWidth(columns), GetButtonHeight(rows));
            await _device.SendRgb565ImageAsync((byte)deviceId, frame.Width, frame.Height, frame.Bytes).ConfigureAwait(false);
        }

        private void HandleDeviceButtonEvent(object sender, CydButtonEventArgs e)
        {
            Task.Run(() => HandleTouchEventAsync(e));
        }

        private async Task HandleTouchEventAsync(CydButtonEventArgs e)
        {
            MacroButton button;
            lock (_sync)
            {
                _buttonsByDeviceId.TryGetValue(e.ButtonId, out button);
            }

            VariableManager.SetValue("CYD_PRESSED_BUTTON", e.ButtonId, VariableType.Integer, this, Array.Empty<string>());
            VariableManager.SetValue("CYD_BUTTON_EVENT", e.Pressed ? "press" : "release", VariableType.String, this, new[] { "press", "release" });

            if (button == null)
            {
                return;
            }

            if (e.Pressed)
            {
                await HandleButtonDownAsync(e.ButtonId, button).ConfigureAwait(false);
            }
            else
            {
                await HandleButtonUpAsync(e.ButtonId, button).ConfigureAwait(false);
            }
        }

        private Task HandleButtonDownAsync(int deviceId, MacroButton button)
        {
            MacroDeckLogger.Info(this, $"CYD press: deviceId={deviceId}, MacroDeck=({button.Position_X},{button.Position_Y})");

            CancellationTokenSource longPressCts = new CancellationTokenSource();
            PressInfo pressInfo = new PressInfo(button, DateTime.UtcNow, longPressCts);

            lock (_sync)
            {
                if (_pressedButtons.TryGetValue(deviceId, out PressInfo oldPress))
                {
                    oldPress.Cancellation.Cancel();
                }
                _pressedButtons[deviceId] = pressInfo;
            }

            TriggerActionList(button.Actions, button, out bool folderChanged);
            if (folderChanged)
            {
                ScheduleFullRefresh(50);
            }
            else
            {
                ScheduleSingleButtonRefresh(button);
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(LongPressMs, longPressCts.Token).ConfigureAwait(false);

                    bool stillPressed;
                    lock (_sync)
                    {
                        stillPressed = _pressedButtons.TryGetValue(deviceId, out PressInfo current) && ReferenceEquals(current, pressInfo);
                        if (stillPressed)
                        {
                            pressInfo.LongPressTriggered = true;
                        }
                    }

                    if (stillPressed)
                    {
                        TriggerActionList(button.ActionsLongPress, button, out bool longFolderChanged);
                        if (longFolderChanged)
                        {
                            ScheduleFullRefresh(50);
                        }
                        else
                        {
                            ScheduleSingleButtonRefresh(button);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // normal release before long press threshold
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Warning(this, $"Long press handling failed: {ex.Message}");
                }
            });

            return Task.CompletedTask;
        }

        private Task HandleButtonUpAsync(int deviceId, MacroButton fallbackButton)
        {
            PressInfo pressInfo = null;

            lock (_sync)
            {
                if (_pressedButtons.TryGetValue(deviceId, out pressInfo))
                {
                    _pressedButtons.Remove(deviceId);
                }
            }

            MacroButton button = pressInfo?.Button ?? fallbackButton;
            pressInfo?.Cancellation.Cancel();

            if (button == null)
            {
                return Task.CompletedTask;
            }

            if (pressInfo != null && pressInfo.LongPressTriggered)
            {
                TriggerActionList(button.ActionsLongPressRelease, button, out bool longReleaseFolderChanged);
                if (longReleaseFolderChanged)
                {
                    ScheduleFullRefresh(50);
                }
                else
                {
                    ScheduleSingleButtonRefresh(button);
                }
            }
            else
            {
                TriggerActionList(button.ActionsRelease, button, out bool releaseFolderChanged);
                if (releaseFolderChanged)
                {
                    ScheduleFullRefresh(50);
                }
                else
                {
                    ScheduleSingleButtonRefresh(button);
                }
            }

            return Task.CompletedTask;
        }

        private void TriggerActionList(IEnumerable<PluginAction> actions, MacroButton actionButton, out bool folderChanged)
        {
            folderChanged = false;
            if (actions == null || actionButton == null)
            {
                return;
            }

            foreach (PluginAction action in actions.ToArray())
            {
                if (action == null)
                {
                    continue;
                }

                try
                {
                    // Empty clientId is treated by Macro Deck's own folder actions as the software client.
                    // It also works for normal actions which ignore clientId.
                    action.Trigger(string.Empty, actionButton);

                    if (ApplyLocalFolderAction(action))
                    {
                        folderChanged = true;
                    }
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Error(this, $"Action '{action.Name}' failed: {ex.Message}");
                }
            }
        }

        private bool ApplyLocalFolderAction(PluginAction action)
        {
            try
            {
                if (action == null)
                {
                    return false;
                }

                MacroDeckProfile profile;
                MacroFolder current;
                lock (_sync)
                {
                    profile = _profile ?? ProfileManager.CurrentProfile;
                    current = _currentFolder;
                }

                if (profile == null)
                {
                    return false;
                }

                string typeName = action.GetType().Name;
                MacroFolder target = null;

                if (typeName == "FolderSwitcher")
                {
                    target = ProfileManager.FindFolderById(action.Configuration, profile);
                }
                else if (typeName == "GoToParentFolder")
                {
                    target = current != null ? ProfileManager.FindParentFolder(current, profile) : null;
                }
                else if (typeName == "GoToRootFolder")
                {
                    target = profile.Folders?.FirstOrDefault(folder => folder.IsRootFolder);
                }

                if (target == null)
                {
                    return false;
                }

                lock (_sync)
                {
                    _currentFolder = target;
                }

                MacroDeckLogger.Info(this, $"CYD local folder changed to '{target.DisplayName}'");
                return true;
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(this, $"Local folder action failed: {ex.Message}");
                return false;
            }
        }

        private static int GetButtonWidth(int columns)
        {
            columns = Math.Max(1, columns);
            return Math.Max(8, (ScreenWidth - (2 * GridMarginX) - ((columns - 1) * GridGapX)) / columns);
        }

        private static int GetButtonHeight(int rows)
        {
            rows = Math.Max(1, rows);
            return Math.Max(8, (ScreenHeight - (2 * GridMarginY) - ((rows - 1) * GridGapY)) / rows);
        }

        private sealed class PressInfo
        {
            public MacroButton Button { get; }
            public DateTime StartedUtc { get; }
            public CancellationTokenSource Cancellation { get; }
            public bool LongPressTriggered { get; set; }

            public PressInfo(MacroButton button, DateTime startedUtc, CancellationTokenSource cancellation)
            {
                Button = button;
                StartedUtc = startedUtc;
                Cancellation = cancellation;
            }
        }
    }
}
