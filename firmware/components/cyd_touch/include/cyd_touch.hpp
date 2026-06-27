#pragma once

#include <stdint.h>

class CydTouch {
public:
    void init();
    bool is_touched();
    bool get_coordinates(uint16_t &x, uint16_t &y);

private:
    uint16_t read_spi(uint8_t cmd);
    static int32_t map_clamped(int32_t v, int32_t in_min, int32_t in_max, int32_t out_min, int32_t out_max);
};
