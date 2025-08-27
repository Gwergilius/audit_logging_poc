using System;
using System.IO;
using System.Text;

namespace AuditLoginPOC.Core.Streams
{
    /// <summary>
    /// Stream wrapper that captures data as it flows through
    /// </summary>
    public class AuditableStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly MemoryStream _capturedData;
        
        public AuditableStream(Stream innerStream)
        {
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _capturedData = new MemoryStream();
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _innerStream.Read(buffer, offset, count);
            if (bytesRead > 0)
            {
                // Capture data as it flows through the stream
                _capturedData.Write(buffer, offset, bytesRead);
            }
            return bytesRead;
        }
        
        public string GetCapturedData()
        {
            return Encoding.UTF8.GetString(_capturedData.ToArray());
        }
        
        // Stream interface implementation...
        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position 
        { 
            get => _innerStream.Position; 
            set => _innerStream.Position = value; 
        }
        
        public override void Flush() => _innerStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) 
            => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) 
            => _innerStream.Write(buffer, offset, count);
    }
}
