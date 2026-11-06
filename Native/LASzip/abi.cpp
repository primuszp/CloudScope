#include <cstddef>
#include "laszip_api.h"

#if defined(_WIN32)
#define CLOUDSCOPE_EXPORT __declspec(dllexport)
#else
#define CLOUDSCOPE_EXPORT __attribute__((visibility("default")))
#endif

// Fail cleanly if an incompatible native ABI is ever packaged beside the managed reader.
extern "C" CLOUDSCOPE_EXPORT int cloudscope_laz_abi(int field)
{
    switch (field)
    {
        case 0: return sizeof(laszip_point_struct);
        case 1: return offsetof(laszip_point_struct, gps_time);
        case 2: return offsetof(laszip_point_struct, rgb);
        case 3: return offsetof(laszip_point_struct, wave_packet);
        case 4: return offsetof(laszip_point_struct, extra_bytes);
        case 5: return offsetof(laszip_point_struct, num_extra_bytes);
        default: return -1;
    }
}
