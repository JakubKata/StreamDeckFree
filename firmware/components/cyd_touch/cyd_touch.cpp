#include "cyd_touch.hpp"
#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include <string.h>

#define TOUCH_CS   33
#define TOUCH_CLK  25
#define TOUCH_MOSI 32
#define TOUCH_MISO 39
#define TOUCH_IRQ  36

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

bool CydTouch::get_coordinates(uint16_t &x, uint16_t &y) {
    if (!is_touched()) return false;

    uint16_t raw_x = read_spi(0xD0);
    uint16_t raw_y = read_spi(0x90);

    if (!is_touched()) return false;

    int32_t cal_x = (raw_x - 300) * 320 / (3800 - 300);
    int32_t cal_y = (raw_y - 300) * 240 / (3800 - 300);

    if (cal_x < 0) cal_x = 0;
    if (cal_x > 320) cal_x = 320;
    if (cal_y < 0) cal_y = 0;
    if (cal_y > 240) cal_y = 240;

    x = (uint16_t)cal_x;
    y = (uint16_t)cal_y;

    return true;
}