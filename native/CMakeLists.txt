cmake_minimum_required(VERSION 3.21)
project(DatadogNative)

set(DD_BUILD_SHARED ON CACHE BOOL "" FORCE)

add_subdirectory(${CMAKE_SOURCE_DIR}/../modules/dd-sdk-cpp dd-sdk-cpp)
