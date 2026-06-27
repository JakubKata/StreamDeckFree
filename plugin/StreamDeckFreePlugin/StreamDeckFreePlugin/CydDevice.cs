using System;
using System.IO.Ports;
using System.Threading.Tasks;
using SuchByte.MacroDeck.Plugins;

namespace StreamDeckFree
{
    public class CydDevice
    {
        private SerialPort _serialPort;
        private readonly MacroDeckPlugin _pluginInstance;
        private bool _isRunning;

        public event EventHandler<int> OnButtonTapped;

        public CydDevice(MacroDeckPlugin pluginInstance)
        {
            _pluginInstance = pluginInstance;
        }

        public bool Connect(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.Open();
                _isRunning = true;
                Task.Run(ListenForClicks);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Disconnect()
        {
            _isRunning = false;
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        public void SendJpeg(byte buttonId, byte[] jpegBytes)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            int payloadLength = 1 + jpegBytes.Length;
            byte[] frame = new byte[4 + payloadLength];

            frame[0] = 0x02;
            frame[1] = 30;
            frame[2] = (byte)(payloadLength & 0xFF);
            frame[3] = (byte)((payloadLength >> 8) & 0xFF);
            frame[4] = buttonId;

            Buffer.BlockCopy(jpegBytes, 0, frame, 5, jpegBytes.Length);
            _serialPort.Write(frame, 0, frame.Length);
        }

        private void ListenForClicks()
        {
            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        if (_serialPort.ReadByte() == 0x02)
                        {
                            int cmd = _serialPort.ReadByte();
                            int lenL = _serialPort.ReadByte();
                            int lenH = _serialPort.ReadByte();
                            int len = lenL | (lenH << 8);

                            byte[] payload = new byte[len];
                            for (int i = 0; i < len; i++) payload[i] = (byte)_serialPort.ReadByte();

                            if (cmd == 20 && len >= 1)
                            {
                                int btnId = payload[0];
                                OnButtonTapped?.Invoke(this, btnId);
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }
}