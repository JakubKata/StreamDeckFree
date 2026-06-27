#pragma once

#include <stdint.h>

// v5 sends RAW RGB565 in high-speed chunks. 8192 bytes keeps ESP32 RAM usage low
// while still allowing about 38-39 rows per frame for the fixed 3x2 grid.
#define PROTOCOL_MAX_PAYLOAD_SIZE 8192

class ProtocolParser {
public:
    ProtocolParser();

    bool process_byte(uint8_t byte);
    uint8_t get_command() const;
    uint8_t* get_payload();
    uint16_t get_payload_length() const;
    void reset();

private:
    enum State {
        WAIT_FOR_START,
        WAIT_FOR_CMD,
        WAIT_FOR_LEN_L,
        WAIT_FOR_LEN_H,
        READ_PAYLOAD
    };

    State current_state;
    uint8_t command;
    uint16_t payload_length;
    uint16_t bytes_read;
    uint8_t buffer[PROTOCOL_MAX_PAYLOAD_SIZE];
};
