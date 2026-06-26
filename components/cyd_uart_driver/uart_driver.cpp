#include "uart_driver.hpp"
#include "ring_buffer.hpp"
#include "driver/uart.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define UART_PORT UART_NUM_0

static RingBuffer my_buffer;

static void uart_rx_task(void *parameter) {
    uint8_t temp_array[128]; 

    while (true) {
        int bytes_received = uart_read_bytes(UART_PORT, temp_array, sizeof(temp_array), pdMS_TO_TICKS(20));

        if (bytes_received > 0) {
            for (int i = 0; i < bytes_received; i++) {
                my_buffer.push(temp_array[i]);
            }
        }
    }
}

void init_uart() {
    uart_config_t uart_config = {};
    uart_config.baud_rate = 115200;
    uart_config.data_bits = UART_DATA_8_BITS;
    uart_config.parity = UART_PARITY_DISABLE;
    uart_config.stop_bits = UART_STOP_BITS_1;
    uart_config.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
    uart_config.rx_flow_ctrl_thresh = 0;
    uart_config.source_clk = UART_SCLK_DEFAULT;
    
    uart_param_config(UART_PORT, &uart_config);
    uart_set_pin(UART_PORT, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE, UART_PIN_NO_CHANGE);
    
    uart_driver_install(UART_PORT, 1024, 0, 0, NULL, 0);
    xTaskCreate(uart_rx_task, "uart_rx_task", 2048, NULL, 10, NULL);
}

bool get_byte(uint8_t &byte_out) {
    return my_buffer.pop(byte_out);
}