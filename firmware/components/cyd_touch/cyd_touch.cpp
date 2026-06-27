#include "cyd_touch.hpp"

#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include <string.h>
#include <stdio.h>

#define TOUCH_CS   33
#define TOUCH_CLK  25
#define TOUCH_MOSI 32
#define TOUCH_MISO 39
#define TOUCH_IRQ  36

#define TOUCH_RAW_X_MIN 300
#define TOUCH_RAW_X_MAX 3800
#define TOUCH_RAW_Y_MIN 300
#define TOUCH_RAW_Y_MAX 3800

#define TOUCH_SWAP_XY  1
#define TOUCH_INVERT_X 0
#define TOUCH_INVERT_Y 0

#define TOUCH_DEBUG 0

static spi_device_handle_t touch_spi;

void CydTouch::init() {
    gpio_set_direction((gpio_num_t)TOUCH_IRQ, GPIO_MODE_INPUT);

    spi_bus_config_t buscfg = {};
    buscfg.miso_io_num = TOUCH_MISO;
    buscfg.mosi_io_num = TOUCH_MOSI;
    buscfg.sclk_io_num = TOUCH_CLK;
    buscfg.quadwp_io_num = -1;
    buscfg.quadhd_io_num = -1;
    buscfg.max_transfer_sz = 32;

    spi_bus_initialize(SPI3_HOST, &buscfg, SPI_DMA_CH_AUTO);

    spi_device_interface_config_t devcfg = {};
    devcfg.clock_speed_hz = 2 * 1000 * 1000;
    devcfg.mode = 0;
    devcfg.spics_io_num = TOUCH_CS;
    devcfg.queue_size = 3;

    spi_bus_add_device(SPI3_HOST, &devcfg, &touch_spi);
}

bool CydTouch::is_touched() {
    return gpio_get_level((gpio_num_t)TOUCH_IRQ) == 0;
}

uint16_t CydTouch::read_spi(uint8_t cmd) {
    uint8_t rx_data[3] = {0};
    uint8_t tx_data[3] = {cmd, 0x00, 0x00};

    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = 24;
    t.tx_buffer = tx_data;
    t.rx_buffer = rx_data;

    spi_device_polling_transmit(touch_spi, &t);

    return ((rx_data[1] << 8) | rx_data[2]) >> 3;
}

int32_t CydTouch::map_clamped(int32_t v, int32_t in_min, int32_t in_max, int32_t out_min, int32_t out_max) {
    if (in_min == in_max) return out_min;

    int32_t result = (v - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;

    if (out_min < out_max) {
        if (result < out_min) result = out_min;
        if (result > out_max) result = out_max;
    } else {
        if (result < out_max) result = out_max;
        if (result > out_min) result = out_min;
    }

    return result;
}

bool CydTouch::get_coordinates(uint16_t &x, uint16_t &y) {
    if (!is_touched()) {
        return false;
    }

    uint32_t sum_raw_x = 0;
    uint32_t sum_raw_y = 0;
    const uint8_t samples = 4;

    for (uint8_t i = 0; i < samples; i++) {
        if (!is_touched()) {
            return false;
        }

        sum_raw_x += read_spi(0xD0);
        sum_raw_y += read_spi(0x90);
        vTaskDelay(pdMS_TO_TICKS(1));
    }

    uint16_t raw_x = sum_raw_x / samples;
    uint16_t raw_y = sum_raw_y / samples;

#if TOUCH_SWAP_XY
    int32_t mapped_source_x = raw_y;
    int32_t mapped_source_y = raw_x;
#else
    int32_t mapped_source_x = raw_x;
    int32_t mapped_source_y = raw_y;
#endif

#if TOUCH_INVERT_X
    int32_t cal_x = map_clamped(mapped_source_x, TOUCH_RAW_X_MAX, TOUCH_RAW_X_MIN, 0, 319);
#else
    int32_t cal_x = map_clamped(mapped_source_x, TOUCH_RAW_X_MIN, TOUCH_RAW_X_MAX, 0, 319);
#endif

#if TOUCH_INVERT_Y
    int32_t cal_y = map_clamped(mapped_source_y, TOUCH_RAW_Y_MAX, TOUCH_RAW_Y_MIN, 0, 239);
#else
    int32_t cal_y = map_clamped(mapped_source_y, TOUCH_RAW_Y_MIN, TOUCH_RAW_Y_MAX, 0, 239);
#endif

#if TOUCH_DEBUG
    printf("TOUCH raw=(%u,%u) cal=(%ld,%ld)\n", raw_x, raw_y, (long)cal_x, (long)cal_y);
#endif

    x = (uint16_t)cal_x;
    y = (uint16_t)cal_y;
    return true;
}
