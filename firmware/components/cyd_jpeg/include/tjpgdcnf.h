#pragma once

// TJpgDec configuration for the CYD JPEG component.
// RGB565 output matches CydDisplay::draw_bitmap().
#define JD_FORMAT 1
#define JD_USE_SCALE 0
#define JD_FASTDECODE 0
#define JD_TBLCLIP 1
#define JD_SZBUF 3100