#include "uart_driver.hpp"
#include "ring_buffer.hpp"

#include "driver/uart.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include <stdio.h>

#define UART_PORT UART_NUM_0
#define UART_BAUDRATE 115200
#define UART_RX_DRIVER_BUFFER 8192
#define UART_TX_DRIVER_BUFFER 2048

static RingBuffer my_buffer;

static void uart_rx_task(void *parameter) {
    (void)parameter;

    uint8_t temp_array[512];
    uint32_t last_reported_drops = 0;

    while (true) {
        int bytes_received = uart_read_bytes(
            UART_PORT,
            temp_array,
            sizeof(temp_array),
            pdMS_TO_TICKS(10)
        );

        if (bytes_received > 0) {
            my_buffer.push_many(temp_array, (size_t)bytes_received);
        }

        uint32_t dropped_now = my_buffer.dropped();
        if (dropped_now != last_reported_drops) {
            printf("UART ring buffer overflow, dropped bytes: %lu\n", (unsigned long)dropped_now);
            last_reported_drops = dropped_now;
        }
    }
}

void init_uart() {
    uart_config_t uart_config = {};
    uart_config.baud_rate = UART_BAUDRATE;
    uart_config.data_bits = UART_DATA_8_BITS;
    uart_config.parity = UART_PARITY_DISABLE;
    uart_config.stop_bits = UART_STOP_BITS_1;
    uart_config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
    uart_config.rx_flow_ctrl_thresh = 0;
    uart_config.source_clk = UART_SCLK_DEFAULT;

    uart_param_config(UART_PORT, &uart_config);
    uart_set_pin(
        UART_PORT,
        UART_PIN_NO_CHANGE,
        UART_PIN_NO_CHANGE,
        UART_PIN_NO_CHANGE,
        UART_PIN_NO_CHANGE
    );

    uart_driver_install(
        UART_PORT,
        UART_RX_DRIVER_BUFFER,
        UART_TX_DRIVER_BUFFER,
        0,
        NULL,
        0
    );

    xTaskCreate(uart_rx_task, "uart_rx_task", 4096, NULL, 12, NULL);
}

bool get_byte(uint8_t &byte_out) {
    return my_buffer.pop(byte_out);
}

int get_rx_buffer_count() {
    return my_buffer.count();
}

uint32_t get_rx_dropped_count() {
    return my_buffer.dropped();
}

void send_frame(uint8_t cmd, const uint8_t* payload, uint16_t len) {
    uint8_t header[4];
    header[0] = 0x02;
    header[1] = cmd;
    header[2] = len & 0xFF;
    header[3] = (len >> 8) & 0xFF;

    uart_write_bytes(UART_PORT, (const char*)header, sizeof(header));
    if (payload != NULL && len > 0) {
        uart_write_bytes(UART_PORT, (const char*)payload, len);
    }
}
