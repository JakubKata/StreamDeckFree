#pragma once
#include <stdint.h>
#include <stddef.h>
#include "cyd_display.hpp"
#include "tjpgd.h"

class CydJpeg {
public:
    CydJpeg(CydDisplay& display);

    bool draw(uint16_t x, uint16_t y, const uint8_t* jpeg_data, size_t jpeg_size);

private:
    CydDisplay& _display;
    uint16_t _draw_x;
    uint16_t _draw_y;

    struct JpegDev {
        const uint8_t* data;
        size_t size;
        size_t offset;
        CydJpeg* instance;
    };

    static unsigned int input_func(JDEC* jdec, uint8_t* buff, unsigned int ndata);
    static int output_func(JDEC* jdec, void* bitmap, JRECT* rect);
};