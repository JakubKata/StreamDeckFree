#include "protocol_parser.hpp"

ProtocolParser::ProtocolParser() {
    reset();
}

void ProtocolParser::reset() {
    current_state = WAIT_FOR_START;
    command = 0;
    payload_length = 0;
    bytes_read = 0;
}

bool ProtocolParser::process_byte(uint8_t byte) {
    switch (current_state) {
        case WAIT_FOR_START:
            if (byte == 0x02) {
                current_state = WAIT_FOR_CMD;
            }
            break;

        case WAIT_FOR_CMD:
            command = byte;
            current_state = WAIT_FOR_LEN_L;
            break;

        case WAIT_FOR_LEN_L:
            payload_length = byte;
            current_state = WAIT_FOR_LEN_H;
            break;

        case WAIT_FOR_LEN_H:
            payload_length |= ((uint16_t)byte << 8);
            if (payload_length > PROTOCOL_MAX_PAYLOAD_SIZE) {
                reset();
            } else if (payload_length == 0) {
                current_state = WAIT_FOR_START;
                return true;
            } else {
                bytes_read = 0;
                current_state = READ_PAYLOAD;
            }
            break;

        case READ_PAYLOAD:
            buffer[bytes_read++] = byte;
            if (bytes_read >= payload_length) {
                current_state = WAIT_FOR_START;
                return true;
            }
            break;
    }

    return false;
}

uint8_t ProtocolParser::get_command() const {
    return command;
}

uint8_t* ProtocolParser::get_payload() {
    return buffer;
}

uint16_t ProtocolParser::get_payload_length() const {
    return payload_length;
}
