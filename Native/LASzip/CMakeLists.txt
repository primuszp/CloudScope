cmake_minimum_required(VERSION 3.24)
project(CloudScopeLaszip LANGUAGES C CXX)

# Keep the decoder reproducible and buildable offline. The archive is upstream source,
# with its original license; CloudScope does not carry a second implementation of LAZ.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(CMAKE_POLICY_DEFAULT_CMP0177 NEW)
include(FetchContent)
FetchContent_Declare(laszip_source
    URL "${CMAKE_CURRENT_LIST_DIR}/../../ThirdParty/LASzip-3.5.0.tar.gz"
    URL_HASH SHA256=8166649112f128085d0ef58a49ee927f4df398136e68a25efd03f0758ab2b499
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(laszip_source)
if(WIN32)
    set(decoder_target laszip3)
else()
    set(decoder_target laszip)
endif()
target_sources(${decoder_target} PRIVATE "${CMAKE_CURRENT_LIST_DIR}/abi.cpp")
target_include_directories(${decoder_target} PRIVATE "${laszip_source_SOURCE_DIR}/dll" "${laszip_source_SOURCE_DIR}/include/laszip")
set_target_properties(${decoder_target} PROPERTIES
    OUTPUT_NAME cloudscope_laz
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/out")
foreach(config DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(${decoder_target} PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY_${config} "${CMAKE_BINARY_DIR}/out"
        RUNTIME_OUTPUT_DIRECTORY_${config} "${CMAKE_BINARY_DIR}/out")
endforeach()

set_property(TARGET ${decoder_target} PROPERTY VERSION)
set_property(TARGET ${decoder_target} PROPERTY SOVERSION)
