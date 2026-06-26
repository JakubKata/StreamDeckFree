#pragma once
#include <stdint.h>

#define START_BYTE 0x02
#define MAX_PAYLOAD_SIZE 64

enum ParserState {
    WAIT_FOR_START,
    READ_COMMAND,
    READ_LENGTH,
    READ_PAYLOAD
};

class ProtocolParser {
private:
    ParserState current_state;
    uint8_t current_command;
    uint8_t payload_length;
    uint8_t payload_buffer[MAX_PAYLOAD_SIZE];
    uint8_t payload_index;

public:
    ProtocolParser();

    bool process_byte(uint8_t byte);

    uint8_t get_command();
    uint8_t get_payload_length();
    uint8_t* get_payload();
};