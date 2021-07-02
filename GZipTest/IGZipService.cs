using System;
using System.Threading;

namespace GZipTest
{
    public interface IGZipService
    {
        event Action<Exception> ExceptionThrowed;

        /// <summary>
        /// Creates a GZip archive.
        /// </summary>
        /// <param name="sourceFile">The path to the file to be archived, specified as an absolute path.</param>
        /// <param name="archiveFile">The path of the archive to be created, specified as an absolute path.</param>
        /// <param name="maxBlockSize">The archive block size</param>
        int Compress(string sourceFile, string archiveFile);

        /// <summary>
        /// Decompress Gzip archive (with archive blocks) to the destination file
        /// </summary>
        /// <param name="archiveFile">Archive file</param>
        /// <param name="destinationFile">Decompressed file</param>
        int Decompress(string archiveFile, string destinationFile);

    }
}
