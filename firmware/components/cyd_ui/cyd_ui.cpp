#include "cyd_ui.hpp"

CydUI::CydUI(CydDisplay& disp_ref)
    : display(disp_ref), columns(5), rows(3) {
}

void CydUI::set_grid(uint8_t cols, uint8_t rows_count) {
    if (cols < 1) cols = 1;
    if (rows_count < 1) rows_count = 1;
    if (cols > MAX_COLS) cols = MAX_COLS;
    if (rows_count > MAX_ROWS) rows_count = MAX_ROWS;

    while ((uint16_t)cols * rows_count > MAX_BUTTONS && rows_count > 1) {
        rows_count--;
    }

    columns = cols;
    rows = rows_count;
}

uint8_t CydUI::get_columns() const {
    return columns;
}

uint8_t CydUI::get_rows() const {
    return rows;
}

uint8_t CydUI::get_button_count() const {
    return columns * rows;
}

uint16_t CydUI::button_width() const {
    int usable = SCREEN_W - (2 * START_X) - ((columns - 1) * GAP_X);
    int result = usable / columns;
    return result > 1 ? (uint16_t)result : 1;
}

uint16_t CydUI::button_height() const {
    int usable = SCREEN_H - (2 * START_Y) - ((rows - 1) * GAP_Y);
    int result = usable / rows;
    return result > 1 ? (uint16_t)result : 1;
}

bool CydUI::get_button_rect(uint8_t btn_id, uint16_t &x, uint16_t &y, uint16_t &w, uint16_t &h) const {
    if (btn_id >= get_button_count()) {
        return false;
    }

    uint8_t row = btn_id / columns;
    uint8_t col = btn_id % columns;

    w = button_width();
    h = button_height();
    x = START_X + col * (w + GAP_X);
    y = START_Y + row * (h + GAP_Y);

    return true;
}

void CydUI::draw_grid() {
    uint16_t grey_color = CydDisplay::rgb565(45, 45, 45);

    for (uint8_t i = 0; i < get_button_count(); i++) {
        set_button_color(i, grey_color);
    }
}

void CydUI::set_button_color(uint8_t btn_id, uint16_t color) {
    uint16_t x, y, w, h;
    if (!get_button_rect(btn_id, x, y, w, h)) {
        return;
    }

    display.draw_filled_rectangle(x, y, w, h, color);
}

int CydUI::get_button_from_touch(uint16_t touch_x, uint16_t touch_y) const {
    for (uint8_t i = 0; i < get_button_count(); i++) {
        uint16_t x, y, w, h;
        if (!get_button_rect(i, x, y, w, h)) {
            continue;
        }

        if (touch_x >= x && touch_x < (x + w) && touch_y >= y && touch_y < (y + h)) {
            return i;
        }
    }

    return -1;
}
