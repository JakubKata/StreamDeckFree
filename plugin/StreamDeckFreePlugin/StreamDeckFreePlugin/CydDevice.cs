using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Plugins;

namespace StreamDeckFree
{
    public sealed class CydButtonEventArgs : EventArgs
    {
        public int ButtonId { get; }
        public bool Pressed { get; }

        public CydButtonEventArgs(int buttonId, bool pressed)
        {
            ButtonId = buttonId;
            Pressed = pressed;
        }
    }

    public sealed class CydDevice : IDisposable
    {
        public const int BaudRate = 115200;
        public const int MaxFirmwarePayload = 24576;

        private const byte FrameStart = 0x02;
        private const byte CmdAck = 6;
        private const byte CmdTouchEvent = 20;
        private const byte CmdDrawJpeg = 30;
        private const byte CmdSetGrid = 31;
        private const byte CmdDrawRgb565Raw = 32;

        private readonly MacroDeckPlugin _pluginInstance;
        private readonly object _writeLock = new object();
        private readonly object _ackLock = new object();
        private readonly AutoResetEvent _ackWaiter = new AutoResetEvent(false);

        private Win32SerialPort _serialPort;
        private Thread _readerThread;
        private volatile bool _isRunning;

        private bool _waitingForAck;
        private byte _expectedAckCommand;
        private byte _expectedAckButton;
        private byte _lastAckStatus;

        public event EventHandler<CydButtonEventArgs> ButtonEvent;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public CydDevice(MacroDeckPlugin pluginInstance)
        {
            _pluginInstance = pluginInstance;
        }

        public bool Connect(string portName)
        {
            try
            {
                Disconnect();

                _serialPort = new Win32SerialPort();
                _serialPort.Open(portName, BaudRate, 200, 5000);

                _isRunning = true;

                _readerThread = new Thread(ListenForFrames)
                {
                    IsBackground = true,
                    Name = "StreamDeckFree CYD UART reader"
                };
                _readerThread.Start();

                MacroDeckLogger.Info(_pluginInstance, $"CYD connected on {portName} at {BaudRate} baud using Win32 serial API");
                return true;
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(_pluginInstance, $"CYD connect failed on {portName}: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            _isRunning = false;

            try
            {
                _serialPort?.Close();
            }
            catch
            {
                // ignored
            }
        }

        public Task<bool> SetGridAsync(byte columns, byte rows, int timeoutMs = 3000)
        {
            byte[] payload = { columns, rows };
            return Task.Run(() => SendFrameAndWaitForAck(CmdSetGrid, payload, 0xFF, timeoutMs));
        }

        public Task<bool> SendRgb565ImageAsync(byte buttonId, int width, int height, byte[] rgb565Bytes, int timeoutMsPerChunk = 3000)
        {
            if (rgb565Bytes == null || rgb565Bytes.Length == 0 || width <= 0 || height <= 0)
            {
                return Task.FromResult(false);
            }

            int expectedPixelBytes = checked(width * height * 2);
            if (rgb565Bytes.Length != expectedPixelBytes)
            {
                MacroDeckLogger.Warning(
                    _pluginInstance,
                    $"RGB565 byte count mismatch for button {buttonId}: got={rgb565Bytes.Length}, expected={expectedPixelBytes}, size={width}x{height}"
                );
                return Task.FromResult(false);
            }

            return Task.Run(() => SendRgb565ImageInChunks(buttonId, width, height, rgb565Bytes, timeoutMsPerChunk));
        }

        private bool SendRgb565ImageInChunks(byte buttonId, int width, int height, byte[] rgb565Bytes, int timeoutMsPerChunk)
        {
            // Payload header for firmware command 32 is 9 bytes. 12 rows per frame keeps
            // each UART frame small enough for stable CH340/ESP32 transfer at 115200 baud.
            int maxRowsByPayload = Math.Max(1, (MaxFirmwarePayload - 9) / Math.Max(1, width * 2));
            int rowsPerChunk = Math.Max(1, Math.Min(12, maxRowsByPayload));

            for (int y = 0; y < height; y += rowsPerChunk)
            {
                int chunkHeight = Math.Min(rowsPerChunk, height - y);
                int pixelBytes = width * chunkHeight * 2;
                int payloadLength = 9 + pixelBytes;

                if (payloadLength > MaxFirmwarePayload)
                {
                    MacroDeckLogger.Warning(_pluginInstance, $"RGB565 chunk too large for button {buttonId}: {payloadLength} bytes");
                    return false;
                }

                byte[] payload = new byte[payloadLength];
                payload[0] = buttonId;
                WriteU16Le(payload, 1, 0);           // x offset inside button
                WriteU16Le(payload, 3, y);           // y offset inside button
                WriteU16Le(payload, 5, width);       // chunk width
                WriteU16Le(payload, 7, chunkHeight); // chunk height

                int sourceOffset = y * width * 2;
                Buffer.BlockCopy(rgb565Bytes, sourceOffset, payload, 9, pixelBytes);

                bool ok = SendFrameAndWaitForAck(CmdDrawRgb565Raw, payload, buttonId, timeoutMsPerChunk);
                if (!ok)
                {
                    MacroDeckLogger.Warning(_pluginInstance, $"Failed to send RGB565 chunk: button={buttonId}, y={y}, h={chunkHeight}");
                    return false;
                }

                Thread.Sleep(4);
            }

            return true;
        }

        private static void WriteU16Le(byte[] buffer, int offset, int value)
        {
            ushort v = (ushort)value;
            buffer[offset] = (byte)(v & 0xFF);
            buffer[offset + 1] = (byte)((v >> 8) & 0xFF);
        }

        // Kept only for manual legacy tests. The v4 plugin uses RGB565, not JPEG.
        public Task<bool> SendJpegAsync(byte buttonId, byte[] jpegBytes, int timeoutMs = 6000)
        {
            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                return Task.FromResult(false);
            }

            int payloadLength = 1 + jpegBytes.Length;
            if (payloadLength > MaxFirmwarePayload)
            {
                MacroDeckLogger.Warning(
                    _pluginInstance,
                    $"JPEG for button {buttonId} is too large: payload={payloadLength} bytes, max={MaxFirmwarePayload} bytes"
                );
                return Task.FromResult(false);
            }

            byte[] payload = new byte[payloadLength];
            payload[0] = buttonId;
            Buffer.BlockCopy(jpegBytes, 0, payload, 1, jpegBytes.Length);

            return Task.Run(() => SendFrameAndWaitForAck(CmdDrawJpeg, payload, buttonId, timeoutMs));
        }

        private bool SendFrameAndWaitForAck(byte command, byte[] payload, byte expectedButton, int timeoutMs)
        {
            if (!IsConnected)
            {
                return false;
            }

            lock (_writeLock)
            {
                _ackWaiter.Reset();
                lock (_ackLock)
                {
                    _waitingForAck = true;
                    _expectedAckCommand = command;
                    _expectedAckButton = expectedButton;
                    _lastAckStatus = 0xFF;
                }

                try
                {
                    SendFrame(command, payload);
                }
                catch (Exception ex)
                {
                    lock (_ackLock)
                    {
                        _waitingForAck = false;
                    }
                    MacroDeckLogger.Error(_pluginInstance, $"UART write failed for command {command}: {ex.Message}");
                    return false;
                }

                bool gotAck = _ackWaiter.WaitOne(timeoutMs);
                byte status;

                lock (_ackLock)
                {
                    _waitingForAck = false;
                    status = _lastAckStatus;
                }

                if (!gotAck)
                {
                    MacroDeckLogger.Warning(_pluginInstance, $"ACK timeout for command {command}, button {expectedButton}");
                    return false;
                }

                if (status != 0)
                {
                    MacroDeckLogger.Warning(_pluginInstance, $"ESP32 returned status {status} for command {command}, button {expectedButton}");
                    return false;
                }

                return true;
            }
        }

        private void SendFrame(byte command, byte[] payload)
        {
            payload ??= Array.Empty<byte>();

            if (payload.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException("Frame payload is too large");
            }

            byte[] header = new byte[4];
            header[0] = FrameStart;
            header[1] = command;
            header[2] = (byte)(payload.Length & 0xFF);
            header[3] = (byte)((payload.Length >> 8) & 0xFF);

            _serialPort.Write(header, 0, header.Length);
            if (payload.Length > 0)
            {
                _serialPort.Write(payload, 0, payload.Length);
            }
        }

        private void ListenForFrames()
        {
            int state = 0;
            int command = 0;
            int length = 0;
            int index = 0;
            byte[] payload = Array.Empty<byte>();

            while (_isRunning)
            {
                int read;

                try
                {
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    read = _serialPort.ReadByte();
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        MacroDeckLogger.Warning(_pluginInstance, $"UART reader stopped: {ex.Message}");
                    }
                    break;
                }

                if (read < 0)
                {
                    continue;
                }

                byte b = (byte)read;

                switch (state)
                {
                    case 0:
                        if (b == FrameStart)
                        {
                            state = 1;
                        }
                        break;

                    case 1:
                        command = b;
                        state = 2;
                        break;

                    case 2:
                        length = b;
                        state = 3;
                        break;

                    case 3:
                        length |= b << 8;
                        if (length < 0 || length > MaxFirmwarePayload)
                        {
                            state = 0;
                            break;
                        }

                        if (length == 0)
                        {
                            HandleFrame((byte)command, Array.Empty<byte>());
                            state = 0;
                        }
                        else
                        {
                            payload = new byte[length];
                            index = 0;
                            state = 4;
                        }
                        break;

                    case 4:
                        payload[index++] = b;
                        if (index >= length)
                        {
                            HandleFrame((byte)command, payload);
                            state = 0;
                        }
                        break;
                }
            }

            _isRunning = false;
        }

        private void HandleFrame(byte command, byte[] payload)
        {
            if (command == CmdAck)
            {
                HandleAck(payload);
                return;
            }

            if (command == CmdTouchEvent)
            {
                if (payload.Length >= 2)
                {
                    int buttonId = payload[0];
                    bool pressed = payload[1] != 0;
                    ButtonEvent?.Invoke(this, new CydButtonEventArgs(buttonId, pressed));
                }
                else if (payload.Length == 1)
                {
                    int buttonId = payload[0];
                    ButtonEvent?.Invoke(this, new CydButtonEventArgs(buttonId, true));
                    Task.Run(async () =>
                    {
                        await Task.Delay(40).ConfigureAwait(false);
                        ButtonEvent?.Invoke(this, new CydButtonEventArgs(buttonId, false));
                    });
                }
            }
        }

        private void HandleAck(byte[] payload)
        {
            if (payload.Length < 3)
            {
                return;
            }

            byte ackedCommand = payload[0];
            byte ackedButton = payload[1];
            byte status = payload[2];

            lock (_ackLock)
            {
                if (!_waitingForAck)
                {
                    return;
                }

                bool commandMatches = ackedCommand == _expectedAckCommand;
                bool buttonMatches = _expectedAckButton == 0xFF || ackedButton == _expectedAckButton;

                if (commandMatches && buttonMatches)
                {
                    _lastAckStatus = status;
                    _ackWaiter.Set();
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
            _ackWaiter.Dispose();
        }
    }

    internal sealed class Win32SerialPort : IDisposable
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;

        private const uint PurgeTxAbort = 0x0001;
        private const uint PurgeRxAbort = 0x0002;
        private const uint PurgeTxClear = 0x0004;
        private const uint PurgeRxClear = 0x0008;

        private const int SetRts = 3;
        private const int ClrRts = 4;
        private const int SetDtr = 5;
        private const int ClrDtr = 6;

        private const byte NoParity = 0;
        private const byte OneStopBit = 0;
        private const uint DcbFlagBinaryOnly = 0x00000001;

        private SafeFileHandle _handle;
        private string _portName;

        public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

        public static string[] GetPortNames()
        {
            string[] ports = new string[30];
            for (int i = 0; i < ports.Length; i++)
            {
                ports[i] = "COM" + (i + 1);
            }
            return ports;
        }

        public void Open(string portName, int baudRate, int readTimeoutMs, int writeTimeoutMs)
        {
            Close();

            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("COM port name is empty", nameof(portName));
            }

            _portName = portName.Trim().ToUpperInvariant();
            string win32Name = NormalizePortName(_portName);

            _handle = CreateFile(
                win32Name,
                GenericRead | GenericWrite,
                0,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero
            );

            if (_handle == null || _handle.IsInvalid)
            {
                ThrowLastWin32("CreateFile", _portName);
            }

            if (!SetupComm(_handle, 32768, 32768))
            {
                ThrowLastWin32("SetupComm", _portName);
            }

            DCB dcb = new DCB();
            dcb.DCBlength = (uint)Marshal.SizeOf<DCB>();

            if (!GetCommState(_handle, ref dcb))
            {
                ThrowLastWin32("GetCommState", _portName);
            }

            dcb.BaudRate = (uint)baudRate;
            dcb.ByteSize = 8;
            dcb.Parity = NoParity;
            dcb.StopBits = OneStopBit;
            dcb.Flags = DcbFlagBinaryOnly;

            if (!SetCommState(_handle, ref dcb))
            {
                ThrowLastWin32("SetCommState", _portName);
            }

            COMMTIMEOUTS timeouts = new COMMTIMEOUTS
            {
                ReadIntervalTimeout = 50,
                ReadTotalTimeoutMultiplier = 0,
                ReadTotalTimeoutConstant = (uint)Math.Max(1, readTimeoutMs),
                WriteTotalTimeoutMultiplier = 0,
                WriteTotalTimeoutConstant = (uint)Math.Max(1, writeTimeoutMs)
            };

            if (!SetCommTimeouts(_handle, ref timeouts))
            {
                ThrowLastWin32("SetCommTimeouts", _portName);
            }

            // Keep ESP32 out of bootloader mode. Ignore errors because some USB-UART drivers do not support both lines.
            EscapeCommFunction(_handle, ClrDtr);
            EscapeCommFunction(_handle, ClrRts);

            PurgeComm(_handle, PurgeTxAbort | PurgeRxAbort | PurgeTxClear | PurgeRxClear);
        }

        public int ReadByte()
        {
            EnsureOpen();

            byte[] buffer = new byte[1];
            if (!ReadFile(_handle, buffer, 1, out int bytesRead, IntPtr.Zero))
            {
                ThrowLastWin32("ReadFile", _portName);
            }

            if (bytesRead == 0)
            {
                return -1;
            }

            return buffer[0];
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            EnsureOpen();

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            byte[] localBuffer;
            if (offset == 0 && count == buffer.Length)
            {
                localBuffer = buffer;
            }
            else
            {
                localBuffer = new byte[count];
                Buffer.BlockCopy(buffer, offset, localBuffer, 0, count);
            }

            if (!WriteFile(_handle, localBuffer, count, out int bytesWritten, IntPtr.Zero))
            {
                ThrowLastWin32("WriteFile", _portName);
            }

            if (bytesWritten != count)
            {
                throw new TimeoutException($"Write timeout on {_portName}: wrote {bytesWritten}/{count} bytes");
            }
        }

        public void Close()
        {
            try
            {
                _handle?.Dispose();
            }
            finally
            {
                _handle = null;
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void EnsureOpen()
        {
            if (!IsOpen)
            {
                throw new IOException("Serial port is not open");
            }
        }

        private static string NormalizePortName(string portName)
        {
            string trimmed = portName.Trim();
            if (trimmed.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return trimmed;
            }
            return @"\\.\" + trimmed;
        }

        private static void ThrowLastWin32(string apiName, string portName)
        {
            int error = Marshal.GetLastWin32Error();
            string message = new Win32Exception(error).Message;
            throw new IOException($"{apiName} failed for {portName}. Win32 error {error}: {message}");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DCB
        {
            public uint DCBlength;
            public uint BaudRate;
            public uint Flags;
            public ushort wReserved;
            public ushort XonLim;
            public ushort XoffLim;
            public byte ByteSize;
            public byte Parity;
            public byte StopBits;
            public byte XonChar;
            public byte XoffChar;
            public byte ErrorChar;
            public byte EofChar;
            public byte EvtChar;
            public ushort wReserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMMTIMEOUTS
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetupComm(SafeFileHandle hFile, uint dwInQueue, uint dwOutQueue);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetCommState(SafeFileHandle hFile, ref DCB lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommState(SafeFileHandle hFile, ref DCB lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PurgeComm(SafeFileHandle hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EscapeCommFunction(SafeFileHandle hFile, int dwFunc);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            [Out] byte[] lpBuffer,
            int nNumberOfBytesToRead,
            out int lpNumberOfBytesRead,
            IntPtr lpOverlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            int nNumberOfBytesToWrite,
            out int lpNumberOfBytesWritten,
            IntPtr lpOverlapped
        );
    }
}
