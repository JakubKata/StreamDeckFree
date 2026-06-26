idf_component_register(SRCS "main.cpp"
                       INCLUDE_DIRS "."
                       REQUIRES cyd_uart_driver protocol_parser)