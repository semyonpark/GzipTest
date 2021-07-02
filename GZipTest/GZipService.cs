using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GZipTest
{
    public class GZipService : IGZipService
    {
        public event Action<Exception> ExceptionThrowed;

        /// <summary>
        /// Block size
        /// </summary>
        private const int MaxBlockSize = 1024 * 1024 * 50;

        private Queue<FileBlock> _blocksToRead;
        private Queue<FileBlock> _blocksToWrite;

        private Dictionary<int, byte[]> _readBlocks;
        private int _currentReadBlock;

        private Dictionary<int, byte[]> _writeBlocks;
        private int _currentWriteBlock;

        /// <summary>
        /// Total blocks count to compress or decompress
        /// </summary>
        private int _blocksCount;

        /// <summary>
        /// The maximum blocks count when reading and compressing or unpacking in memory
        /// </summary>
        private int _maxReadArchiveBlocks;

        public int Compress(string sourceFile, string archiveFile)
        {
            _blocksCount = 0;
            _readBlocks = new Dictionary<int, byte[]>();
            _writeBlocks = new Dictionary<int, byte[]>();

            _blocksToRead = new Queue<FileBlock>();
            _blocksToWrite = new Queue<FileBlock>();

            int compressThreadCount = Environment.ProcessorCount; // Optimal thread count for compression operations
            if (compressThreadCount < 2)
                compressThreadCount = 2;

            _maxReadArchiveBlocks = (int)(Helper.GetAvailableMemory() / MaxBlockSize / 4);

            SplitFileOnBlocks(sourceFile);
            CreateArchiveFile(archiveFile, _blocksToRead.Count * FileBlock.BlockSize + 4);

            //Thread - Read file
            ThreadResult readThreadResult = new ThreadResult();
            Thread readThread = new Thread(() => ReadBlocks(sourceFile, readThreadResult));
            readThread.Start();

            //Threads - Compress data 
            List<ThreadResult> compressThreadResults = new List<ThreadResult>();
            for (int i = 0; i < compressThreadCount; i++)
            {
                ThreadResult threadResult = new ThreadResult();
                compressThreadResults.Add(threadResult);

                Thread uncompressThread = new Thread(() => ArchiveBlocks(threadResult));
                uncompressThread.Start();
            }

            //Thread - Write compressed data to archive file
            ThreadResult writeThreadResult = new ThreadResult();
            Thread writeThread = new Thread(() => WriteCompressedBlocksData(archiveFile, writeThreadResult));
            writeThread.Start();

            //Wait all threads
            readThread.Join();
            WaitHandle.WaitAll(compressThreadResults.Select(x => x.WaitHandle).ToArray());
            writeThread.Join();

            WriteBlocksInfoToFile(archiveFile);

            return readThreadResult.Success &&
                   writeThreadResult.Success &&
                   compressThreadResults.All(x => x.Success) ?
                   0 : 1;
            ;
        }

        private void SplitFileOnBlocks(string filePath)
        {
            long fileLength = new FileInfo(filePath).Length;
            long filePosition = 0;
            int index = 0;
            while (fileLength > 0)
            {
                _blocksToRead.Enqueue(new FileBlock(index, filePosition, fileLength < MaxBlockSize ? (int)fileLength : MaxBlockSize));

                index++;
                filePosition += MaxBlockSize;
                fileLength -= MaxBlockSize;
            }

            _blocksCount = _blocksToRead.Count;
        }

        /// <summary>
        /// Create archive file
        /// </summary>
        /// <param name="archiveFile">Archive file path</param>
        /// <param name="reservedBytesCount">Reserved bytes for archive file header. It contains information about blocks</param>
        private void CreateArchiveFile(string archiveFile, int reservedBytesCount)
        {
            using (FileStream fs = File.Create(archiveFile, reservedBytesCount))
            {
                fs.Write(new byte[reservedBytesCount], 0, reservedBytesCount);
            }
        }

        private void ReadBlocks(string archiveFile, ThreadResult threadResult)
        {
            try
            {
                while (_blocksToRead.Count > 0)
                {
                    int readBlocksCount = 0;
                    lock (_readBlocks)
                        readBlocksCount = _readBlocks.Count;

                    int writeBlocksCount = 0;
                    lock (_writeBlocks)
                        writeBlocksCount = _writeBlocks.Count;

                    if (readBlocksCount <= _maxReadArchiveBlocks && writeBlocksCount <= _maxReadArchiveBlocks)
                    {
                        FileBlock fileBlock = _blocksToRead.Dequeue();
                        ReadBlock(fileBlock, archiveFile);
                    }
                    else
                    {
                        while (_readBlocks.Count >= _maxReadArchiveBlocks && _writeBlocks.Count > _maxReadArchiveBlocks / 2)
                        {
                            Thread.Sleep(10);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                threadResult.Exception = ex;
                ExceptionThrowed?.Invoke(ex);
            }
            finally
            {
                threadResult.WaitHandle.Set();
                //_readCompleted = true;
            }
        }

        private void ReadBlock(FileBlock fileBlock, string sourceFile)
        {
            using (FileStream archiveFileStream = File.OpenRead(sourceFile))
            {
                archiveFileStream.Position = fileBlock.FilePosition;
                using (MemoryStream blockMemoryStream = new MemoryStream())
                {
                    ReadAndWriteToStream(archiveFileStream, blockMemoryStream, MaxBlockSize, fileBlock.Length);
                    lock (_readBlocks)
                    {
                        _readBlocks.Add(fileBlock.Id, blockMemoryStream.ToArray());
                    }
                }
            }
        }

        private void ArchiveBlocks(ThreadResult threadResult)
        {
            try
            {
                while (_currentReadBlock < _blocksCount)
                {
                    int blockId = 0;
                    byte[] blockData = null;
                    lock (_readBlocks)
                    {
                        if (_readBlocks.Count > 0)
                        {
                            var readBlock = _readBlocks.First();
                            blockId = readBlock.Key;
                            blockData = readBlock.Value;

                            _readBlocks.Remove(blockId);
                        }
                    }
                    if (blockData == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    ArchiveBlock(blockData, blockId);

                    //if (_readCompleted && _readBlocks.Count == 0)
                    //    _gzipCompleted = true;
                }
            }
            catch (Exception ex)
            {
                threadResult.Exception = ex;
                ExceptionThrowed?.Invoke(ex);
                //_gzipCompleted = true;
            }
            finally
            {
                threadResult.WaitHandle.Set();
            }
        }

        private void ArchiveBlock(byte[] archivedBlock, int blockId)
        {
            using (MemoryStream compressionMemoryStream = new MemoryStream())
            {
                using (GZipStream compressionStream = new GZipStream(compressionMemoryStream, CompressionMode.Compress))
                {
                    compressionStream.Write(archivedBlock, 0, archivedBlock.Length);
                }
                lock (_writeBlocks)
                {
                    _writeBlocks.Add(blockId, compressionMemoryStream.ToArray());
                    _currentReadBlock++;
                }
            }
        }

        private void WriteCompressedBlocksData(string destinationFile, ThreadResult threadResult)
        {
            try
            {
                while (_currentWriteBlock < _blocksCount)
                {
                    int blockId = 0;
                    byte[] blockData = null;
                    lock (_writeBlocks)
                    {
                        if (_writeBlocks.Count > 0)
                        {
                            var writeBlock = _writeBlocks.First();
                            blockId = writeBlock.Key;
                            blockData = writeBlock.Value;

                            _writeBlocks.Remove(blockId);
                        }
                    }
                    if (blockData == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    long filePosition = 0;

                    using (FileStream fileStream = File.OpenWrite(destinationFile))
                    {
                        filePosition = fileStream.Length;
                        fileStream.Position = filePosition;
                        fileStream.Write(blockData, 0, blockData.Length);
                    }

                    _blocksToWrite.Enqueue(new FileBlock(blockId, filePosition, blockData.Length));

                    _currentWriteBlock++;
                }

            }
            catch (Exception ex)
            {
                threadResult.Exception = ex;
                ExceptionThrowed?.Invoke(ex);
            }
            finally
            {
                threadResult.WaitHandle.Set();
            }
        }

        /// <summary>
        /// Write archive file header information. The information about blocks
        /// </summary>
        /// <param name="archiveFile"></param>
        private void WriteBlocksInfoToFile(string archiveFile)
        {
            int blocksInfoSize = _blocksToWrite.Count * FileBlock.BlockSize;
            using (FileStream archiveFileStream = File.OpenWrite(archiveFile))
            {
                archiveFileStream.Position = 0;

                archiveFileStream.WriteInt(blocksInfoSize);

                while (_blocksToWrite.Count > 0)
                {
                    FileBlock block = _blocksToWrite.Dequeue();
                    byte[] blockInfo = block.ToByteArray();

                    archiveFileStream.Write(blockInfo, 0, blockInfo.Length);
                }
            }
        }

        private int ReadAndWriteToStream(Stream sourceStream, Stream destinationStream, int bufferSize, long numBytesToRead)
        {
            // Read the source file into a byte array.
            byte[] bytes = new byte[bufferSize];
            int numBytesRead = 0;
            while (numBytesToRead > 0)
            {
                // Read may return anything from 0 to numBytesToRead.
                int n = sourceStream.Read(bytes, 0, bufferSize);

                destinationStream.Write(bytes, 0, numBytesToRead < n ? (int)numBytesToRead : n);

                // Break when the end of the file is reached.
                if (n == 0)
                    break;

                numBytesRead += n;
                numBytesToRead -= n;
            }

            return numBytesRead;
        }

        public int Decompress(string archiveFile, string destinationFile)
        {
            _blocksCount = 0;
            _readBlocks = new Dictionary<int, byte[]>();
            _writeBlocks = new Dictionary<int, byte[]>();

            _currentReadBlock = 0;
            _currentWriteBlock = 0;

            long availableMemory = Helper.GetAvailableMemory();
            if (MaxBlockSize > availableMemory)
                throw new OutOfMemoryException($"Not enought availbale memory. Archive block size ({MaxBlockSize} bytes) should be less than available memory.");

            _maxReadArchiveBlocks = (int)(availableMemory / MaxBlockSize / 4);

            int threadCount = Environment.ProcessorCount; // Optimal thread count for decompression operations
            if (threadCount < 2)
                threadCount = 2;

            ReadArchiveBlocksInfo(archiveFile);

            using (File.Create(destinationFile)) { }

            //Thread - Read archive file
            ThreadResult readThreadResult = new ThreadResult();
            Thread readThread = new Thread(() => ReadArchiveBlocks(archiveFile, readThreadResult));
            readThread.Start();

            //Threads - Decompress data
            List<ThreadResult> decompressThreadResults = new List<ThreadResult>();
            for (int i = 0; i < threadCount; i++)
            {
                ThreadResult threadResult = new ThreadResult();
                decompressThreadResults.Add(threadResult);

                Thread uncompressThread = new Thread(() => DecompressArchiveBlocks(threadResult));
                uncompressThread.Start();
            }

            //Thread - Write decompressed data to the file
            ThreadResult writeThreadResult = new ThreadResult();
            Thread writeThread = new Thread(() => WriteUncompressedBlocksData(destinationFile, writeThreadResult));
            writeThread.Start();

            //Wait all threads
            readThread.Join();
            WaitHandle.WaitAll(decompressThreadResults.Select(x => x.WaitHandle).ToArray());
            writeThread.Join();

            return readThreadResult.Success &&
                   writeThreadResult.Success &&
                   decompressThreadResults.All(x => x.Success) ?
                   0 : 1;
        }

        private void ReadArchiveBlocksInfo(string archiveFile)
        {
            List<FileBlock> fileBlocks = new List<FileBlock>();

            using (FileStream archiveFileStream = File.OpenRead(archiveFile))
            {
                int blocksInfoSize = archiveFileStream.ReadInt();

                while (blocksInfoSize > 0)
                {
                    int id = archiveFileStream.ReadInt();
                    long filePosition = archiveFileStream.ReadLong();
                    int length = archiveFileStream.ReadInt();

                    fileBlocks.Add(new FileBlock(id, filePosition, length));

                    blocksInfoSize -= FileBlock.BlockSize;
                }
            }

            _blocksToRead = new Queue<FileBlock>(fileBlocks.OrderBy(x => x.Id));

            _blocksCount = fileBlocks.Count;
        }

        private void ReadArchiveBlocks(string archiveFile, ThreadResult threadResult)
        {
            try
            {
                while (_blocksToRead.Count > 0)
                {
                    int archivedBlocksCount = 0;
                    lock (_readBlocks)
                        archivedBlocksCount = _readBlocks.Count;

                    int uncompressedBlocksCount = 0;
                    lock (_writeBlocks)
                        uncompressedBlocksCount = _writeBlocks.Count;

                    if (archivedBlocksCount <= _maxReadArchiveBlocks && uncompressedBlocksCount <= _maxReadArchiveBlocks)
                    {
                        ReadArchiveBlock(archiveFile);
                    }
                    else
                    {
                        while (_readBlocks.Count >= _maxReadArchiveBlocks && _writeBlocks.Count > _maxReadArchiveBlocks / 2)
                        {
                            Thread.Sleep(10);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                threadResult.Exception = ex;
                ExceptionThrowed?.Invoke(ex);
            }
            finally
            {
                threadResult.WaitHandle.Set();
                //_readCompleted = true;
            }
        }

        private void ReadArchiveBlock(string archiveFile)
        {
            FileBlock fileBlock = _blocksToRead.Dequeue();
            using (FileStream archiveFileStream = File.OpenRead(archiveFile))
            {
                archiveFileStream.Position = fileBlock.FilePosition;
                using (MemoryStream blockMemoryStream = new MemoryStream())
                {
                    ReadAndWriteToStream(archiveFileStream, blockMemoryStream, MaxBlockSize, fileBlock.Length);
                    lock (_readBlocks)
                    {
                        _readBlocks.Add(fileBlock.Id, blockMemoryStream.ToArray());
                    }
                }
            }
        }

        private void DecompressArchiveBlocks(ThreadResult threadResult)
        {
            try
            {
                while (_currentReadBlock < _blocksCount)
                {
                    int blockId = 0;
                    byte[] archivedBlock = null;
                    lock (_readBlocks)
                    {
                        if (_readBlocks.Count > 0)
                            if (_readBlocks.TryGetValue(_currentReadBlock, out archivedBlock))
                            {
                                blockId = _currentReadBlock;
                                _readBlocks.Remove(_currentReadBlock);
                                _currentReadBlock++;
                            }
                    }
                    if (archivedBlock == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    DecompressArchiveBlock(archivedBlock, blockId);

                    //if (_readCompleted && _readBlocks.Count == 0)
                    //    _gzipCompleted = true;

                }
            }
            catch (Exception ex)
            {
                threadResult.Exception = ex;
                ExceptionThrowed?.Invoke(ex);
                //_gzipCompleted = true;
            }
            finally
            {
                threadResult.WaitHandle.Set();
            }
        }

        private void DecompressArchiveBlock(byte[] archivedBlock, int blockId)
        {
            using (MemoryStream uncompressedBlock = new MemoryStream())
            {
                using (GZipStream decompressionStream = new GZipStream(new MemoryStream(archivedBlock), CompressionMode.Decompress))
                {
                    while (true)
                    {
                        byte[] bytes = new byte[MaxBlockSize];
                        int n = decompressionStream.Read(bytes, 0, MaxBlockSize);
                        uncompressedBlock.Write(bytes, 0, n);
                        if (n == 0)
                            break;
                    }
                }
                lock (_writeBlocks)
                {
                    _writeBlocks.Add(blockId, uncompressedBlock.ToArray());
                }
            }
        }

        private void WriteUncompressedBlocksData(string destinationFile, ThreadResult threadResult)
        {
            try
            {
                while (_currentWriteBlock < _blocksCount)
                {
                    byte[] uncompressedBlock = null;
                    lock (_writeBlocks)
                    {
                        if (_writeBlocks.Count > 0)
                        {
                            if (_writeBlocks.TryGetValue(_currentWriteBlock, out uncompressedBlock))
                            {
                                _writeBlocks.Remove(_currentWriteBlock);
                                _currentWriteBlock++;
                            }
                        }
                    }
                    if (uncompressedBlock == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    using (FileStream fileStream = File.OpenWrite(destinationFile))
                    {
                        fileStream.Position = fileStream.Length;
                        fileStream.Write(uncompressedBlock, 0, uncompressedBlock.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                threadResult.Exception = ex;
                ExceptionThrowed?.Invoke(ex);
            }
            finally
            {
                threadResult.WaitHandle.Set();
            }
        }

    }
}
