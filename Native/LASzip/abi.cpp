#include <cstddef>
#include <filesystem>
#include <fstream>
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

// Use the standard wide-path stream constructor on Windows. Keep the stream alive
// until LASzip closes its borrowed stream; neither a temporary file nor locale
// changes are needed for UTF-8 paths from .NET.
extern "C" CLOUDSCOPE_EXPORT int cloudscope_laz_open_reader(laszip_POINTER handle, const char* path,
    laszip_BOOL* compressed, void** owner)
{
    *owner = nullptr;
    try
    {
        auto stream = new std::ifstream(std::filesystem::u8path(path), std::ios::binary);
        if (!stream->is_open()) { delete stream; return 1; }
        int result = laszip_open_reader_stream(handle, *stream, compressed);
        if (result != 0) { delete stream; return result; }
        *owner = stream; return 0;
    }
    catch (...) { return 1; }
}

extern "C" CLOUDSCOPE_EXPORT int cloudscope_laz_open_writer(laszip_POINTER handle, const char* path,
    laszip_BOOL compressed, void** owner)
{
    *owner = nullptr;
    try
    {
        auto stream = new std::ofstream(std::filesystem::u8path(path), std::ios::binary);
        if (!stream->is_open()) { delete stream; return 1; }
        int result = laszip_open_writer_stream(handle, *stream, compressed, false);
        if (result != 0) { delete stream; return result; }
        *owner = stream; return 0;
    }
    catch (...) { return 1; }
}

extern "C" CLOUDSCOPE_EXPORT void cloudscope_laz_release_stream(void* owner, int writer)
{
    if (writer) delete static_cast<std::ofstream*>(owner);
    else delete static_cast<std::ifstream*>(owner);
}
