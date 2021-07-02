using System;
using System.Collections.Generic;

namespace GZipTest
{
    public class FileBlock
    {
        /// <summary>
        /// Data size about block
        /// </summary>
        public const  int BlockSize = 16;

        public FileBlock(int id, long filePosition, int length)
        {
            Id = id;
            FilePosition = filePosition;
            Length = length;
        }

        public int Id { get; }

        public long FilePosition { get; }

        public int Length { get; }

        public byte[] ToByteArray()
        {
            List<byte> data = new List<byte>(BlockSize);

            data.AddRange( BitConverter.GetBytes(Id));
            data.AddRange(BitConverter.GetBytes(FilePosition));
            data.AddRange(BitConverter.GetBytes(Length));
            
            return data.ToArray();
        }
    }
}
