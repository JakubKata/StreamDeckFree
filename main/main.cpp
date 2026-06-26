#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "uart_driver.hpp"
#include "protocol_parser.hpp"

extern "C" void app_main(void)
{
    printf("Starting Stream Deck system...\n");
    printf("Waiting for binary frames (Start byte: 0x02)...\n");

    init_uart();

    ProtocolParser parser;
    uint8_t received_byte;

    while (true) {

        if (get_byte(received_byte)) {

            if (parser.process_byte(received_byte)) {

                printf("\n--- COMMAND RECEIVED ---\n");
                printf("Command ID: %d\n", parser.get_command());
                printf("Payload Length: %d\n", parser.get_payload_length());
                
                printf("Data: ");
                uint8_t* payload = parser.get_payload();
                for(int i = 0; i < parser.get_payload_length(); i++) {
                    printf("%02X ", payload[i]);
                }
                printf("\n------------------------\n");
            }
        }

        vTaskDelay(10 / portTICK_PERIOD_MS); 
    }
}