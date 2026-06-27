#pragma once
#include <stdint.h>

class CydTouch {
public:
    void init();
    bool is_touched();
    bool get_coordinates(uint16_t &x, uint16_t &y);
    
private:
    uint16_t read_spi(uint8_t cmd);
};