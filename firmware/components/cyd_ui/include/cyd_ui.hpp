#pragma once

#include <stdint.h>
#include "cyd_display.hpp"

class CydUI {
private:
    CydDisplay& display;

    static const uint16_t SCREEN_W = 320;
    static const uint16_t SCREEN_H = 240;
    static const uint16_t START_X = 2;
    static const uint16_t START_Y = 2;
    static const uint16_t GAP_X = 3;
    static const uint16_t GAP_Y = 3;
    static const uint8_t MAX_COLS = 8;
    static const uint8_t MAX_ROWS = 6;
    static const uint8_t MAX_BUTTONS = 64;

    uint8_t columns;
    uint8_t rows;

    uint16_t button_width() const;
    uint16_t button_height() const;

public:
    explicit CydUI(CydDisplay& disp_ref);

    void set_grid(uint8_t cols, uint8_t rows_count);
    uint8_t get_columns() const;
    uint8_t get_rows() const;
    uint8_t get_button_count() const;

    void draw_grid();
    void set_button_color(uint8_t btn_id, uint16_t color);
    bool get_button_rect(uint8_t btn_id, uint16_t &x, uint16_t &y, uint16_t &w, uint16_t &h) const;
    int get_button_from_touch(uint16_t touch_x, uint16_t touch_y) const;
};
