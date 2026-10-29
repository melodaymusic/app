// Small offscreen WGL host for the unmodified projectM C API. No visible/native child window.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <GL/glew.h>
#include <GL/wglew.h>
#include <projectM-4/projectM.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

// GLEW's entry points are global. Protect their initialisation from another view's rendering.
static std::mutex graphicsGate;

struct Host {
    HPBUFFERARB buffer = nullptr;
    HDC dc = nullptr;
    HGLRC context = nullptr;
    projectm_handle engine = nullptr;
    int width = 0, height = 0;
    std::string error;
    std::vector<uint32_t> pixels;

    ~Host() {
        if (context) wglMakeCurrent(dc, context);
        if (engine) projectm_destroy(engine);
        wglMakeCurrent(nullptr, nullptr);
        if (context) wglDeleteContext(context);
        if (dc && buffer) wglReleasePbufferDCARB(buffer, dc);
        if (buffer) wglDestroyPbufferARB(buffer);
    }

    void Resize(int w, int h) {
        if (width == w && height == h) return;
        // Pbuffers have a real default framebuffer, which projectM 4.1 requires for its final pass.
        // Create the replacement before releasing the old buffer; the GL context/preset survives.
        int format = GetPixelFormat(dc);
        const int attrs[] = {0};
        auto next = wglCreatePbufferARB(dc, format, w, h, attrs);
        if (!next) throw std::runtime_error("Couldn't resize the OpenGL surface.");
        auto nextDc = wglGetPbufferDCARB(next);
        if (!nextDc || !wglMakeCurrent(nextDc, context)) {
            if (nextDc) wglReleasePbufferDCARB(next, nextDc);
            wglDestroyPbufferARB(next);
            throw std::runtime_error("Couldn't activate the OpenGL surface.");
        }
        wglReleasePbufferDCARB(buffer, dc);
        wglDestroyPbufferARB(buffer);
        buffer = next; dc = nextDc; width = w; height = h;
        pixels.resize(static_cast<size_t>(w) * h);
        projectm_set_window_size(engine, w, h);
    }
};

static void CopyError(char* target, int capacity, const std::string& error) {
    if (!target || capacity < 1) return;
    auto size = std::min(static_cast<size_t>(capacity - 1), error.size());
    std::memcpy(target, error.data(), size);
    target[size] = 0;
}

extern "C" __declspec(dllexport) Host* __cdecl meloday_projectm_create(const wchar_t* preset, char* error, int capacity) {
    std::lock_guard<std::mutex> guard(graphicsGate);
    HWND window = nullptr;
    HDC bootstrapDc = nullptr;
    HGLRC bootstrapContext = nullptr;
    auto cleanBootstrap = [&]() {
        if (bootstrapContext) wglDeleteContext(bootstrapContext);
        if (bootstrapDc) ReleaseDC(window, bootstrapDc);
        if (window) DestroyWindow(window);
    };
    try {
        // Windows' legacy context is used only to discover modern WGL extensions.
        window = CreateWindowExW(0, L"STATIC", L"Meloday projectM bootstrap", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!window) throw std::runtime_error("Couldn't initialise OpenGL.");
        bootstrapDc = GetDC(window);
        PIXELFORMATDESCRIPTOR pfd{};
        pfd.nSize = sizeof(pfd); pfd.nVersion = 1;
        pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL;
        pfd.iPixelType = PFD_TYPE_RGBA; pfd.cColorBits = 32; pfd.cAlphaBits = 8;
        auto format = ChoosePixelFormat(bootstrapDc, &pfd);
        if (!format || !SetPixelFormat(bootstrapDc, format, &pfd)) throw std::runtime_error("No OpenGL pixel format is available.");
        bootstrapContext = wglCreateContext(bootstrapDc);
        if (!bootstrapContext || !wglMakeCurrent(bootstrapDc, bootstrapContext)) throw std::runtime_error("Couldn't activate OpenGL.");
        glewExperimental = GL_TRUE;
        if (glewInit() != GLEW_OK || !WGLEW_ARB_pbuffer || !WGLEW_ARB_pixel_format || !WGLEW_ARB_create_context)
            throw std::runtime_error("projectM needs a graphics driver with OpenGL 3.3 and offscreen rendering.");
        const int attrs[] = {WGL_DRAW_TO_PBUFFER_ARB, TRUE, WGL_SUPPORT_OPENGL_ARB, TRUE,
            WGL_PIXEL_TYPE_ARB, WGL_TYPE_RGBA_ARB, WGL_COLOR_BITS_ARB, 32, WGL_ALPHA_BITS_ARB, 8, WGL_DOUBLE_BUFFER_ARB, FALSE, 0};
        UINT count = 0;
        if (!wglChoosePixelFormatARB(bootstrapDc, attrs, nullptr, 1, &format, &count) || !count)
            throw std::runtime_error("No offscreen OpenGL pixel format is available.");
        auto host = std::make_unique<Host>();
        const int empty[] = {0};
        host->buffer = wglCreatePbufferARB(bootstrapDc, format, 64, 64, empty);
        if (!host->buffer) throw std::runtime_error("Couldn't create the offscreen OpenGL surface.");
        host->dc = wglGetPbufferDCARB(host->buffer);
        const int contextAttrs[] = {WGL_CONTEXT_MAJOR_VERSION_ARB, 3, WGL_CONTEXT_MINOR_VERSION_ARB, 3,
            WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB, 0};
        host->context = wglCreateContextAttribsARB(host->dc, nullptr, contextAttrs);
        if (!host->context || !wglMakeCurrent(host->dc, host->context)) throw std::runtime_error("projectM needs OpenGL 3.3. Please update the graphics driver.");
        if (glewInit() != GLEW_OK) throw std::runtime_error("Couldn't load OpenGL 3.3.");
        while (glGetError() != GL_NO_ERROR) {} // GLEW's core-profile probe can leave an error.
        host->engine = projectm_create();
        if (!host->engine) throw std::runtime_error("Couldn't start projectM.");
        projectm_set_preset_locked(host->engine, true);
        projectm_set_mesh_size(host->engine, 48, 32);
        projectm_set_window_size(host->engine, 64, 64);
        projectm_set_preset_switch_failed_event_callback(host->engine, [](const char*, const char* message, void* data) {
            static_cast<Host*>(data)->error = message ? message : "Couldn't load this preset.";
        }, host.get());
        // Load contents, avoiding locale-dependent narrow file paths (Windows user names may be Unicode).
        std::ifstream stream(preset, std::ios::binary);
        if (!stream) throw std::runtime_error("The projectM preset is missing.");
        std::string contents((std::istreambuf_iterator<char>(stream)), std::istreambuf_iterator<char>());
        projectm_load_preset_data(host->engine, contents.c_str(), false);
        if (!host->error.empty()) throw std::runtime_error(host->error);
        host->width = host->height = 64;
        wglMakeCurrent(nullptr, nullptr);
        cleanBootstrap();
        return host.release();
    } catch (const std::exception& ex) {
        wglMakeCurrent(nullptr, nullptr);
        cleanBootstrap();
        CopyError(error, capacity, ex.what());
        return nullptr;
    }
}

extern "C" __declspec(dllexport) int __cdecl meloday_projectm_render(Host* host, const float* pcm, int count,
    int width, int height, int fps, uint32_t* bgra, char* error, int capacity) {
    std::lock_guard<std::mutex> guard(graphicsGate);
    try {
        if (!host || !bgra || width < 1 || height < 1 || width > 1920 || height > 1920)
            throw std::runtime_error("Invalid projectM frame.");
        if (!wglMakeCurrent(host->dc, host->context)) throw std::runtime_error("Couldn't activate projectM's graphics context.");
        host->Resize(width, height);
        projectm_set_fps(host->engine, fps);
        if (pcm && count > 0) projectm_pcm_add_float(host->engine, pcm, std::min(static_cast<unsigned int>(count), projectm_pcm_get_max_samples()), PROJECTM_STEREO);
        projectm_opengl_render_frame(host->engine);
        if (!host->error.empty()) throw std::runtime_error(host->error);
        glBindFramebuffer(GL_READ_FRAMEBUFFER, 0);
        glReadBuffer(GL_FRONT);
        glReadPixels(0, 0, width, height, GL_BGRA, GL_UNSIGNED_BYTE, host->pixels.data());
        if (glGetError() != GL_NO_ERROR) throw std::runtime_error("Couldn't read projectM's frame.");
        // OpenGL has a bottom-left origin; Win2D wants top-left, opaque BGRA.
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
                bgra[y * width + x] = host->pixels[(height - 1 - y) * width + x] | 0xff000000u;
        wglMakeCurrent(nullptr, nullptr);
        return 1;
    } catch (const std::exception& ex) {
        wglMakeCurrent(nullptr, nullptr);
        CopyError(error, capacity, ex.what());
        return 0;
    }
}

extern "C" __declspec(dllexport) void __cdecl meloday_projectm_destroy(Host* host) {
    std::lock_guard<std::mutex> guard(graphicsGate);
    delete host;
}
