#pragma once
#include <Windows.h>
#include <cstdint>
#ifdef AURORAVIEW_IMPORT
#define AV_API extern "C" __declspec(dllimport)
#else
#define AV_API extern "C" __declspec(dllexport)
#endif
AV_API uint32_t __cdecl av_create(HWND parent, const wchar_t* url, const wchar_t* data_directory);
AV_API int __cdecl av_state(uint32_t handle);
AV_API void __cdecl av_place(uint32_t handle, HWND parent, int x, int y, int width, int height, int visible);
AV_API int __cdecl av_poll(uint32_t handle, wchar_t* text, int capacity);
AV_API int __cdecl av_error(uint32_t handle, wchar_t* text, int capacity);
AV_API int __cdecl av_eval(uint32_t handle, const wchar_t* script);
AV_API void __cdecl av_destroy(uint32_t handle);
