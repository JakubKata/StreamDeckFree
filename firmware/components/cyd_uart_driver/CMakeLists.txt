idf_component_register(SRCS "uart_driver.cpp"
                       INCLUDE_DIRS "include"
                       REQUIRES esp_driver_uart freertos)