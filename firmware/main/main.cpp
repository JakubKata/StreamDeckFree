#include <stdio.h>
#include <stdint.h>
#include <stddef.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#include "uart_driver.hpp"
#include "protocol_parser.hpp"
#include "cyd_display.hpp"
#include "cyd_touch.hpp"
#include "cyd_ui.hpp"

// Commands PC -> ESP32
static const uint8_t CMD_SET_COLOR       = 10;
static const uint8_t CMD_SET_GRID        = 31;
static const uint8_t CMD_DRAW_RGB565_RAW = 32; // reliable RAW RGB565 chunks, no JPEG decoder

// Commands ESP32 -> PC
static const uint8_t CMD_ACK         = 6;
static const uint8_t CMD_TOUCH_EVENT = 20;

static uint16_t read_u16_le(const uint8_t* p) {
    return (uint16_t)p[0] | ((uint16_t)p[1] << 8);
}

static void send_ack(uint8_t acked_cmd, uint8_t button_id, uint8_t status) {
    // status: 0 = OK, 1 = draw error, 2 = invalid payload/button
    uint8_t payload[3] = { acked_cmd, button_id, status };
    send_frame(CMD_ACK, payload, sizeof(payload));
}

static void send_touch_event(uint8_t button_id, uint8_t event_type) {
    // event_type: 1 = press/down, 0 = release/up
    uint8_t payload[2] = { button_id, event_type };
    send_frame(CMD_TOUCH_EVENT, payload, sizeof(payload));
}

extern "C" void app_main(void) {
    printf("Starting StreamDeckFree CYD firmware v5, 921600 baud, fixed 3x2 grid, RAW RGB565 chunks...\n");

    init_uart();

    static ProtocolParser parser;
    static CydDisplay display;
    static CydTouch touch;

    display.init();
    display.fill_screen(CydDisplay::rgb565(0, 0, 0));
    touch.init();

    static CydUI ui(display);
    ui.set_grid(3, 2);
    ui.draw_grid();

    printf("System ready: UART 921600, default grid 3x2, command 32 RAW chunks.\n");

    uint8_t received_byte;
    uint16_t tx = 0;
    uint16_t ty = 0;

    int stable_button = -1;
    int candidate_button = -1;
    TickType_t candidate_since = xTaskGetTickCount();
    const TickType_t touch_stable_ticks = pdMS_TO_TICKS(35);

    while (true) {
        // Drain all available UART bytes. Handling only one byte per tick drops image frames.
        while (get_byte(received_byte)) {
            if (!parser.process_byte(received_byte)) {
                continue;
            }

            uint8_t cmd = parser.get_command();
            uint8_t* payload = parser.get_payload();
            uint16_t len = parser.get_payload_length();

            if (cmd == CMD_SET_GRID) {
                if (len >= 2) {
                    ui.set_grid(payload[0], payload[1]);
                    display.fill_screen(CydDisplay::rgb565(0, 0, 0));
                    ui.draw_grid();
                    printf("Grid set to %u x %u\n", ui.get_columns(), ui.get_rows());
                    send_ack(cmd, 0xFF, 0);
                } else {
                    printf("Invalid SET_GRID payload length: %u\n", len);
                    send_ack(cmd, 0xFF, 2);
                }
            } else if (cmd == CMD_SET_COLOR) {
                if (len == 4) {
                    uint8_t btn_id = payload[0];
                    uint16_t target_color = CydDisplay::rgb565(payload[1], payload[2], payload[3]);
                    ui.set_button_color(btn_id, target_color);
                    send_ack(cmd, btn_id, 0);
                } else {
                    printf("Invalid SET_COLOR payload length: %u\n", len);
                    send_ack(cmd, 0xFF, 2);
                }
            } else if (cmd == CMD_DRAW_RGB565_RAW) {
                // Payload:
                // [0]    button_id
                // [1..2] x offset inside button, little endian
                // [3..4] y offset inside button, little endian
                // [5..6] chunk width, little endian
                // [7..8] chunk height, little endian
                // [9..]  RGB565 pixels, high byte first, low byte second
                if (len < 9) {
                    printf("Invalid RAW payload length: %u\n", len);
                    send_ack(cmd, 0xFF, 2);
                    continue;
                }

                uint8_t btn_id = payload[0];
                uint16_t off_x = read_u16_le(&payload[1]);
                uint16_t off_y = read_u16_le(&payload[3]);
                uint16_t w = read_u16_le(&payload[5]);
                uint16_t h = read_u16_le(&payload[7]);

                uint32_t pixel_bytes = (uint32_t)w * (uint32_t)h * 2U;
                uint32_t expected = 9U + pixel_bytes;

                if (w == 0 || h == 0 || pixel_bytes > 24000U || (uint32_t)len != expected) {
                    printf("Invalid RAW payload: button=%u off=(%u,%u) size=%ux%u len=%u expected=%lu\n",
                           btn_id, off_x, off_y, w, h, len, (unsigned long)expected);
                    send_ack(cmd, btn_id, 2);
                    continue;
                }

                uint16_t bx, by, bw, bh;
                if (!ui.get_button_rect(btn_id, bx, by, bw, bh)) {
                    printf("Invalid button id %u for RAW\n", btn_id);
                    send_ack(cmd, btn_id, 2);
                    continue;
                }

                if ((uint32_t)off_x + w > bw || (uint32_t)off_y + h > bh) {
                    printf("RAW chunk outside button: button=%u off=(%u,%u) chunk=%ux%u button=%ux%u\n",
                           btn_id, off_x, off_y, w, h, bw, bh);
                    send_ack(cmd, btn_id, 2);
                    continue;
                }

                bool ok = display.draw_rgb565be(bx + off_x, by + off_y, w, h, &payload[9], pixel_bytes);
                send_ack(cmd, btn_id, ok ? 0 : 1);

                if (!ok) {
                    printf("RAW draw failed: button=%u off=(%u,%u) size=%ux%u bytes=%lu\n",
                           btn_id, off_x, off_y, w, h, (unsigned long)pixel_bytes);
                }
            } else {
                printf("Unknown command: %u, len=%u\n", cmd, len);
                send_ack(cmd, 0xFF, 2);
            }
        }

        bool touched = touch.get_coordinates(tx, ty);
        int current_button = touched ? ui.get_button_from_touch(tx, ty) : -1;
        TickType_t now = xTaskGetTickCount();

        if (current_button != candidate_button) {
            candidate_button = current_button;
            candidate_since = now;
        } else if (current_button != stable_button && (now - candidate_since) >= touch_stable_ticks) {
            if (stable_button >= 0) {
                send_touch_event((uint8_t)stable_button, 0);
            }

            if (current_button >= 0) {
                send_touch_event((uint8_t)current_button, 1);
            }

            stable_button = current_button;
        }

        vTaskDelay(pdMS_TO_TICKS(touched ? 10 : 5));
    }
}
