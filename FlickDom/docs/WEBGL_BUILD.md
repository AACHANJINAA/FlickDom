# WebGL Build Notes

Use the Unity menu `FlickDom > Build > WebGL Release And Run` for local WebGL testing.

The generated `Web_Test` build that showed Chrome's "page unresponsive" dialog was a
Development-style output:

- `index.html` loads `TemplateData/profiler.js`
- `index.html` adds the Unity profiler button
- `Build/Web_Test.wasm` is about 140 MB and uncompressed

That loading dialog appears while Chrome is busy compiling the WebAssembly module. It is
not caused by Unity Relay or the flick networking code.

The release menu applies WebGL release settings before building:

- Development Build, script debugging, profiler connection, and deep profiling disabled
- IL2CPP compiler set to Release
- Managed stripping set to Medium
- WebGL debug symbols disabled
- WebGL exception support limited to explicitly thrown exceptions
- WebGL compression disabled for reliable localhost testing
- WebGL initial memory raised to 128 MB to avoid early memory-growth stalls

If a hosted release build needs gzip or Brotli compression later, enable it only with a
server that returns the matching `Content-Encoding` headers for the generated files.
