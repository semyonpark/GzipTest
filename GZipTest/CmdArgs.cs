using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GZipTest
{
    public class CmdArgs
    {
        public enum CommandTypes
        {
            None,
            Compress,
            Decompress,
        }

        public CmdArgs(string[] args)
        {
            if (args.Length < 3)
                return;

            Command = ParseCommandType(args[0]);
            SourceFile = args[1];
            DestinationFile = args[2];

            if (args.Length > 3 && long.TryParse(args[3], out long blockSize))
                BlockSize = blockSize;
        }

        public CommandTypes Command { get; }

        public string SourceFile { get; }

        public string DestinationFile { get; }

        public long BlockSize { get; } = 1024L * 1024L * 1024L * 32L; // Default block size is 32 GByte

        public bool IsValid => Command != CommandTypes.None && !string.IsNullOrEmpty(SourceFile) && !string.IsNullOrEmpty(SourceFile);

        public string GetExceptionMessage()
        {
            StringBuilder exceptionMessage = new StringBuilder();
            if (Command == CommandTypes.None)
                exceptionMessage.Append("Command is invalid. ");

            if (string.IsNullOrEmpty(SourceFile))
                exceptionMessage.Append("Source file is empty. ");
        
            if (string.IsNullOrEmpty(DestinationFile))
                exceptionMessage.Append("Destination file is empty. ");

            if (BlockSize <= 0)
                exceptionMessage.Append("Archive block size must be greater then 0");

            return exceptionMessage.ToString();
        }

        private CommandTypes ParseCommandType(string commandName)
        {
            if (commandName.Equals("compress", StringComparison.InvariantCultureIgnoreCase))
                return CommandTypes.Compress;

            if (commandName.Equals("decompress", StringComparison.InvariantCultureIgnoreCase))
                return CommandTypes.Decompress;

            return CommandTypes.None;
        }

    }
}
