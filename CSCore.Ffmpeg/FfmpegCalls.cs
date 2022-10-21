﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CSCore.Ffmpeg.Interops;

namespace CSCore.Ffmpeg
{
    internal class FfmpegCalls
    {
        [Flags]
        public enum SeekFlags
        {
            SeekSet = 0,
            SeekCur = 1,
            SeekEnd = 2,
            SeekSize = 0x10000,
            SeekForce = 0x20000
        }

        public delegate int AvioReadData(IntPtr opaque, IntPtr buffer, int bufferSize);

        public delegate int AvioWriteData(IntPtr opaque, IntPtr buffer, int bufferSize);

        public delegate long AvioSeek(IntPtr opaque, long offset, SeekFlags whence);

        static FfmpegCalls()
        {
            string platform;
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                    platform = "windows";
                    break;
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    platform = "unix";
                    break;
                default:
                    throw new PlatformNotSupportedException();
            }
            var assembly = Assembly.GetEntryAssembly();
            if (assembly != null)
            {
                var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
                if (assemblyDirectory != null)
                {
                    string path = Path.Combine(
                        assemblyDirectory,
                        Path.Combine("FFmpeg", Path.Combine("bin",
                            Path.Combine(platform, IntPtr.Size == 8 ? "x64" : "x86"))));

                    InteropHelper.RegisterLibrariesSearchPath(path);
                }

                ffmpeg.av_register_all();
                ffmpeg.avcodec_register_all();
            }
            else
            {
                Debug.WriteLine($"Failed to load or initialize ffmpeg libraries.");
            }
        }

        internal static void RegisterFFmpegWithNewPath(string pathToFfmpegBins)
        {
            InteropHelper.RegisterLibrariesSearchPath(pathToFfmpegBins);
            ffmpeg.av_register_all();
            ffmpeg.avcodec_register_all();
        }

        internal static unsafe IntPtr AvMalloc(int bufferSize)
        {
            void* buffer = ffmpeg.av_malloc((ulong) bufferSize);
            IntPtr ptr = new IntPtr(buffer);
            if(ptr == IntPtr.Zero)
                throw new OutOfMemoryException("Could not allocate memory.");
            return ptr;
        }

        internal static unsafe void AvFree(IntPtr buffer)
        {
            ffmpeg.av_free((void*)buffer);
        }

        internal static unsafe AVIOContext* AvioAllocContext(AvioBuffer buffer, bool writeable, IntPtr userData,
            AvioReadData readData, AvioWriteData writeData, AvioSeek seek)
        {
            byte* bufferPtr = (byte*)buffer.Buffer;

            var avioContext = ffmpeg.avio_alloc_context(
                bufferPtr,
                buffer.BufferSize,
                writeable ? 1 : 0,
                (void*) userData,
                readData, writeData, seek);
            if (avioContext == null)
            {
                throw new FfmpegException("Could not allocate avio-context.", "avio_alloc_context");
            }

            return avioContext;
        }

        internal static unsafe AVFormatContext* AvformatAllocContext()
        {
            var formatContext = ffmpeg.avformat_alloc_context();
            if (formatContext == null)
            {
                throw new FfmpegException("Could not allocate avformat-context.", "avformat_alloc_context");
            }

            return formatContext;
        }

        internal static unsafe void AvformatOpenInput(AVFormatContext** formatContext, AvioContext avioContext)
        {
            (*formatContext)->pb = (AVIOContext*) avioContext.ContextPtr;
            int result = ffmpeg.avformat_open_input(formatContext, "DUMMY-FILENAME", null, null);
            FfmpegException.Try(result, "avformat_open_input");
        }

        internal static unsafe void AvformatOpenInput(AVFormatContext** formatContext, string url)
        {
            int result = ffmpeg.avformat_open_input(formatContext, url, null, null);
            FfmpegException.Try(result, "avformat_open_input");
        }

        internal static unsafe void AvformatCloseInput(AVFormatContext** formatContext)
        {
            ffmpeg.avformat_close_input(formatContext);
        }

        internal static unsafe void AvFormatFindStreamInfo(AVFormatContext* formatContext)
        {
            int result = ffmpeg.avformat_find_stream_info(formatContext, null);
            FfmpegException.Try(result, "avformat_find_stream_info");
        }

        internal static unsafe int AvFindBestStreamInfo(AVFormatContext* formatContext)
        {
            int result = ffmpeg.av_find_best_stream(
                formatContext,
                AVMediaType.AVMEDIA_TYPE_AUDIO,
                -1, -1, null, 0);
            FfmpegException.Try(result, "av_find_best_stream");

            return result; //stream index
        }

        internal static unsafe AVCodec* AvCodecFindDecoder(Interops.AVCodecID codecId)
        {
            var decoder = ffmpeg.avcodec_find_decoder(codecId);
            if (decoder == null)
            {
                throw new FfmpegException(
                    String.Format("Failed to find a decoder for CodecId {0}.", codecId),
                    "avcodec_find_decoder");
            }
            return decoder;
        }

        internal static unsafe void AvCodecOpen(AVCodecContext* codecContext, AVCodec* codec)
        {
            int result = ffmpeg.avcodec_open2(codecContext, codec, null);
            FfmpegException.Try(result, "avcodec_open2");
        }

        internal static unsafe void AvCodecClose(AVCodecContext* codecContext)
        {
            ffmpeg.avcodec_close(codecContext);
        }

        internal static unsafe AVFrame* AvFrameAlloc()
        {
            var frame = ffmpeg.av_frame_alloc();
            if (frame == null)
            {
                throw new FfmpegException("Could not allocate frame.", "av_frame_alloc");
            }

            return frame;
        }

        internal static unsafe void AvFrameFree(AVFrame* frame)
        {
            ffmpeg.av_frame_free(&frame);
        }

        internal static unsafe void InitPacket(AVPacket* packet)
        {
            ffmpeg.av_init_packet(packet);
        }

        internal static unsafe void FreePacket(AVPacket* packet)
        {
            ffmpeg.av_packet_unref(packet);
        }

        internal static unsafe bool AvReadFrame(AvFormatContext formatContext, AVPacket* packet)
        {
            int result = ffmpeg.av_read_frame((AVFormatContext*) formatContext.FormatPtr, packet);
            return result >= 0;
        }

        internal static unsafe bool AvCodecDecodeAudio4(AVCodecContext* codecContext, AVFrame* frame, AVPacket* packet, out int bytesConsumed)
        {
            int gotFrame;
            int result = ffmpeg.avcodec_decode_audio4(codecContext, frame, &gotFrame, packet);
            FfmpegException.Try(result, "avcodec_decode_audio4");
            bytesConsumed = result;
            return gotFrame != 0;
        }

        internal static int AvGetBytesPerSample(AVSampleFormat sampleFormat)
        {
            int dataSize = ffmpeg.av_get_bytes_per_sample(sampleFormat);
            if (dataSize <= 0)
            {
                throw new FfmpegException("Could not calculate data size.");
            }
            return dataSize;
        }

        internal static unsafe int AvSamplesGetBufferSize(AVFrame* frame)
        {
            int result = ffmpeg.av_samples_get_buffer_size(null, frame->channels, frame->nb_samples,
                (AVSampleFormat) frame->format, 1);
            FfmpegException.Try(result, "av_samples_get_buffer_size");
            return result;
        }

        internal static unsafe void AvFormatSeekFile(AvFormatContext formatContext, double time)
        {
            int result = ffmpeg.avformat_seek_file((AVFormatContext*) formatContext.FormatPtr,
                formatContext.BestAudioStreamIndex, long.MinValue, (long) time, (long) time, 0);

            FfmpegException.Try(result, "avformat_seek_file");
        }

        internal static unsafe string AvStrError(int errorCode)
        {
            byte* buffer = stackalloc byte[500];
            int result = ffmpeg.av_strerror(errorCode, new IntPtr(buffer), 500);
            if (result < 0)
                return "No description available.";
            var errorMessage = Marshal.PtrToStringAnsi(new IntPtr(buffer), 500).Trim('\0').Trim();
#if DEBUG
            Debug.WriteLineIf(Debugger.IsAttached, errorMessage);
#endif
            return errorMessage;
        }
    }
}
