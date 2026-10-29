// Runs the same offscreen bridge the app uses, with tones and silence; saves inspection frames.
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <vector>
extern "C" __declspec(dllimport) void* __cdecl meloday_projectm_create(const wchar_t*, char*, int);
extern "C" __declspec(dllimport) int __cdecl meloday_projectm_render(void*, const float*, int, int, int, int, uint32_t*, char*, int);
extern "C" __declspec(dllimport) void __cdecl meloday_projectm_destroy(void*);
int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    std::filesystem::create_directories(argv[2]);
    int found = 0;
    for (const auto& entry : std::filesystem::directory_iterator(argv[1])) {
        if (entry.path().extension() != L".milk") continue;
        char error[1024]{};
        auto host = meloday_projectm_create(entry.path().c_str(), error, sizeof(error));
        if (!host) { std::cerr << error << '\n'; return 1; }
        std::vector<float> pcm(2048);
        std::vector<uint32_t> frame(640 * 360);
        bool changed = false;
        std::vector<uint32_t> earlier;
        for (int i = 0; i < 100; ++i) {
            for (int j = 0; j < 1024; ++j) {
                float phase = (i * 735 + j) * 6.2831853f * (i % 20 < 10 ? 80 : 440) / 44100;
                pcm[2 * j] = i < 80 ? 0.45f * std::sin(phase) : 0;
                pcm[2 * j + 1] = i < 80 ? 0.45f * std::sin(phase + 0.8f) : 0;
            }
            int w = i < 10 ? 320 : 640, h = i < 10 ? 180 : 360;
            if (!meloday_projectm_render(host, pcm.data(), static_cast<int>(pcm.size() / 2), w, h, 60, frame.data(), error, sizeof(error))) {
                std::cerr << error << '\n'; meloday_projectm_destroy(host); return 1;
            }
            if (i == 15) earlier = frame;
            if (i == 70) {
                changed = frame != earlier;
                auto output = std::filesystem::path(argv[2]) / entry.path().stem(); output += L".ppm";
                std::ofstream image(output, std::ios::binary); image << "P6\n640 360\n255\n";
                for (auto p : frame) { char rgb[] = {static_cast<char>(p >> 16), static_cast<char>(p >> 8), static_cast<char>(p)}; image.write(rgb, 3); }
            }
            Sleep(16);
        }
        auto lit = std::count_if(frame.begin(), frame.end(), [](uint32_t p) { return (p & 0xffffff) != 0; });
        std::wcout << entry.path().filename().c_str() << L": lit pixels " << lit << L", animated " << changed << L'\n';
        meloday_projectm_destroy(host);
        if (lit < 100 || !changed) return 1;
        ++found;
    }
    return found == 5 ? 0 : 1;
}
