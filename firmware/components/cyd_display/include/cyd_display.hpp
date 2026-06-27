#pragma once

#include <stdint.h>
#include <stddef.h>

class CydDisplay {
public:
    void init();
    void fill_screen(uint16_t color);
    void draw_filled_rectangle(uint16_t x, uint16_t y, uint16_t w, uint16_t h, uint16_t color);
    void draw_bitmap(uint16_t x, uint16_t y, uint16_t w, uint16_t h, const uint16_t* data);

    // Draw raw RGB565 bytes in display order: high byte first, low byte second.
    // This avoids JPEG decoder failures and does not depend on ESP32 endianness.
    bool draw_rgb565be(uint16_t x, uint16_t y, uint16_t w, uint16_t h, const uint8_t* data, size_t len);

    static inline uint16_t rgb565(uint8_t r, uint8_t g, uint8_t b) {
        return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
    }

private:
    void send_command(uint8_t cmd);
    void send_data(uint8_t data);
    void set_address_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1);
};
