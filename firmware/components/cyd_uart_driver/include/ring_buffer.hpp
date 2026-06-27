#pragma once

#include <stdint.h>
#include <stddef.h>
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

#define BUFFER_SIZE 32768

class RingBuffer {
private:
    uint8_t memory[BUFFER_SIZE];
    int head;
    int tail;
    int data_count;
    uint32_t dropped_count;
    SemaphoreHandle_t mutex_lock;

public:
    RingBuffer()
        : head(0), tail(0), data_count(0), dropped_count(0) {
        mutex_lock = xSemaphoreCreateMutex();
    }

    bool push(uint8_t new_byte) {
        if (xSemaphoreTake(mutex_lock, pdMS_TO_TICKS(10)) == pdTRUE) {
            if (data_count >= BUFFER_SIZE) {
                dropped_count++;
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

    size_t push_many(const uint8_t* data, size_t len) {
        if (!data || len == 0) return 0;

        size_t pushed = 0;
        if (xSemaphoreTake(mutex_lock, pdMS_TO_TICKS(10)) == pdTRUE) {
            for (size_t i = 0; i < len; i++) {
                if (data_count >= BUFFER_SIZE) {
                    dropped_count += (uint32_t)(len - i);
                    break;
                }
                memory[head] = data[i];
                head = (head + 1) % BUFFER_SIZE;
                data_count++;
                pushed++;
            }
            xSemaphoreGive(mutex_lock);
        }
        return pushed;
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

    int count() {
        int result = 0;
        if (xSemaphoreTake(mutex_lock, pdMS_TO_TICKS(10)) == pdTRUE) {
            result = data_count;
            xSemaphoreGive(mutex_lock);
        }
        return result;
    }

    uint32_t dropped() {
        uint32_t result = 0;
        if (xSemaphoreTake(mutex_lock, pdMS_TO_TICKS(10)) == pdTRUE) {
            result = dropped_count;
            xSemaphoreGive(mutex_lock);
        }
        return result;
    }
};
