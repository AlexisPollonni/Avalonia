using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Platform.Interop;
using static Avalonia.OpenGL.EglConsts;

namespace Avalonia.OpenGL
{
    public class EglDisplay
    {
        private readonly EglInterface _egl;
        private readonly IntPtr _display;
        private readonly IntPtr _config;
        private readonly int[] _contextAttributes;
        private readonly int _surfaceType;

        public IntPtr Handle => _display;
        private int _sampleCount;
        private int _stencilSize;
        private GlVersion _version;

        public EglDisplay(EglInterface egl) : this(egl, -1, IntPtr.Zero, null)
        {
            
        }

        static IntPtr CreateDisplay(EglInterface egl, int platformType, IntPtr platformDisplay, int[] attrs)
        {
            var display = IntPtr.Zero;
            if (platformType == -1 && platformDisplay == IntPtr.Zero)
            {
                if (display == IntPtr.Zero)
                    display = egl.GetDisplay(IntPtr.Zero);
            }
            else
            {
                if (egl.GetPlatformDisplayEXT == null)
                    throw new OpenGlException("eglGetPlatformDisplayEXT is not supported by libegl");
                display = egl.GetPlatformDisplayEXT(platformType, platformDisplay, attrs);
            }
            
            if (display == IntPtr.Zero)
                throw OpenGlException.GetFormattedException("eglGetDisplay", egl);
            return display;
        }

        public EglDisplay(EglInterface egl, int platformType, IntPtr platformDisplay, int[] attrs)
            : this(egl, CreateDisplay(egl, platformType, platformDisplay, attrs))
        {

        }

        public EglDisplay(EglInterface egl, IntPtr display)
        {
            _egl = egl;
            _display = display;
            if(_display == IntPtr.Zero)
                throw new ArgumentException();
            
            
            if (!_egl.Initialize(_display, out var major, out var minor))
                throw OpenGlException.GetFormattedException("eglInitialize", _egl);

            var glProfiles = AvaloniaLocator.Current.GetService<AngleOptions>()?.GlProfiles
                                    ?? new[]
                                    {
                                        new GlVersion(GlProfileType.OpenGLES, 3, 0),
                                        new GlVersion(GlProfileType.OpenGLES, 2, 0)
                                    };

            var cfgs = glProfiles.Select(x =>
            {
                var typeBit = EGL_OPENGL_ES3_BIT;

                switch (x.Major)
                {
                    case 2:
                        typeBit = EGL_OPENGL_ES2_BIT;
                        break;

                    case 1:
                        typeBit = EGL_OPENGL_ES_BIT;
                        break;
                }

                return new
                {
                    Attributes = new[]
                    {
                        EGL_CONTEXT_MAJOR_VERSION, x.Major,
                        EGL_CONTEXT_MINOR_VERSION, x.Minor,
                        EGL_NONE
                    },
                    Api = EGL_OPENGL_ES_API,
                    RenderableTypeBit = typeBit,
                    Version = x
                };
            });

            foreach (var cfg in cfgs)
            {
                if (!_egl.BindApi(cfg.Api))
                    continue;
                foreach(var surfaceType in new[]{EGL_PBUFFER_BIT|EGL_WINDOW_BIT, EGL_WINDOW_BIT})
                foreach(var stencilSize in new[]{8, 1, 0})
                foreach (var depthSize in new []{8, 1, 0})
                {
                    var attribs = new[]
                    {
                        EGL_SURFACE_TYPE, surfaceType,
                        EGL_RENDERABLE_TYPE, cfg.RenderableTypeBit,
                        EGL_RED_SIZE, 8,
                        EGL_GREEN_SIZE, 8,
                        EGL_BLUE_SIZE, 8,
                        EGL_ALPHA_SIZE, 8,
                        EGL_STENCIL_SIZE, stencilSize,
                        EGL_DEPTH_SIZE, depthSize,
                        EGL_NONE
                    };
                    if (!_egl.ChooseConfig(_display, attribs, out _config, 1, out int numConfigs))
                        continue;
                    if (numConfigs == 0)
                        continue;
                    _contextAttributes = cfg.Attributes;
                    _surfaceType = surfaceType;
                    _version = cfg.Version;
                    _egl.GetConfigAttrib(_display, _config, EGL_SAMPLES, out _sampleCount);
                    _egl.GetConfigAttrib(_display, _config, EGL_STENCIL_SIZE, out _stencilSize);
                    goto Found;
                }

            }
            Found:
            if (_contextAttributes == null)
                throw new OpenGlException("No suitable EGL config was found");
        }

        public EglDisplay() : this(new EglInterface())
        {
            
        }
        
        public EglInterface EglInterface => _egl;
        public EglContext CreateContext(IGlContext share)
        {
            if((_surfaceType|EGL_PBUFFER_BIT) == 0)
                throw new InvalidOperationException("Platform doesn't support PBUFFER surfaces");
            var shareCtx = (EglContext)share;
            var ctx = _egl.CreateContext(_display, _config, shareCtx?.Context ?? IntPtr.Zero, _contextAttributes);
            if (ctx == IntPtr.Zero)
                throw OpenGlException.GetFormattedException("eglCreateContext", _egl);
            var surf = _egl.CreatePBufferSurface(_display, _config, new[]
            {
                EGL_WIDTH, 1,
                EGL_HEIGHT, 1,
                EGL_NONE
            });
            if (surf == IntPtr.Zero)
                throw OpenGlException.GetFormattedException("eglCreatePBufferSurface", _egl);
            var rv = new EglContext(this, _egl, ctx, new EglSurface(this, _egl, surf),
                _version, _sampleCount, _stencilSize);
            return rv;
        }

        public EglContext CreateContext(EglContext share, EglSurface offscreenSurface)
        {
            var ctx = _egl.CreateContext(_display, _config, share?.Context ?? IntPtr.Zero, _contextAttributes);
            if (ctx == IntPtr.Zero)
                throw OpenGlException.GetFormattedException("eglCreateContext", _egl);
            var rv = new EglContext(this, _egl, ctx, offscreenSurface, _version, _sampleCount, _stencilSize);
            rv.MakeCurrent(null);
            return rv;
        }

        public EglSurface CreateWindowSurface(IntPtr window)
        {
            var s = _egl.CreateWindowSurface(_display, _config, window, new[] {EGL_NONE, EGL_NONE});
            if (s == IntPtr.Zero)
                throw OpenGlException.GetFormattedException("eglCreateWindowSurface", _egl);
            return new EglSurface(this, _egl, s);
        }
        
        public EglSurface CreatePBufferFromClientBuffer (int bufferType, IntPtr handle, int[] attribs)
        {
            var s = _egl.CreatePbufferFromClientBuffer(_display, bufferType, handle,
                _config, attribs);         

            if (s == IntPtr.Zero)
                throw OpenGlException.GetFormattedException("eglCreatePbufferFromClientBuffer", _egl);
            return new EglSurface(this, _egl, s);
        }
    }
}
