#include "cyd_jpeg.hpp"
#include <string.h>

static uint8_t jpeg_workspace[3100];

CydJpeg::CydJpeg(CydDisplay& display) : _display(display), _draw_x(0), _draw_y(0) {}

bool CydJpeg::draw(uint16_t x, uint16_t y, const uint8_t* jpeg_data, size_t jpeg_size) {
    _draw_x = x;
    _draw_y = y;

    JpegDev dev;
    dev.data = jpeg_data;
    dev.size = jpeg_size;
    dev.offset = 0;
    dev.instance = this;

    JDEC jdec;

    if (jd_prepare(&jdec, input_func, jpeg_workspace, sizeof(jpeg_workspace), &dev) != JDR_OK) {
        return false;
    }

    if (jd_decomp(&jdec, output_func, 0) != JDR_OK) {
        return false;
    }

    return true;
}

unsigned int CydJpeg::input_func(JDEC* jdec, uint8_t* buff, unsigned int ndata) {
    JpegDev* dev = (JpegDev*)jdec->device;
    unsigned int remain = dev->size - dev->offset;

    if (ndata > remain) ndata = remain;

    if (buff) {
        memcpy(buff, dev->data + dev->offset, ndata);
    }
    dev->offset += ndata;
    return ndata;
}

int CydJpeg::output_func(JDEC* jdec, void* bitmap, JRECT* rect) {
    JpegDev* dev = (JpegDev*)jdec->device;
    CydJpeg* self = dev->instance;

    uint16_t x = self->_draw_x + rect->left;
    uint16_t y = self->_draw_y + rect->top;
    uint16_t w = rect->right - rect->left + 1;
    uint16_t h = rect->bottom - rect->top + 1;

    self->_display.draw_bitmap(x, y, w, h, (const uint16_t*)bitmap);

    return 1;
}