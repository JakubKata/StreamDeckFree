#include "cyd_ui.hpp"

CydUI::CydUI(CydDisplay& disp_ref) : display(disp_ref) {
}

void CydUI::draw_grid() {
    uint16_t grey_color = CydDisplay::rgb565(60, 60, 60);

    for (int row = 0; row < 2; row++) {
        for (int col = 0; col < 3; col++) {
            uint16_t x = START_X + col * (BTN_W + GAP_X);
            uint16_t y = START_Y + row * (BTN_H + GAP_Y);
            display.draw_filled_rectangle(x, y, BTN_W, BTN_H, grey_color);
        }
    }
}

void CydUI::set_button_color(uint8_t btn_id, uint16_t color) {
    if (btn_id > 5) return;

    int row = btn_id / 3;
    int col = btn_id % 3;

    uint16_t x = START_X + col * (BTN_W + GAP_X);
    uint16_t y = START_Y + row * (BTN_H + GAP_Y);

    display.draw_filled_rectangle(x, y, BTN_W, BTN_H, color);
}

int CydUI::get_button_from_touch(uint16_t touch_x, uint16_t touch_y) {
    for (int row = 0; row < 2; row++) {
        for (int col = 0; col < 3; col++) {
            uint16_t x = START_X + col * (BTN_W + GAP_X);
            uint16_t y = START_Y + row * (BTN_H + GAP_Y);

            if (touch_x >= x && touch_x <= (x + BTN_W) &&
                touch_y >= y && touch_y <= (y + BTN_H)) {
                return (row * 3) + col;
            }
        }
    }
    return -1;
}