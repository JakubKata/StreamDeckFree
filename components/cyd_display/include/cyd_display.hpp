#pragma once
#include <stdint.h>

class CydDisplay {
public:
    void init();
    void fill_screen(uint16_t color);

    static inline uint16_t rgb565(uint8_t r, uint8_t g, uint8_t b) {
        return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
    }

private:
    void send_command(uint8_t cmd);
    void send_data(uint8_t data);
    void set_address_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1);
};