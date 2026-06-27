# StreamDeckFree

StreamDeckFree is an open-source solution that turns a budget ESP32 microcontroller with a display (CYD) into a fully functional touch panel for Macro Deck 2.
The project supports two-way USB (UART) communication:

- Receiving and hardware-decoding JPEG images on the fly
- Reporting user touch events to the Windows system
- Custom, lightweight 16-bit transmission protocol

The project consists of two main parts:

- `firmware` (C++ software for ESP32)
- `plugin` (C# .NET library for the Macro Deck application)

---

## Features

- Zero-latency two-way USB communication (Baudrate: 115200)
- On-the-fly graphics decoding (Tiny JPEG Decompressor) on the MCU
- Optimized image scaling using the `ImageSharp` library
- Modular architecture (separate components for parser, UART, display, and touch)
- Integration with Macro Deck's variable system (`CYD_PRESSED_BUTTON`)

---

## Requirements

- **Hardware:** ESP32 board with an integrated touch screen (e.g., Cheap Yellow Display)
- **Firmware:** ESP-IDF framework, CMake
- **PC Plugin:** Windows 10/11, Visual Studio, .NET 8.0 SDK, Macro Deck 2 installed

---

## Build and Installation

### Firmware (ESP32)

Navigate to the firmware directory and build the project using the ESP-IDF terminal:

```bash
cd firmware
idf.py build
idf.py -p COM4 flash monitor
```

### PC Plugin

1. Open the solution file located in the `plugin/StreamDeckFreePlugin` folder using Visual Studio.
2. Build the project (F6 or `Build Solution`).
3. Copy the generated files from the `bin/Debug/net8.0-windows10.0.22000.0/` folder to your Macro Deck plugins directory:

```powershell
%appdata%\Macro Deck\plugins\StreamDeckFree
```

---

## Usage Example

The `CydDevice` class encapsulates communication with the microcontroller. Below is an example of sending a compressed image and listening for user clicks.

```csharp
using StreamDeckFree;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Initialize connection on a specific COM port
CydDevice device = new CydDevice(this);
device.Connect("COM4");

// Listen for hardware screen touches
device.OnButtonTapped += (sender, btnId) => {
    MacroDeckLogger.Info(this, $"Button pressed: {btnId}");
};

// Generate and send a JPEG image to tile #1
using (Image<Rgba32> img = new Image<Rgba32>(101, 114))
{
    img.Mutate(ctx => ctx.BackgroundColor(Color.DarkBlue));
    
    byte[] jpegData = ImageEncoder.GetJpegBytes(img);
    device.SendJpeg(1, jpegData);
}
```

---

## Project Structure

```text
STREAMDECKFREE/
├── firmware/
│   ├── components/
│   │   ├── cyd_display/
│   │   ├── cyd_jpeg/
│   │   ├── cyd_touch/
│   │   ├── cyd_uart_driver/
│   │   ├── cyd_ui/
│   │   └── protocol_parser/
│   ├── main/
│   │   ├── CMakeLists.txt
│   │   └── main.cpp
│   ├── CMakeLists.txt
│   ├── partitions.csv
│   └── sdkconfig
├── plugin/
│   └── StreamDeckFreePlugin/
│       ├── CydDevice.cs
│       ├── ExtensionManifest.json
│       ├── ImageEncoder.cs
│       └── StreamDeckFreePlugin.cs
├── .gitignore
└── README.md
```

---

## License

MIT