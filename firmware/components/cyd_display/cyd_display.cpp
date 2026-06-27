#include "cyd_display.hpp"

#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_err.h"
#include <string.h>

#define PIN_MOSI 13
#define PIN_CLK  14
#define PIN_CS   15
#define PIN_DC   2
#define PIN_BL   21

#define SCREEN_WIDTH  320
#define SCREEN_HEIGHT 240

static spi_device_handle_t spi_handle;

void CydDisplay::send_command(uint8_t cmd) {
    gpio_set_level((gpio_num_t)PIN_DC, 0);
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = 8;
    t.tx_buffer = &cmd;
    spi_device_polling_transmit(spi_handle, &t);
}

void CydDisplay::send_data(uint8_t data) {
    gpio_set_level((gpio_num_t)PIN_DC, 1);
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = 8;
    t.tx_buffer = &data;
    spi_device_polling_transmit(spi_handle, &t);
}

void CydDisplay::init() {
    gpio_set_direction((gpio_num_t)PIN_DC, GPIO_MODE_OUTPUT);
    gpio_set_direction((gpio_num_t)PIN_BL, GPIO_MODE_OUTPUT);
    gpio_set_level((gpio_num_t)PIN_BL, 1);

    spi_bus_config_t buscfg = {};
    buscfg.miso_io_num = -1;
    buscfg.mosi_io_num = PIN_MOSI;
    buscfg.sclk_io_num = PIN_CLK;
    buscfg.quadwp_io_num = -1;
    buscfg.quadhd_io_num = -1;
    buscfg.max_transfer_sz = SCREEN_WIDTH * 2 + 8;

    spi_bus_initialize(SPI2_HOST, &buscfg, SPI_DMA_CH_AUTO);

    spi_device_interface_config_t devcfg = {};
    devcfg.clock_speed_hz = 40 * 1000 * 1000;
    devcfg.mode = 0;
    devcfg.spics_io_num = PIN_CS;
    devcfg.queue_size = 7;

    spi_bus_add_device(SPI2_HOST, &devcfg, &spi_handle);

    send_command(0x01); // software reset
    vTaskDelay(pdMS_TO_TICKS(150));
    send_command(0x11); // sleep out
    vTaskDelay(pdMS_TO_TICKS(150));
    send_command(0x3A); // pixel format
    send_data(0x55);    // 16-bit RGB565
    send_command(0x36); // memory access control
    send_data(0x28);    // original CYD landscape orientation
    send_command(0x29); // display on
    vTaskDelay(pdMS_TO_TICKS(100));
}

void CydDisplay::set_address_window(uint16_t x0, uint16_t y0, uint16_t x1, uint16_t y1) {
    send_command(0x2A);
    send_data(x0 >> 8);
    send_data(x0 & 0xFF);
    send_data(x1 >> 8);
    send_data(x1 & 0xFF);

    send_command(0x2B);
    send_data(y0 >> 8);
    send_data(y0 & 0xFF);
    send_data(y1 >> 8);
    send_data(y1 & 0xFF);

    send_command(0x2C);
}

void CydDisplay::fill_screen(uint16_t color) {
    set_address_window(0, 0, SCREEN_WIDTH - 1, SCREEN_HEIGHT - 1);
    gpio_set_level((gpio_num_t)PIN_DC, 1);

    uint8_t line_buffer[SCREEN_WIDTH * 2];
    for (int i = 0; i < SCREEN_WIDTH; i++) {
        line_buffer[i * 2] = color >> 8;
        line_buffer[i * 2 + 1] = color & 0xFF;
    }

    for (int y = 0; y < SCREEN_HEIGHT; y++) {
        spi_transaction_t t;
        memset(&t, 0, sizeof(t));
        t.length = SCREEN_WIDTH * 2 * 8;
        t.tx_buffer = line_buffer;
        spi_device_polling_transmit(spi_handle, &t);
    }
}

void CydDisplay::draw_filled_rectangle(uint16_t x, uint16_t y, uint16_t w, uint16_t h, uint16_t color) {
    if (x >= SCREEN_WIDTH || y >= SCREEN_HEIGHT || w == 0 || h == 0) return;
    if ((x + w) > SCREEN_WIDTH) w = SCREEN_WIDTH - x;
    if ((y + h) > SCREEN_HEIGHT) h = SCREEN_HEIGHT - y;

    set_address_window(x, y, x + w - 1, y + h - 1);
    gpio_set_level((gpio_num_t)PIN_DC, 1);

    uint8_t line_buffer[SCREEN_WIDTH * 2];
    for (int i = 0; i < w; i++) {
        line_buffer[i * 2] = color >> 8;
        line_buffer[i * 2 + 1] = color & 0xFF;
    }

    for (int i = 0; i < h; i++) {
        spi_transaction_t t;
        memset(&t, 0, sizeof(t));
        t.length = w * 2 * 8;
        t.tx_buffer = line_buffer;
        spi_device_polling_transmit(spi_handle, &t);
    }
}

void CydDisplay::draw_bitmap(uint16_t x, uint16_t y, uint16_t w, uint16_t h, const uint16_t* data) {
    if (x >= SCREEN_WIDTH || y >= SCREEN_HEIGHT || w == 0 || h == 0 || data == nullptr) return;
    if ((x + w) > SCREEN_WIDTH) w = SCREEN_WIDTH - x;
    if ((y + h) > SCREEN_HEIGHT) h = SCREEN_HEIGHT - y;

    set_address_window(x, y, x + w - 1, y + h - 1);
    gpio_set_level((gpio_num_t)PIN_DC, 1);

    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = w * h * 16;
    t.tx_buffer = data;
    spi_device_polling_transmit(spi_handle, &t);
}

bool CydDisplay::draw_rgb565be(uint16_t x, uint16_t y, uint16_t w, uint16_t h, const uint8_t* data, size_t len) {
    if (data == nullptr || w == 0 || h == 0) return false;
    if (len < (size_t)w * (size_t)h * 2u) return false;
    if (x >= SCREEN_WIDTH || y >= SCREEN_HEIGHT) return false;

    uint16_t original_w = w;
    uint16_t draw_w = w;
    uint16_t draw_h = h;

    if ((x + draw_w) > SCREEN_WIDTH) draw_w = SCREEN_WIDTH - x;
    if ((y + draw_h) > SCREEN_HEIGHT) draw_h = SCREEN_HEIGHT - y;
    if (draw_w == 0 || draw_h == 0) return false;

    set_address_window(x, y, x + draw_w - 1, y + draw_h - 1);
    gpio_set_level((gpio_num_t)PIN_DC, 1);

    // Copy each line into a local aligned buffer before SPI DMA transfer.
    // The source pointer is inside the UART parser buffer and may not be DMA-aligned.
    uint8_t line_buffer[SCREEN_WIDTH * 2];

    for (uint16_t row = 0; row < draw_h; row++) {
        const uint8_t* line = data + ((size_t)row * (size_t)original_w * 2u);
        memcpy(line_buffer, line, draw_w * 2u);

        spi_transaction_t t;
        memset(&t, 0, sizeof(t));
        t.length = draw_w * 2 * 8;
        t.tx_buffer = line_buffer;
        esp_err_t err = spi_device_polling_transmit(spi_handle, &t);
        if (err != ESP_OK) {
            return false;
        }
    }

    return true;
}
