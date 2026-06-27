#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "uart_driver.hpp"
#include "protocol_parser.hpp"
#include "cyd_display.hpp"
#include "cyd_touch.hpp"
#include "cyd_ui.hpp"
#include "cyd_jpeg.hpp" 

extern "C" void app_main(void)
{
    printf("Starting Stream Deck system...\n");

    
    init_uart();
    static ProtocolParser parser;
    static CydDisplay display;
    static CydTouch touch;
    static CydJpeg jpeg(display); 

    display.init();
    display.fill_screen(CydDisplay::rgb565(0, 0, 0));
    touch.init();
    
    static CydUI ui(display);
    ui.draw_grid();

    printf("System ready to receive JPEG!\n");

    uint8_t received_byte;
    uint16_t tx, ty;

    while (true) {
        
        if (get_byte(received_byte)) {
            if (parser.process_byte(received_byte)) {
                
                uint8_t cmd = parser.get_command();
                uint8_t* payload = parser.get_payload();
                uint16_t len = parser.get_payload_length();
                
                
                if (cmd == 10 && len == 4) {
                    uint16_t target_color = CydDisplay::rgb565(payload[1], payload[2], payload[3]);
                    ui.set_button_color(payload[0], target_color);
                }
                
                
                else if (cmd == 30 && len > 1) {
                    uint8_t btn_id = payload[0];
                    
                    
                    const uint8_t* img_data = &payload[1];
                    size_t img_size = len - 1;
                    
                    
                    
                    int row = btn_id / 3;
                    int col = btn_id % 3;
                    uint16_t x = 5 + col * (101 + 4);
                    uint16_t y = 4 + row * (114 + 4);
                    
                    
                    if (jpeg.draw(x, y, img_data, img_size)) {
                        printf("Success: JPEG drawn for button %d (Size: %d bytes)\n", btn_id, img_size);
                    } else {
                        printf("Error: Failed to decode the JPEG file.\n");
                    }
                }
            }
        }

        
        if (touch.get_coordinates(tx, ty)) {
            int pressed_btn = ui.get_button_from_touch(tx, ty);
            
            if (pressed_btn != -1) {
                
                uint8_t rx_payload[1] = { (uint8_t)pressed_btn };
                
                
                
                
                send_frame(20, rx_payload, 1);
                
                vTaskDelay(200 / portTICK_PERIOD_MS);
            }
        }

        vTaskDelay(1 / portTICK_PERIOD_MS); 
    }
}