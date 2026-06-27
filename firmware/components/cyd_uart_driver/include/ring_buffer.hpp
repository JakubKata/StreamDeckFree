#pragma once
#include <stdint.h>
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

#define BUFFER_SIZE 2048

class RingBuffer {
private:
    uint8_t memory[BUFFER_SIZE];
    int head;
    int tail;
    int data_count;
    SemaphoreHandle_t mutex_lock;

public:
    RingBuffer() {
        head = 0;
        tail = 0;
        data_count = 0;
        mutex_lock = xSemaphoreCreateMutex(); 
    }

    bool push(uint8_t new_byte) {
        if (xSemaphoreTake(mutex_lock, pdMS_TO_TICKS(10)) == pdTRUE) {
            
            if (data_count >= BUFFER_SIZE) {
                xSemaphoreGive(mutex_lock);
                return false;
            }

            memory[head] = new_byte;
            head = (head + 1) % BUFFER_SIZE;
            data_count++;

            xSemaphoreGive(mutex_lock);
            return true;
        }
        return false;
    }

    bool pop(uint8_t &read_byte) {
        if (xSemaphoreTake(mutex_lock, pdMS_TO_TICKS(10)) == pdTRUE) {
            if (data_count == 0) {
                xSemaphoreGive(mutex_lock);
                return false;
            }

            read_byte = memory[tail];
            tail = (tail + 1) % BUFFER_SIZE;
            data_count--;

            xSemaphoreGive(mutex_lock);
            return true;
        }
        return false;
    }
};