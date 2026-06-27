#pragma once
#include <stdint.h>

class ProtocolParser {
public:
    ProtocolParser();
    bool process_byte(uint8_t byte);

    uint8_t get_command();
    uint8_t* get_payload();
    uint16_t get_payload_length();

private:
    enum State { WAIT_FOR_START, WAIT_FOR_CMD, WAIT_FOR_LEN_L, WAIT_FOR_LEN_H, READ_PAYLOAD };

    State current_state;
    uint8_t command;
    uint16_t payload_length;
    uint16_t bytes_read;

    uint8_t buffer[4096];
};