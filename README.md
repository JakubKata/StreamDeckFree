# StreamDeckFree

![ESP32](https://img.shields.io/badge/ESP32-CYD-orange?style=for-the-badge)
![Macro Deck](https://img.shields.io/badge/Macro%20Deck-2-blue?style=for-the-badge)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?style=for-the-badge)
![ESP-IDF](https://img.shields.io/badge/ESP--IDF-CMake-red?style=for-the-badge)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)

**StreamDeckFree** is an open-source DIY project that turns a cheap ESP32 display board, commonly known as **CYD / Cheap Yellow Display**, into a small touch macro panel for **Macro Deck 2**.

The project uses fast two-way USB serial communication between Windows and the ESP32:

- the PC plugin renders Macro Deck buttons and sends them to the ESP32 display,
- the ESP32 reports touch events back to Macro Deck,
- Macro Deck actions can be triggered directly from the CYD screen,
- no Wi-Fi, browser, phone, or paid hardware is required.

The project consists of two main parts:

- `firmware` - C++ firmware for ESP32, built with ESP-IDF,
- `plugin` - C# .NET plugin for the Macro Deck application.

---

## Features

- Turns an ESP32 CYD board into a physical Macro Deck touch panel
- Fixed **2 rows x 3 columns** layout optimized for 320x240 displays
- Mirrors the first six buttons from the active Macro Deck profile or folder
- Supports touch input from the ESP32 screen
- Triggers Macro Deck actions from hardware touch events
- Two-way USB UART communication
- Default high-speed serial mode: **921600 baud**
- RAW RGB565 image transport for reliable drawing on the ESP32
- Button cache to avoid unnecessary screen updates
- Modular firmware architecture:
  - display driver
  - touch driver
  - UART driver
  - protocol parser
  - UI layer
- Macro Deck variable integration:
  - `CYD_PRESSED_BUTTON`
  - `CYD_BUTTON_EVENT`
- No network setup required
- Designed for cheap and widely available ESP32 display boards

---

## Hardware

Recommended board:

- ESP32 Cheap Yellow Display / CYD
- 2.8 inch TFT display
- 320x240 resolution
- Resistive touch panel
- USB-UART chip, for example CH340 or CP210x

Typical CYD boards are sold as:

```text
ESP32-2432S028
ESP32 CYD
Cheap Yellow Display
ESP32 2.8 inch TFT Touch Display
```

---

## Software Requirements

### Firmware

- ESP-IDF installed and configured
- CMake
- Python environment required by ESP-IDF
- USB driver for your board, for example CH340 or CP210x

### PC Plugin

- Windows 10 or Windows 11
- Visual Studio 2022
- .NET 8.0 SDK
- Macro Deck 2 installed

---

## Build and Installation

### 1. Flash the ESP32 firmware

Open an ESP-IDF terminal and go to the firmware directory:

```bash
cd firmware
idf.py fullclean
idf.py build
idf.py -p COM7 flash
```

Replace `COM7` with the COM port assigned to your ESP32 board.

After flashing, close every serial monitor before starting Macro Deck. Only one program can use the COM port at the same time.

---

### 2. Build the Macro Deck plugin

Open the plugin project in Visual Studio:

```text
plugin/StreamDeckFreePlugin/
```

Build the project:

```text
Build -> Build Solution
```

Copy the complete output directory:

```text
bin/Debug/net8.0-windows10.0.22000.0/
```

or:

```text
bin/Release/net8.0-windows10.0.22000.0/
```

to the Macro Deck plugin directory:

```text
%AppData%\Macro Deck\plugins\StreamDeckFree\
```

> **Important:** Copy the entire output folder, including all `.dll` files and `ExtensionManifest.json`.

---

## Configuration

1. Connect the ESP32 CYD board to your PC.
2. Open **Macro Deck**.
3. Go to **Plugins** -> **Installed**.
4. Open the settings for **StreamDeckFree**.
5. Select the COM port assigned to the ESP32 board.
6. Save the configuration.
7. Completely restart Macro Deck, including closing it from the system tray.

Recommended Macro Deck profile layout:

```text
Rows:    2
Columns: 3
```

The plugin is optimized for six buttons on the CYD screen.

---

## Usage

After Macro Deck starts:

1. The plugin opens the configured COM port.
2. The active Macro Deck profile is rendered into six CYD buttons.
3. The ESP32 displays the buttons on the touch screen.
4. Touching a button on the CYD sends an event back to Macro Deck.
5. Macro Deck executes the assigned action.

The device works as a small external macro panel connected through USB.

---

## Serial Communication

StreamDeckFree does not use Wi-Fi or LAN communication.

Communication is done through the USB serial port created by the ESP32 board:

```text
PC / Macro Deck Plugin  <---- USB UART ---->  ESP32 CYD Firmware
```

Default baudrate:

```text
921600
```

If your USB cable, board, or driver is unstable at `921600`, you can lower the baudrate to `460800` or `115200`, but the same value must be changed in both:

- the firmware,
- the plugin.

---

## Project Structure

```text
StreamDeckFree/
|-- firmware/
|   |-- components/
|   |   |-- cyd_display/
|   |   |-- cyd_touch/
|   |   |-- cyd_uart_driver/
|   |   |-- cyd_ui/
|   |   `-- protocol_parser/
|   |-- main/
|   |   |-- CMakeLists.txt
|   |   `-- main.cpp
|   |-- CMakeLists.txt
|   |-- partitions.csv
|   `-- sdkconfig
|
`-- plugin/
    `-- StreamDeckFreePlugin/
        |-- ConfigWindow.cs
        |-- CydDevice.cs
        |-- ExtensionManifest.json
        |-- ImageEncoder.cs
        |-- StreamDeckFreePlugin.cs
        `-- StreamDeckFreePlugin.csproj
```

---

## Troubleshooting

### The ESP32 screen shows only gray buttons

Check that:

- the correct COM port is selected in the plugin settings,
- Macro Deck was restarted after saving the COM port,
- no serial monitor is using the port,
- firmware and plugin use the same baudrate,
- the newest firmware and newest plugin are installed together.

---

### Macro Deck cannot connect to the CYD

Close all programs that may be using the COM port:

- ESP-IDF monitor
- Arduino Serial Monitor
- PuTTY
- VS Code serial monitor
- other terminal applications

Then restart Macro Deck.

---

### Images load slowly

Use the default high-speed baudrate:

```text
921600
```

Also try:

- a shorter USB cable,
- a different USB port,
- a powered USB hub,
- a better quality data cable.

---

### The wrong button is triggered

Touch calibration may be different on your CYD board.

Check the touch driver settings in the firmware and adjust:

```cpp
TOUCH_SWAP_XY
TOUCH_INVERT_X
TOUCH_INVERT_Y
```

Then rebuild and flash the firmware again.

---

### Macro Deck shows an update error

If Macro Deck shows an error like:

```text
Failed to check for updates for StreamDeckFree
404 Not Found
```

it can usually be ignored. It is related to Macro Deck extension update checking and does not affect USB communication with the ESP32.

---

### Network IP addresses are shown in Macro Deck logs

This project does not use network IP addresses.

Addresses like:

```text
169.254.x.x
192.168.x.x
127.0.0.1
```

are shown by Macro Deck itself or other plugins. StreamDeckFree communicates only through the selected COM port.

---

## Development Notes

The project is designed to be simple, cheap, and hackable.

Possible future improvements:

- faster partial screen updates,
- configurable grid size,
- automatic COM port detection,
- better touch calibration wizard,
- brightness control,
- multiple pages,
- folder navigation improvements.

Contributions are welcome.

---

## Disclaimer

This is an unofficial community project.

StreamDeckFree is not affiliated with:

- Macro Deck,
- Elgato,
- Espressif,
- any hardware manufacturer.

Use it at your own risk.

---

## License

MIT