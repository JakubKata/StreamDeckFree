#pragma once
#include <stdint.h>

void init_uart();
bool get_byte(uint8_t &byte_out);
void send_frame(uint8_t cmd, uint8_t* payload, uint8_t len);