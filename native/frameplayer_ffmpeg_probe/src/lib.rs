use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int, c_uint, c_void};
use std::path::Path;

mod decode_core;
mod exact_index;

pub(crate) const MESSAGE_CAPACITY: usize = 256;
pub(crate) const STATUS_OK: c_int = 0;
pub(crate) const STATUS_INVALID_ARGUMENT: c_int = 1;
pub(crate) const STATUS_RUNTIME_DIRECTORY_MISSING: c_int = 2;
pub(crate) const STATUS_LIBRARY_LOAD_FAILED: c_int = 3;
pub(crate) const STATUS_SYMBOL_LOAD_FAILED: c_int = 4;
pub(crate) const MAX_DECODED_FRAME_PIXELS: i64 = 256 * 1024 * 1024 / 4;

type VersionFn = unsafe extern "C" fn() -> c_uint;

#[repr(C)]
pub struct FramePlayerRustFfmpegProbeResult {
    pub status: c_int,
    pub avutil_version: c_uint,
    pub avcodec_version: c_uint,
    pub avformat_version: c_uint,
    pub message: [c_char; MESSAGE_CAPACITY],
}

impl Default for FramePlayerRustFfmpegProbeResult {
    fn default() -> Self {
        Self {
            status: STATUS_INVALID_ARGUMENT,
            avutil_version: 0,
            avcodec_version: 0,
            avformat_version: 0,
            message: [0 as c_char; MESSAGE_CAPACITY],
        }
    }
}

#[no_mangle]
/// Probes the FFmpeg runtime and writes version metadata to `result`.
///
/// # Safety
///
/// `runtime_directory` must point to a valid NUL-terminated string for the duration of the call,
/// and `result` must point to writable storage for one `FramePlayerRustFfmpegProbeResult`.
pub unsafe extern "C" fn frameplayer_rust_ffmpeg_probe(
    runtime_directory: *const c_char,
    result: *mut FramePlayerRustFfmpegProbeResult,
) -> c_int {
    if result.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    *result = FramePlayerRustFfmpegProbeResult::default();

    ERR.with(|slot| {
        *slot.borrow_mut() = None;
    });

    let status = std::panic::catch_unwind(|| probe_inner(runtime_directory, result))
        .unwrap_or_else(|_| {
            (*result).status = STATUS_INVALID_ARGUMENT;
            write_message(result, "Rust FFmpeg runtime probe panicked.");
            STATUS_INVALID_ARGUMENT
        });

    (*result).status = status;
    status
}

unsafe fn probe_inner(
    runtime_directory: *const c_char,
    result: *mut FramePlayerRustFfmpegProbeResult,
) -> c_int {
    if runtime_directory.is_null() {
        write_message(result, "FFmpeg runtime directory pointer was null.");
        return STATUS_INVALID_ARGUMENT;
    }

    let runtime_directory = match CStr::from_ptr(runtime_directory).to_str() {
        Ok(value) if !value.trim().is_empty() => value,
        Ok(_) => {
            (*result).status = STATUS_RUNTIME_DIRECTORY_MISSING;
            write_message(result, "FFmpeg runtime directory was empty.");
            return STATUS_RUNTIME_DIRECTORY_MISSING;
        }
        Err(_) => {
            write_message(result, "FFmpeg runtime directory was not valid UTF-8.");
            return STATUS_INVALID_ARGUMENT;
        }
    };

    match probe_runtime(Path::new(runtime_directory), result) {
        Ok(()) => STATUS_OK,
        Err(status) => {
            (*result).status = status;
            if result_message_is_empty(result) {
                write_message(result, "");
            }
            status
        }
    }
}

unsafe fn probe_runtime(
    runtime_directory: &Path,
    result: *mut FramePlayerRustFfmpegProbeResult,
) -> Result<(), c_int> {
    if !runtime_directory.is_dir() {
        (*result).status = STATUS_RUNTIME_DIRECTORY_MISSING;
        write_message(
            result,
            &format!(
                "FFmpeg runtime directory does not exist: {}",
                runtime_directory.display()
            ),
        );
        return Err(STATUS_RUNTIME_DIRECTORY_MISSING);
    }

    let avutil = load_required_library(runtime_directory, runtime_library_names().avutil)?;
    let _swresample = load_required_library(runtime_directory, runtime_library_names().swresample)?;
    let _swscale = load_required_library(runtime_directory, runtime_library_names().swscale)?;
    let avcodec = load_required_library(runtime_directory, runtime_library_names().avcodec)?;
    let avformat = load_required_library(runtime_directory, runtime_library_names().avformat)?;

    let avutil_version = load_version_symbol(&avutil, "avutil_version")?;
    let avcodec_version = load_version_symbol(&avcodec, "avcodec_version")?;
    let avformat_version = load_version_symbol(&avformat, "avformat_version")?;

    (*result).status = STATUS_OK;
    (*result).avutil_version = avutil_version();
    (*result).avcodec_version = avcodec_version();
    (*result).avformat_version = avformat_version();
    write_message(
        result,
        "Rust FFmpeg runtime probe loaded bundled FFmpeg libraries.",
    );
    Ok(())
}

unsafe fn load_required_library(
    runtime_directory: &Path,
    library_name: &str,
) -> Result<platform::Library, c_int> {
    let library_path = runtime_directory.join(library_name);
    match platform::Library::load(&library_path) {
        Ok(library) => Ok(library),
        Err(message) => {
            ERR.with(|slot| {
                *slot.borrow_mut() = Some(format!(
                    "Could not load {}: {}",
                    library_path.display(),
                    message
                ));
            });
            Err(STATUS_LIBRARY_LOAD_FAILED)
        }
    }
}

unsafe fn load_version_symbol(
    library: &platform::Library,
    symbol: &str,
) -> Result<VersionFn, c_int> {
    load_symbol::<VersionFn>(library, symbol)
}

pub(crate) unsafe fn load_symbol<T>(library: &platform::Library, symbol: &str) -> Result<T, c_int>
where
    T: Copy,
{
    match library.symbol::<T>(symbol) {
        Ok(version) => Ok(version),
        Err(message) => {
            ERR.with(|slot| {
                *slot.borrow_mut() = Some(format!("Could not resolve {}: {}", symbol, message));
            });
            Err(STATUS_SYMBOL_LOAD_FAILED)
        }
    }
}

pub(crate) unsafe fn load_runtime_libraries(
    runtime_directory: &Path,
) -> Result<RuntimeLibraries, c_int> {
    Ok(RuntimeLibraries {
        avutil: load_required_library(runtime_directory, runtime_library_names().avutil)?,
        _swresample: load_required_library(runtime_directory, runtime_library_names().swresample)?,
        _swscale: load_required_library(runtime_directory, runtime_library_names().swscale)?,
        avcodec: load_required_library(runtime_directory, runtime_library_names().avcodec)?,
        avformat: load_required_library(runtime_directory, runtime_library_names().avformat)?,
    })
}

thread_local! {
    static ERR: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) };
}

unsafe fn write_message(result: *mut FramePlayerRustFfmpegProbeResult, message: &str) {
    let effective_message = if message.is_empty() {
        ERR.with(|slot| slot.borrow().clone())
            .unwrap_or_else(|| "Unknown Rust FFmpeg probe error.".to_string())
    } else {
        message.to_string()
    };

    let bytes = effective_message.as_bytes();
    let byte_count = bytes.len().min(MESSAGE_CAPACITY - 1);
    for (index, byte) in bytes.iter().take(byte_count).enumerate() {
        (*result).message[index] = *byte as c_char;
    }
    (*result).message[byte_count] = 0;
}

unsafe fn result_message_is_empty(result: *const FramePlayerRustFfmpegProbeResult) -> bool {
    result.is_null() || (*result).message[0] == 0
}

struct RuntimeLibraryNames {
    avutil: &'static str,
    swresample: &'static str,
    swscale: &'static str,
    avcodec: &'static str,
    avformat: &'static str,
}

pub(crate) struct RuntimeLibraries {
    pub avutil: platform::Library,
    pub _swresample: platform::Library,
    pub _swscale: platform::Library,
    pub avcodec: platform::Library,
    pub avformat: platform::Library,
}

#[cfg(target_os = "windows")]
fn runtime_library_names() -> RuntimeLibraryNames {
    RuntimeLibraryNames {
        avutil: "avutil-60.dll",
        swresample: "swresample-6.dll",
        swscale: "swscale-9.dll",
        avcodec: "avcodec-62.dll",
        avformat: "avformat-62.dll",
    }
}

#[cfg(target_os = "macos")]
fn runtime_library_names() -> RuntimeLibraryNames {
    RuntimeLibraryNames {
        avutil: "libavutil.60.dylib",
        swresample: "libswresample.6.dylib",
        swscale: "libswscale.9.dylib",
        avcodec: "libavcodec.62.dylib",
        avformat: "libavformat.62.dylib",
    }
}

#[cfg(all(unix, not(target_os = "macos")))]
fn runtime_library_names() -> RuntimeLibraryNames {
    RuntimeLibraryNames {
        avutil: "libavutil.so.60",
        swresample: "libswresample.so.6",
        swscale: "libswscale.so.9",
        avcodec: "libavcodec.so.62",
        avformat: "libavformat.so.62",
    }
}

#[cfg(target_os = "windows")]
mod platform {
    use super::{c_char, c_void, CString};
    use std::os::windows::ffi::OsStrExt;
    use std::path::Path;

    #[link(name = "kernel32")]
    extern "system" {
        fn LoadLibraryW(file_name: *const u16) -> *mut c_void;
        fn GetProcAddress(module: *mut c_void, proc_name: *const c_char) -> *mut c_void;
        fn FreeLibrary(module: *mut c_void) -> i32;
        fn GetLastError() -> u32;
    }

    pub struct Library {
        handle: *mut c_void,
    }

    impl Library {
        pub unsafe fn load(path: &Path) -> Result<Self, String> {
            let mut wide_path: Vec<u16> = path
                .as_os_str()
                .encode_wide()
                .chain(std::iter::once(0))
                .collect();
            let handle = LoadLibraryW(wide_path.as_mut_ptr());
            if handle.is_null() {
                return Err(format!(
                    "LoadLibraryW failed with Win32 error {}",
                    GetLastError()
                ));
            }

            Ok(Self { handle })
        }

        pub unsafe fn symbol<T>(&self, symbol: &str) -> Result<T, String>
        where
            T: Copy,
        {
            let symbol_name =
                CString::new(symbol).map_err(|_| "symbol name contained NUL".to_string())?;
            let address = GetProcAddress(self.handle, symbol_name.as_ptr());
            if address.is_null() {
                return Err(format!(
                    "GetProcAddress failed with Win32 error {}",
                    GetLastError()
                ));
            }

            Ok(std::mem::transmute_copy::<*mut c_void, T>(&address))
        }
    }

    impl Drop for Library {
        fn drop(&mut self) {
            unsafe {
                if !self.handle.is_null() {
                    FreeLibrary(self.handle);
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn probe_loads_configured_runtime() {
        let Some(runtime_directory) = std::env::var_os("FRAMEPLAYER_FFMPEG_RUNTIME_DIR") else {
            return;
        };
        let runtime_directory = CString::new(runtime_directory.to_string_lossy().as_bytes())
            .expect("runtime path should not contain NUL");
        let mut result = FramePlayerRustFfmpegProbeResult::default();

        let status =
            unsafe { frameplayer_rust_ffmpeg_probe(runtime_directory.as_ptr(), &mut result) };

        assert_eq!(STATUS_OK, status, "{}", read_message(&result));
        assert_eq!(STATUS_OK, result.status, "{}", read_message(&result));
        assert!(result.avutil_version > 0);
        assert!(result.avcodec_version > 0);
        assert!(result.avformat_version > 0);
    }

    fn read_message(result: &FramePlayerRustFfmpegProbeResult) -> String {
        let bytes = result
            .message
            .iter()
            .take_while(|value| **value != 0)
            .map(|value| *value as u8)
            .collect::<Vec<_>>();
        String::from_utf8_lossy(&bytes).into_owned()
    }
}

#[cfg(unix)]
mod platform {
    use super::{c_char, c_int, c_void, CString};
    use std::path::Path;

    #[cfg(target_os = "macos")]
    #[link(name = "System")]
    extern "C" {
        fn dlopen(file_name: *const c_char, flags: c_int) -> *mut c_void;
        fn dlsym(handle: *mut c_void, symbol: *const c_char) -> *mut c_void;
        fn dlclose(handle: *mut c_void) -> c_int;
        fn dlerror() -> *const c_char;
    }

    #[cfg(not(target_os = "macos"))]
    #[link(name = "dl")]
    extern "C" {
        fn dlopen(file_name: *const c_char, flags: c_int) -> *mut c_void;
        fn dlsym(handle: *mut c_void, symbol: *const c_char) -> *mut c_void;
        fn dlclose(handle: *mut c_void) -> c_int;
        fn dlerror() -> *const c_char;
    }

    const RTLD_NOW: c_int = 2;

    pub struct Library {
        handle: *mut c_void,
    }

    impl Library {
        pub unsafe fn load(path: &Path) -> Result<Self, String> {
            let path_name = path_to_cstring(path)?;
            let handle = dlopen(path_name.as_ptr(), RTLD_NOW);
            if handle.is_null() {
                return Err(last_error());
            }

            Ok(Self { handle })
        }

        pub unsafe fn symbol<T>(&self, symbol: &str) -> Result<T, String>
        where
            T: Copy,
        {
            let symbol_name =
                CString::new(symbol).map_err(|_| "symbol name contained NUL".to_string())?;
            let address = dlsym(self.handle, symbol_name.as_ptr());
            if address.is_null() {
                return Err(last_error());
            }

            Ok(std::mem::transmute_copy::<*mut c_void, T>(&address))
        }
    }

    impl Drop for Library {
        fn drop(&mut self) {
            unsafe {
                if !self.handle.is_null() {
                    dlclose(self.handle);
                }
            }
        }
    }

    #[cfg(unix)]
    fn path_to_cstring(path: &Path) -> Result<CString, String> {
        use std::os::unix::ffi::OsStrExt;
        CString::new(path.as_os_str().as_bytes()).map_err(|_| "path contained NUL".to_string())
    }

    unsafe fn last_error() -> String {
        let error = dlerror();
        if error.is_null() {
            "dlopen/dlsym failed without dlerror text".to_string()
        } else {
            std::ffi::CStr::from_ptr(error)
                .to_string_lossy()
                .into_owned()
        }
    }
}
