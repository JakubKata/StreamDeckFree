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

        private const int DeviceColumns = 3;
        private const int DeviceRows = 2;
        private const int LongPressMs = 750;

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
        private readonly Dictionary<int, MacroButton> _buttonsByDeviceId = new Dictionary<int, MacroButton>();
        private readonly HashSet<string> _subscribedButtonGuids = new HashSet<string>();
        private readonly Dictionary<int, PressInfo> _pressedButtons = new Dictionary<int, PressInfo>();
        private readonly Dictionary<int, string> _sentFrameHashes = new Dictionary<int, string>();
        private readonly Dictionary<string, CancellationTokenSource> _buttonRefreshDebounce = new Dictionary<string, CancellationTokenSource>();

        private CydDevice _device;
        private MacroDeckProfile _profile;
        private MacroFolder _currentFolder;
        private CancellationTokenSource _refreshDebounce;

        public override bool CanConfigure => true;

        public override void Enable()
        {
            try
            {
                MacroDeckLogger.Info(this, "Starting Stream Deck Free CYD mirror v5...");

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
                CancellationTokenSource old = _refreshDebounce;
                old?.Cancel();
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

            await _sendSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
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
                }

                if (folder == null)
                {
                    MacroDeckLogger.Warning(this, $"Profile '{profile.DisplayName}' has no folder to mirror");
                    return;
                }

                MacroDeckLogger.Info(this, $"Mirroring profile '{profile.DisplayName}', folder '{folder.DisplayName}', fixed grid {DeviceColumns}x{DeviceRows}, transport RAW RGB565 at {CydDevice.BaudRate} baud");

                bool gridOk = await _device.SetGridAsync((byte)DeviceColumns, (byte)DeviceRows).ConfigureAwait(false);
                if (!gridOk)
                {
                    MacroDeckLogger.Warning(this, "ESP32 did not accept grid command; profile refresh aborted");
                    return;
                }

                lock (_sync)
                {
                    _sentFrameHashes.Clear();
                }

                Dictionary<int, MacroButton> newMap = new Dictionary<int, MacroButton>();
                int buttonWidth = GetButtonWidth();
                int buttonHeight = GetButtonHeight();

                for (int row = 0; row < DeviceRows; row++)
                {
                    for (int col = 0; col < DeviceColumns; col++)
                    {
                        int deviceId = row * DeviceColumns + col;
                        MacroButton button = ProfileManager.FindActionButton(folder, row, col);

                        if (button == null)
                        {
                            continue;
                        }

                        newMap[deviceId] = button;
                        SubscribeToButton(button);

                        bool ok = await SendButtonVisualAsync(deviceId, button, buttonWidth, buttonHeight).ConfigureAwait(false);
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
            finally
            {
                _sendSemaphore.Release();
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
                ScheduleSingleButtonRefresh(button, 120);
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

        private void ScheduleSingleButtonRefresh(MacroButton button, int delayMs = 250)
        {
            if (button == null || string.IsNullOrWhiteSpace(button.Guid))
            {
                return;
            }

            CancellationTokenSource old = null;
            CancellationTokenSource cts = new CancellationTokenSource();
            string key = button.Guid;

            lock (_sync)
            {
                if (_buttonRefreshDebounce.TryGetValue(key, out old))
                {
                    old.Cancel();
                    _buttonRefreshDebounce.Remove(key);
                }

                _buttonRefreshDebounce[key] = cts;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
                    await RefreshSingleButtonAsync(button, cts.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    MacroDeckLogger.Warning(this, $"Single button refresh failed: {ex.Message}");
                }
                finally
                {
                    lock (_sync)
                    {
                        if (_buttonRefreshDebounce.TryGetValue(key, out CancellationTokenSource current) && ReferenceEquals(current, cts))
                        {
                            _buttonRefreshDebounce.Remove(key);
                        }
                    }

                    cts.Dispose();
                }
            });
        }

        private async Task RefreshSingleButtonAsync(MacroButton button, CancellationToken token)
        {
            if (_device == null || !_device.IsConnected || button == null)
            {
                return;
            }

            int deviceId = -1;
            lock (_sync)
            {
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

            await _sendSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                await SendButtonVisualAsync(deviceId, button, GetButtonWidth(), GetButtonHeight()).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        private async Task<bool> SendButtonVisualAsync(int deviceId, MacroButton button, int width, int height)
        {
            if (button == null)
            {
                return true;
            }

            Rgb565Image frame;
            try
            {
                frame = ImageEncoder.RenderButtonRgb565(button, width, height);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Warning(this, $"Render failed for button {deviceId}: {ex.Message}");
                frame = ImageEncoder.RenderErrorRgb565(width, height, "ERR");
            }

            string hash = ComputeFrameHash("raw", frame.Width, frame.Height, frame.Bytes);
            if (IsFrameAlreadySent(deviceId, hash))
            {
                return true;
            }

            bool ok = await _device.SendRgb565ImageAsync((byte)deviceId, frame.Width, frame.Height, frame.Bytes).ConfigureAwait(false);
            if (ok)
            {
                MarkFrameSent(deviceId, hash);
            }

            return ok;
        }

        private bool IsFrameAlreadySent(int deviceId, string hash)
        {
            lock (_sync)
            {
                return _sentFrameHashes.TryGetValue(deviceId, out string oldHash) && oldHash == hash;
            }
        }

        private void MarkFrameSent(int deviceId, string hash)
        {
            lock (_sync)
            {
                _sentFrameHashes[deviceId] = hash;
            }
        }

        private static string ComputeFrameHash(string prefix, int width, int height, byte[] bytes)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;

                hash ^= (uint)width;
                hash *= prime;
                hash ^= (uint)height;
                hash *= prime;

                if (bytes != null)
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        hash ^= bytes[i];
                        hash *= prime;
                    }
                }

                return prefix + ":" + width + "x" + height + ":" + hash.ToString("X16");
            }
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
            PressInfo pressInfo = new PressInfo(button, longPressCts);

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
                    }
                }
                catch (TaskCanceledException)
                {
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

            bool folderChanged;
            if (pressInfo != null && pressInfo.LongPressTriggered)
            {
                TriggerActionList(button.ActionsLongPressRelease, button, out folderChanged);
            }
            else
            {
                TriggerActionList(button.ActionsRelease, button, out folderChanged);
            }

            if (folderChanged)
            {
                ScheduleFullRefresh(50);
            }
            else
            {
                ScheduleSingleButtonRefresh(button, 300);
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

        private static int GetButtonWidth()
        {
            return Math.Max(8, (ScreenWidth - (2 * GridMarginX) - ((DeviceColumns - 1) * GridGapX)) / DeviceColumns);
        }

        private static int GetButtonHeight()
        {
            return Math.Max(8, (ScreenHeight - (2 * GridMarginY) - ((DeviceRows - 1) * GridGapY)) / DeviceRows);
        }

        private sealed class PressInfo
        {
            public MacroButton Button { get; }
            public CancellationTokenSource Cancellation { get; }
            public bool LongPressTriggered { get; set; }

            public PressInfo(MacroButton button, CancellationTokenSource cancellation)
            {
                Button = button;
                Cancellation = cancellation;
            }
        }
    }
}
