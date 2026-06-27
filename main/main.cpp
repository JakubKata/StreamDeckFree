#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "uart_driver.hpp"
#include "protocol_parser.hpp"
#include "cyd_display.hpp"
#include "cyd_touch.hpp"
#include "cyd_ui.hpp"

extern "C" void app_main(void)
{
    printf("Uruchamianie Stream Decka...\n");

    init_uart();
    ProtocolParser parser;
    CydDisplay display;
    CydTouch touch;

    display.init();
    display.fill_screen(CydDisplay::rgb565(0, 0, 0));
    touch.init();

    CydUI ui(display);
    ui.draw_grid();

    printf("System gotowy!\n");

    uint8_t received_byte;
    uint16_t tx, ty;

    while (true) {

        if (get_byte(received_byte)) {
            if (parser.process_byte(received_byte)) {
                if (parser.get_command() == 10) {
                    uint8_t* p = parser.get_payload();
                    uint16_t target_color = CydDisplay::rgb565(p[1], p[2], p[3]);

                    ui.set_button_color(p[0], target_color);
                }
            }
        }

        if (touch.get_coordinates(tx, ty)) {
            int pressed_btn = ui.get_button_from_touch(tx, ty);
            
            if (pressed_btn != -1) {
                printf("Kliknieto przycisk: %d\n", pressed_btn);

                uint8_t rx_payload[1] = { (uint8_t)pressed_btn };

                send_frame(20, rx_payload, 1);

                vTaskDelay(200 / portTICK_PERIOD_MS);
            }
        }

        vTaskDelay(10 / portTICK_PERIOD_MS);
    }
}