#pragma once
#include <stdint.h>
#include "cyd_display.hpp"

class CydUI {
private:
    CydDisplay& display;

    static const uint16_t BTN_W = 101;
    static const uint16_t BTN_H = 114;
    static const uint16_t GAP_X = 4;
    static const uint16_t GAP_Y = 4;
    static const uint16_t START_X = 5;
    static const uint16_t START_Y = 4;

public:
    CydUI(CydDisplay& disp_ref);

    void draw_grid();
    void set_button_color(uint8_t btn_id, uint16_t color);

    int get_button_from_touch(uint16_t touch_x, uint16_t touch_y);
};