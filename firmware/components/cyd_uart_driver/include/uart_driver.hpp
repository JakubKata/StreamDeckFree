#pragma once

#include <stdint.h>

void init_uart();
bool get_byte(uint8_t &byte_out);
int get_rx_buffer_count();
uint32_t get_rx_dropped_count();
void send_frame(uint8_t cmd, const uint8_t* payload, uint16_t len);
