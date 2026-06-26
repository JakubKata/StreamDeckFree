#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "uart_driver.hpp"

extern "C" void app_main(void)
{
    printf("Starting Stream Deck system...\n");
    
    init_uart();
    
    uint8_t received_byte;
    
    while (true) {
        
        if (get_byte(received_byte)) {
            printf("Received byte: %c (code: %d)\n", received_byte, received_byte);
        }
        
        vTaskDelay(10 / portTICK_PERIOD_MS); 
    }
}