#include "protocol_parser.hpp"

ProtocolParser::ProtocolParser() {
    current_state = WAIT_FOR_START;
    current_command = 0;
    payload_length = 0;
    payload_index = 0;
}

bool ProtocolParser::process_byte(uint8_t byte) {
    switch (current_state) {
        
        case WAIT_FOR_START:
            if (byte == START_BYTE) {
                current_state = READ_COMMAND;
            }
            break;
            
        case READ_COMMAND:
            current_command = byte;
            current_state = READ_LENGTH;
            break;
            
        case READ_LENGTH:
            payload_length = byte;
            if (payload_length > MAX_PAYLOAD_SIZE) {
                current_state = WAIT_FOR_START;
            } else if (payload_length == 0) {
                current_state = WAIT_FOR_START; 
                return true;
            } else {
                payload_index = 0;
                current_state = READ_PAYLOAD;
            }
            break;
            
        case READ_PAYLOAD:
            payload_buffer[payload_index] = byte;
            payload_index++;
            
            if (payload_index >= payload_length) {
                current_state = WAIT_FOR_START;
                return true;
            }
            break;
    }
    
    return false; 
}

uint8_t ProtocolParser::get_command() {
    return current_command;
}

uint8_t ProtocolParser::get_payload_length() {
    return payload_length;
}

uint8_t* ProtocolParser::get_payload() {
    return payload_buffer;
}