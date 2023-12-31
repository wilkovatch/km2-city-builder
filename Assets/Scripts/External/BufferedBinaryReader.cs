//Taken from: https://www.jacksondunstan.com/articles/3568, MIT licensed as per author's comment: https://www.jacksondunstan.com/articles/3568#comment-710389

/*
Copyright 2016 Jackson Dunstan

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.IO;

using UnityEngine;

public class BufferedBinaryReader : IDisposable {
    private readonly Stream stream;
    private readonly byte[] buffer;
    private readonly int bufferSize;
    private int bufferOffset;
    private int numBufferedBytes;

    public BufferedBinaryReader(Stream stream, int bufferSize) {
        this.stream = stream;
        this.bufferSize = bufferSize;
        buffer = new byte[bufferSize];
        bufferOffset = bufferSize;
    }

    public int NumBytesAvailable { get { return Math.Max(0, numBufferedBytes - bufferOffset); } }

    public bool FillBuffer() {
        var numBytesUnread = bufferSize - bufferOffset;
        var numBytesToRead = bufferSize - numBytesUnread;
        bufferOffset = 0;
        numBufferedBytes = numBytesUnread;
        if (numBytesUnread > 0) {
            Buffer.BlockCopy(buffer, numBytesToRead, buffer, 0, numBytesUnread);
        }
        while (numBytesToRead > 0) {
            var numBytesRead = stream.Read(buffer, numBytesUnread, numBytesToRead);
            if (numBytesRead == 0) {
                return false;
            }
            numBufferedBytes += numBytesRead;
            numBytesToRead -= numBytesRead;
            numBytesUnread += numBytesRead;
        }
        return true;
    }

    public void Dispose() {
        stream.Close();
    }

    /* Below new and modified methods */

    private int readBytes = 0;

    public int GetLength() {
        return (int)stream.Length;
    }

    public byte ReadByte() {
        if (numBufferedBytes - bufferOffset <= 0) FillBuffer();
        var val = buffer[bufferOffset];
        bufferOffset += 1;
        readBytes += 1;
        return val;
    }

    public byte[] ReadBytes(int count) {
        var left = count;
        var val = new byte[count];
        while (left > 0) {
            var done = count - left;
            var thisCount = Mathf.Min(NumBytesAvailable, left);
            if (thisCount > 0) Buffer.BlockCopy(buffer, bufferOffset, val, done, thisCount);
            bufferOffset += thisCount;
            left -= thisCount;
            if (numBufferedBytes - bufferOffset <= 0) FillBuffer();
        }
        readBytes += count;
        return val;
    }

    public ushort ReadUInt16() {
        var val1 = ReadByte();
        var val2 = ReadByte();
        var val = (ushort)(val1 | val2 << 8);
        return val;
    }

    public short ReadInt16() {
        var val1 = ReadByte();
        var val2 = ReadByte();
        var val = (short)(val1 | val2 << 8);
        return val;
    }

    public uint ReadUInt32() {
        var val1 = ReadByte();
        var val2 = ReadByte();
        var val3 = ReadByte();
        var val4 = ReadByte();
        var val = (uint)(val1 | val2 << 8 | val3 << 16 | val4 << 24);
        return val;
    }

    public int ReadInt32() {
        var val1 = ReadByte();
        var val2 = ReadByte();
        var val3 = ReadByte();
        var val4 = ReadByte();
        var val = (int)(val1 | val2 << 8 | val3 << 16 | val4 << 24);
        return val;
    }

    public bool ReachedEnd() {
        return readBytes >= stream.Length;
    }
}