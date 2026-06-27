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


```text
bin/Debug/net8.0-windows10.0.22000.0/
```

to your Macro Deck plugins directory:

```text
%AppData%\Macro Deck\plugins\StreamDeckFree\
```

> **Important:** Copy the entire output directory, including all generated `.dll` files and `ExtensionManifest.json`.

---

## Configuration

1. Connect your ESP32 CYD device to your computer.
2. Launch **Macro Deck**.
3. Open **Plugins** → **Installed**.
4. Click the **Configure** (⚙️) button for **Stream Deck Free**.
5. Select the COM port assigned to your ESP32 (CH340/CP210x) and click **Save**.
6. Completely restart Macro Deck (including closing it from the system tray) to establish the serial connection.

---

## Usage

After restarting Macro Deck:

- The plugin automatically connects to the configured COM port.
- The ESP32 receives button images from Macro Deck.
- Touch events on the ESP32 are sent back to Macro Deck and can be used to trigger actions.

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
│       ├── ConfigWindow.cs
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