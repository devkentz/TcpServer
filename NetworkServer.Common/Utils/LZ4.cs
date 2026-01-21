using System.Buffers;
using System.Diagnostics;
using Google.Protobuf;
using K4os.Compression.LZ4;
using Microsoft.IO;
using NetworkServer.ProtoGenerator;

namespace Network.Server.Common.Utils
{
    public class Lz4
    {
        private const int MaxBufferSize = 1024 * 1024 * 2; // 2MB
        private byte[] _compressBuffer = new byte[1024 * 10];
        private byte[] _depressBuffer = new byte[1024 * 10];

        // 버퍼 크기를 확장하는 메서드
        private void EnsureBufferSize(ref byte[] buffer, int requiredSize)
        {
            if (requiredSize > MaxBufferSize)
            {
                throw new ArgumentException($"Required buffer size ({requiredSize} bytes) exceeds the maximum allowed size ({MaxBufferSize} bytes).");
            }

            if (buffer.Length < requiredSize)
            {
                int newSize = Math.Min(requiredSize * 2, MaxBufferSize);
                buffer = new byte[newSize]; // 새로운 배열 생성
            }
        }

        public ReadOnlySpan<byte> Compress(ReadOnlySpan<byte> input)
        {
            int maxCompressedSize = LZ4Codec.MaximumOutputSize(input.Length);

            // 버퍼 크기 확인 및 확장
            EnsureBufferSize(ref _compressBuffer, maxCompressedSize);

            // LZ4 압축 수행
            int compressedSize = LZ4Codec.Encode(
                input, // 입력 데이터
                _compressBuffer.AsSpan(0, maxCompressedSize) // 출력 버퍼
            );

            // 압축된 데이터를 ReadOnlySpan으로 반환
            return _compressBuffer.AsSpan(0, compressedSize);
        }

        public int Decompress(ReadOnlySpan<byte> compressed, Span<byte> output)
        {
            // LZ4 압축 해제 수행
            return LZ4Codec.Decode(
                compressed, // 입력 데이터
                output // 출력 버퍼
            );
        }

        public ReadOnlySpan<byte> Decompress(ReadOnlySpan<byte> compressed, int originalSize)
        {
            // 버퍼 크기 확인 및 확장
            EnsureBufferSize(ref _depressBuffer, originalSize);

            // LZ4 압축 해제 수행
            int decodedSize = LZ4Codec.Decode(
                compressed, // 입력 데이터
                _depressBuffer // 출력 버퍼
            );

            // 압축 해제된 크기가 원본 크기와 일치하지 않으면 오류
            if (decodedSize != originalSize)
            {
                throw new InvalidOperationException("Decompressed size does not match original size.");
            }

            return _depressBuffer.AsSpan(0, originalSize);
        }
    }
}


public static class ProtobufCompressor
{
    // 최대 버퍼 크기 제한 (안전 장치)
    private const int MaxBufferSize = 1024 * 1024 * 2;
    private static readonly RecyclableMemoryStreamManager StreamManager = new();

    public static int Compress(IMessage message, Span<byte> output)
    {
        // 1. 직렬화
        // using을 사용하여 반드시 반환되도록 함
        using var stream = StreamManager.GetStream();

        // 중요: 스트림에 직접 써야 Position이 업데이트 됩니다.
        message.WriteTo((Stream) stream);

        int serializedSize = (int) stream.Length;

        // 2. 버퍼 가져오기 (GetBuffer는 내부 배열을 반환하며, Length보다 클 수 있음)
        ReadOnlySpan<byte> serialized = stream.GetBuffer().AsSpan(0, serializedSize);

        // 3. 압축 공간 확인
        int maxSize = LZ4Codec.MaximumOutputSize(serializedSize);
        if (output.Length < maxSize)
        {
            // output 버퍼가 너무 작으면 예외 처리 혹은 false 반환
            // 여기서는 원본 의도대로 예외 발생
            throw new ArgumentException($"Output buffer too small. Needed: {maxSize}, Got: {output.Length}");
        }

        // 4. 압축 (반환값은 실제 압축된 바이트 수)
        return LZ4Codec.Encode(serialized, output);
    }

    public static IMessage DecompressMessage(MessageParser parser, ReadOnlySpan<byte> compressed, int originalSize)
    {
        if (originalSize > MaxBufferSize)
            throw new InvalidOperationException($"Message size {originalSize} exceeds MaxBufferSize {MaxBufferSize}");

        // 1. 스트림 가져오기 (using 필수!)
        using var stream = StreamManager.GetStream();

        // 2. 버퍼 확보
        stream.SetLength(originalSize);

        // GetBuffer()를 통해 내부 배열에 접근
        var decompressBuffer = stream.GetBuffer();

        // 3. 압축 해제
        int decodedSize = LZ4Codec.Decode(compressed, decompressBuffer.AsSpan(0, originalSize));

        if (decodedSize != originalSize)
        {
            throw new InvalidOperationException(
                $"Decompressed size {decodedSize} != expected {originalSize}"
            );
        }

        return parser.ParseFrom(decompressBuffer.AsSpan(0, decodedSize));
    }
}