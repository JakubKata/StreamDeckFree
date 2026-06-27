using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Variables;
using Image = SixLabors.ImageSharp.Image;
using Color = SixLabors.ImageSharp.Color;

namespace StreamDeckFree
{
    public class StreamDeckFreePlugin : MacroDeckPlugin
    {
        private CydDevice _device;

        public override bool CanConfigure => true;

        public override void Enable()
        {
            try
            {
                MacroDeckLogger.Info(this, "Starting Stream Deck Free...");
                _device = new CydDevice(this);

                _device.OnButtonTapped += HandleButtonTapped;

                string targetPort = PluginConfiguration.GetValue(this, "CydPort");

                if (!string.IsNullOrEmpty(targetPort))
                {
                    if (_device.Connect(targetPort))
                    {
                        InitializeScreenSafeAsync();
                    }
                }
                else
                {
                    MacroDeckLogger.Warning(this, "COM port is not configured. Please open plugin settings.");
                }
            }
            catch (System.Exception ex)
            {
                MacroDeckLogger.Error(this, $"CRITICAL STARTUP ERROR: {ex.Message} | {ex.StackTrace}");
            }
        }

        private async void InitializeScreenSafeAsync()
        {
            await Task.Delay(1500);
            BuildInitialScreen();
        }

        public override void OpenConfigurator()
        {
            using (var configWindow = new ConfigWindow(this))
            {
                configWindow.ShowDialog();
            }
        }

        private void HandleButtonTapped(object sender, int buttonId)
        {
            MacroDeckLogger.Info(this, $"Received click for button {buttonId} from ESP32!");
            VariableManager.SetValue("CYD_PRESSED_BUTTON", buttonId, VariableType.Integer, this, null);
            AnimateScreenClick((byte)buttonId);
        }

        private void BuildInitialScreen()
        {
            for (byte i = 0; i < 6; i++)
            {
                DrawButton(i, Color.DarkBlue);
            }
        }

        private async void AnimateScreenClick(byte buttonId)
        {
            DrawButton(buttonId, Color.LimeGreen);
            await Task.Delay(300);
            DrawButton(buttonId, Color.DarkBlue);
        }

        private void DrawButton(byte buttonId, Color backgroundColor)
        {
            using (Image<Rgba32> image = new Image<Rgba32>(101, 114))
            {
                image.Mutate(ctx => ctx.BackgroundColor(backgroundColor));
                byte[] jpegData = ImageEncoder.GetJpegBytes(image);
                _device.SendJpeg(buttonId, jpegData);
            }
        }
    }
}