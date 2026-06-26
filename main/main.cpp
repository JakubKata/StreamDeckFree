#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "uart_driver.hpp"
#include "protocol_parser.hpp"
#include "cyd_display.hpp"

extern "C" void app_main(void)
{
    init_uart();

    ProtocolParser parser;
    CydDisplay display;

    display.init();
    display.fill_screen(CydDisplay::rgb565(0, 0, 0));

    uint8_t received_byte;

    while (true) {
        if (get_byte(received_byte)) {
            if (parser.process_byte(received_byte)) {
                if (parser.get_command() == 10) {

                    uint8_t* p = parser.get_payload();

                    uint8_t r = p[0];
                    uint8_t g = p[1];
                    uint8_t b = p[2];

                    uint16_t hw_color = CydDisplay::rgb565(r, g, b);
                    display.fill_screen(hw_color);

                    printf("Painted LCD with RGB(%d, %d, %d)\n", r, g, b);
                }
            }
        }

        vTaskDelay(10 / portTICK_PERIOD_MS); 
    }
}